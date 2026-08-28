"""Turning whatever Windows hands us on a drop into :class:`~desktopdock.store.Pin` objects.

Dropping a browser tab, a shortcut, an .exe, a folder or a chunk of text all
arrive here.  The parsing is deliberately kept free of Tk so it can be tested
on its own.
"""

from __future__ import annotations

import os
import re
import urllib.parse

from .store import Pin

APP_EXTENSIONS = {
    ".exe", ".lnk", ".bat", ".cmd", ".com", ".msi", ".ps1", ".appref-ms", ".pif",
}
IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico"}

_URL_RE = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.\-]*://\S+$")
_BARE_HOST_RE = re.compile(r"^(?:www\.)?[\w\-]+(?:\.[\w\-]+)+(?:[/?#]\S*)?$")
# An absolute Windows (C:\..., \\server\...) or POSIX (/...) path, recognised on
# any platform so a drop can be parsed the same way everywhere.
_ABS_PATH_RE = re.compile(r"^(?:[A-Za-z]:[\\/]|\\\\[^\\]|/)")


def split_tcl_list(data: str) -> list:
    """Split a Tcl list as produced by tkdnd's ``DND_Files`` payload.

    Paths containing spaces arrive wrapped in braces, e.g.
    ``{C:\\Program Files\\App\\app.exe} C:\\tools\\x.exe``.
    """
    items, current, depth, in_braces = [], [], 0, False
    index = 0
    while index < len(data):
        char = data[index]
        if char == "{" and not current and not in_braces:
            in_braces, depth = True, 1
        elif in_braces and char == "{":
            depth += 1
            current.append(char)
        elif in_braces and char == "}":
            depth -= 1
            if depth == 0:
                items.append("".join(current))
                current, in_braces = [], False
                index += 1
                while index < len(data) and data[index].isspace():
                    index += 1
                continue
            current.append(char)
        elif not in_braces and char.isspace():
            if current:
                items.append("".join(current))
                current = []
        else:
            current.append(char)
        index += 1
    if current:
        items.append("".join(current))
    return [item for item in items if item]


def normalize_path(candidate: str) -> str:
    """Convert a ``file://`` URI or a quoted path into a plain filesystem path."""
    path = (candidate or "").strip().strip('"')
    if path.lower().startswith("file:"):
        parsed = urllib.parse.urlparse(path)
        path = urllib.parse.unquote(parsed.path)
        if parsed.netloc and parsed.netloc.lower() not in ("", "localhost"):
            path = "//%s%s" % (parsed.netloc, path)
        # file:///C:/x -> C:/x
        if re.match(r"^/[A-Za-z]:", path):
            path = path[1:]
    return path


def looks_like_url(text: str) -> bool:
    """True for anything we should treat as a web link rather than a path."""
    candidate = (text or "").strip()
    if not candidate or "\n" in candidate or len(candidate) > 2048:
        return False
    if candidate.lower().startswith("file:"):
        return False
    if _URL_RE.match(candidate):
        return True
    return bool(_BARE_HOST_RE.match(candidate)) and " " not in candidate


def ensure_scheme(url: str) -> str:
    """Add ``https://`` to bare hosts such as ``example.com/page``."""
    url = (url or "").strip()
    if url and not re.match(r"^[a-zA-Z][a-zA-Z0-9+.\-]*://", url):
        if not url.lower().startswith("mailto:"):
            return "https://" + url
    return url


def label_for_url(url: str) -> str:
    """A short, friendly name for a URL: the site name, or the last path part."""
    try:
        parsed = urllib.parse.urlparse(ensure_scheme(url))
    except ValueError:
        return url
    host = (parsed.netloc or "").split("@")[-1].split(":")[0]
    host = host[4:] if host.lower().startswith("www.") else host
    tail = [part for part in (parsed.path or "").split("/") if part]
    if host:
        # Drop the public suffix and keep the domain itself: news.ycombinator.com
        # becomes "ycombinator", claude.ai becomes "claude".
        parts = [part for part in host.split(".") if part]
        name = parts[-2] if len(parts) > 1 else parts[0]
        name = name.replace("-", " ").strip()
        if tail and len(tail[-1]) <= 24:
            leaf = urllib.parse.unquote(tail[-1])
            leaf = os.path.splitext(leaf)[0].replace("-", " ").replace("_", " ")
            if leaf and leaf.lower() not in name.lower():
                return leaf.title()[:32]
        return name.title()[:32]
    return (url or "link")[:32]


def basename_any(path: str) -> str:
    """Last component of a path, splitting on both separators.

    ``os.path.basename`` only understands backslashes when running on Windows,
    and a dropped Windows path has to parse the same way everywhere.
    """
    cleaned = (path or "").rstrip("\\/")
    return re.split(r"[\\/]", cleaned)[-1] if cleaned else ""


def label_for_path(path: str) -> str:
    """A friendly name for a filesystem target."""
    cleaned = (path or "").rstrip("\\/")
    base = basename_any(cleaned) or cleaned
    stem, extension = os.path.splitext(base)
    if extension.lower() in APP_EXTENSIONS or extension.lower() in (".url",):
        base = stem
    return (base or path)[:32]


def read_url_shortcut(path: str) -> str:
    """Extract the target from a Windows ``.url`` internet shortcut."""
    try:
        with open(path, "r", encoding="utf-8", errors="ignore") as handle:
            for line in handle:
                if line.strip().lower().startswith("url="):
                    return line.split("=", 1)[1].strip()
    except OSError:
        pass
    return ""


def pin_for_path(path: str) -> "Pin | None":
    """Build a pin for a dropped file, folder, shortcut or application."""
    path = normalize_path(path)
    if not path:
        return None
    extension = os.path.splitext(path)[1].lower()
    if extension == ".url":
        url = read_url_shortcut(path)
        if url:
            return Pin("link", label_for_path(path), url)
        return None
    if os.path.isdir(path):
        return Pin("folder", label_for_path(path), path)
    if extension in APP_EXTENSIONS:
        return Pin("app", label_for_path(path), path)
    return Pin("file", label_for_path(path), path)


def pin_for_url(url: str, label: str = "") -> "Pin | None":
    url = ensure_scheme(url)
    if not url:
        return None
    return Pin("link", label.strip() or label_for_url(url), url)


def pins_from_drop(payload: str) -> list:
    """Parse a raw tkdnd payload into pins.

    Handles file lists, ``file://`` URI lists, plain URLs (a browser tab drag),
    and the ``url\\ntitle`` pairs Firefox sends.
    """
    payload = (payload or "").strip()
    if not payload:
        return []

    lines = [line.strip() for line in payload.splitlines() if line.strip()]
    if len(lines) == 2 and looks_like_url(lines[0]) and not looks_like_url(lines[1]):
        pin = pin_for_url(lines[0], lines[1])
        return [pin] if pin else []

    pins: list = []
    candidates: list = []
    for line in lines:
        candidates.extend(split_tcl_list(line) if ("{" in line or len(lines) == 1) else [line])

    for candidate in candidates:
        if looks_like_url(candidate):
            pin = pin_for_url(candidate)
        else:
            path = normalize_path(candidate)
            if os.path.exists(path) or _ABS_PATH_RE.match(path):
                pin = pin_for_path(path)
            elif looks_like_url(candidate.strip("<>")):
                pin = pin_for_url(candidate.strip("<>"))
        if pin is not None:
            pins.append(pin)
    return pins
