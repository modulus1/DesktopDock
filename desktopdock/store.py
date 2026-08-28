"""Plain-text storage for DesktopDock.

Everything the app remembers lives in a single human-readable text file
(``pins.txt``) that sits in the application directory, so it can be edited by
hand, copied between machines or kept in version control.

File format::

    # comments start with '#'
    [settings]
    key = value

    [pins]
    type | label | target | icon

Only ``|``, ``%`` and newlines are escaped inside a field (as ``%7C``, ``%25``
and ``%0A``) so Windows paths stay readable.
"""

from __future__ import annotations

import os
import tempfile
from dataclasses import dataclass, field

APP_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA_FILE = os.path.join(APP_DIR, "pins.txt")
ICON_CACHE_DIR = os.path.join(APP_DIR, "icons")

PIN_TYPES = ("app", "file", "folder", "link")

DEFAULT_SETTINGS = {
    "x": "80",
    "y": "80",
    "orientation": "vertical",   # vertical | horizontal
    "icon_size": "48",
    "opacity": "0.96",
    "always_on_top": "true",
    "locked": "false",
    "show_labels": "false",
    "fetch_favicons": "true",
    "profile_pic": "",
    "profile_name": "",
}

_HEADER = (
    "# DesktopDock data file\n"
    "#\n"
    "# Pins are 'type | label | target | icon'.\n"
    "#   type   : app, file, folder or link\n"
    "#   label  : the name shown in the dock\n"
    "#   target : path to launch, or the URL to open\n"
    "#   icon   : optional path to a custom icon image (blank = automatic)\n"
    "#\n"
    "# Edit this file by hand if you like, then use 'Reload pins.txt' in the\n"
    "# dock menu. '|', '%' and newlines inside a field are written as %7C, %25\n"
    "# and %0A.\n"
)


def encode_field(value: str) -> str:
    """Escape the few characters that would break the line format."""
    return (
        (value or "")
        .replace("%", "%25")
        .replace("|", "%7C")
        .replace("\r", "")
        .replace("\n", "%0A")
    )


def decode_field(value: str) -> str:
    """Inverse of :func:`encode_field`."""
    return (
        (value or "")
        .replace("%0A", "\n")
        .replace("%7C", "|")
        .replace("%25", "%")
    )


@dataclass
class Pin:
    """A single item in the dock."""

    kind: str
    label: str
    target: str
    icon: str = ""

    def to_line(self) -> str:
        return " | ".join(
            encode_field(v) for v in (self.kind, self.label, self.target, self.icon)
        )

    @classmethod
    def from_line(cls, line: str) -> "Pin | None":
        parts = [decode_field(p.strip()) for p in line.split("|")]
        while len(parts) < 4:
            parts.append("")
        kind, label, target, icon = parts[:4]
        kind = kind.lower()
        if kind not in PIN_TYPES or not target:
            return None
        return cls(kind=kind, label=label or target, target=target, icon=icon)

    @property
    def is_link(self) -> bool:
        return self.kind == "link"


@dataclass
class DockData:
    settings: dict = field(default_factory=lambda: dict(DEFAULT_SETTINGS))
    pins: list = field(default_factory=list)

    # -- typed settings helpers -------------------------------------------------
    def get(self, key: str, default: str = "") -> str:
        return self.settings.get(key, DEFAULT_SETTINGS.get(key, default))

    def get_int(self, key: str, default: int = 0) -> int:
        try:
            return int(float(self.get(key)))
        except (TypeError, ValueError):
            return default

    def get_float(self, key: str, default: float = 0.0) -> float:
        try:
            return float(self.get(key))
        except (TypeError, ValueError):
            return default

    def get_bool(self, key: str, default: bool = False) -> bool:
        value = str(self.get(key)).strip().lower()
        if value in ("1", "true", "yes", "on"):
            return True
        if value in ("0", "false", "no", "off"):
            return False
        return default

    def set(self, key: str, value) -> None:
        if isinstance(value, bool):
            value = "true" if value else "false"
        self.settings[key] = str(value)


def parse(text: str) -> DockData:
    """Parse the contents of a data file."""
    data = DockData()
    section = "pins"
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1].strip().lower()
            continue
        if section == "settings":
            if "=" in line:
                key, _, value = line.partition("=")
                data.settings[key.strip().lower()] = decode_field(value.strip())
        elif section == "pins":
            pin = Pin.from_line(line)
            if pin is not None:
                data.pins.append(pin)
    return data


def serialize(data: DockData) -> str:
    """Render a :class:`DockData` back to the text file format."""
    out = [_HEADER, "\n[settings]"]
    for key in sorted(data.settings):
        out.append("%s = %s" % (key, encode_field(str(data.settings[key]))))
    out.append("\n[pins]")
    for pin in data.pins:
        out.append(pin.to_line())
    return "\n".join(out) + "\n"


def load(path: str = DATA_FILE) -> DockData:
    """Load the data file, returning defaults when it does not exist yet."""
    try:
        with open(path, "r", encoding="utf-8") as handle:
            data = parse(handle.read())
    except (OSError, UnicodeDecodeError):
        return DockData()
    merged = dict(DEFAULT_SETTINGS)
    merged.update(data.settings)
    data.settings = merged
    return data


def save(data: DockData, path: str = DATA_FILE) -> None:
    """Write the data file atomically so a crash can never truncate it."""
    directory = os.path.dirname(os.path.abspath(path)) or "."
    os.makedirs(directory, exist_ok=True)
    handle = tempfile.NamedTemporaryFile(
        "w", encoding="utf-8", dir=directory, prefix=".pins-", suffix=".tmp", delete=False
    )
    try:
        with handle:
            handle.write(serialize(data))
        os.replace(handle.name, path)
    except BaseException:
        try:
            os.unlink(handle.name)
        except OSError:
            pass
        raise
