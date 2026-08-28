"""Icon resolution and rendering.

Every pin gets a square RGBA image: the real application icon when Windows can
give us one, the site's favicon for a web link, a thumbnail for an image file,
and a coloured initials tile as the always-available fallback.
"""

from __future__ import annotations

import colorsys
import hashlib
import io
import os
import sys
import urllib.parse
import urllib.request

from PIL import Image, ImageDraw, ImageFont

from .dnd import IMAGE_EXTENSIONS
from .store import ICON_CACHE_DIR

IS_WINDOWS = sys.platform.startswith("win")

_FONT_CANDIDATES = (
    "C:\\Windows\\Fonts\\segoeuib.ttf",
    "C:\\Windows\\Fonts\\arialbd.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
)

_font_cache: dict = {}


def _font(size: int):
    if size not in _font_cache:
        font = None
        for path in _FONT_CANDIDATES:
            try:
                font = ImageFont.truetype(path, size)
                break
            except OSError:
                continue
        _font_cache[size] = font or ImageFont.load_default()
    return _font_cache[size]


def _initials(label: str) -> str:
    words = [word for word in (label or "?").replace("_", " ").replace("-", " ").split() if word]
    if not words:
        return "?"
    if len(words) == 1:
        return words[0][:2].upper()
    return (words[0][0] + words[1][0]).upper()


def _tile_color(seed: str):
    digest = hashlib.md5((seed or "?").encode("utf-8")).digest()
    hue = digest[0] / 255.0
    red, green, blue = colorsys.hsv_to_rgb(hue, 0.55, 0.85)
    return int(red * 255), int(green * 255), int(blue * 255)


def rounded_mask(size: int, radius: int = None) -> Image.Image:
    radius = size // 5 if radius is None else radius
    mask = Image.new("L", (size * 4, size * 4), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, size * 4 - 1, size * 4 - 1), radius=radius * 4, fill=255
    )
    return mask.resize((size, size), Image.LANCZOS)


def letter_tile(label: str, size: int) -> Image.Image:
    """The fallback icon: initials on a coloured rounded tile."""
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    red, green, blue = _tile_color(label)
    draw.rounded_rectangle(
        (0, 0, size - 1, size - 1), radius=max(4, size // 5), fill=(red, green, blue, 255)
    )
    text = _initials(label)
    font = _font(max(10, int(size * (0.46 if len(text) > 1 else 0.58))))
    box = draw.textbbox((0, 0), text, font=font)
    draw.text(
        ((size - (box[2] - box[0])) / 2 - box[0], (size - (box[3] - box[1])) / 2 - box[1]),
        text,
        font=font,
        fill=(255, 255, 255, 235),
    )
    return image


def fit_square(image: Image.Image, size: int) -> Image.Image:
    """Scale an image into a size x size RGBA canvas, keeping its aspect ratio."""
    image = image.convert("RGBA")
    image.thumbnail((size, size), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(image, ((size - image.width) // 2, (size - image.height) // 2), image)
    return canvas


def circular(image: Image.Image, size: int) -> Image.Image:
    """Crop an image to a circle - used for the profile picture."""
    image = image.convert("RGBA")
    scale = max(size / image.width, size / image.height)
    resized = image.resize((max(1, int(image.width * scale)), max(1, int(image.height * scale))), Image.LANCZOS)
    left, top = (resized.width - size) // 2, (resized.height - size) // 2
    cropped = resized.crop((left, top, left + size, top + size))
    mask = Image.new("L", (size * 4, size * 4), 0)
    ImageDraw.Draw(mask).ellipse((0, 0, size * 4 - 1, size * 4 - 1), fill=255)
    cropped.putalpha(mask.resize((size, size), Image.LANCZOS))
    return cropped


# --------------------------------------------------------------------------- #
# Windows shell icons
# --------------------------------------------------------------------------- #

def resolve_shortcut(path: str) -> str:
    """Return the target an .lnk points at (Windows only, best effort)."""
    if not IS_WINDOWS or not path.lower().endswith(".lnk"):
        return path
    try:
        import win32com.client  # type: ignore

        shell = win32com.client.Dispatch("WScript.Shell")
        target = shell.CreateShortCut(path).Targetpath
        return target or path
    except Exception:
        return path


def _icon_from_handle(hicon, size: int):
    """Convert an HICON to a PIL image, recovering alpha with the two-pass trick."""
    import win32gui  # type: ignore
    import win32ui  # type: ignore

    screen_handle = win32gui.GetDC(0)
    screen_dc = win32ui.CreateDCFromHandle(screen_handle)
    renders = []
    try:
        for background in (0x000000, 0xFFFFFF):
            bitmap = win32ui.CreateBitmap()
            bitmap.CreateCompatibleBitmap(screen_dc, size, size)
            memory_dc = screen_dc.CreateCompatibleDC()
            memory_dc.SelectObject(bitmap)
            memory_dc.FillSolidRect((0, 0, size, size), background)
            memory_dc.DrawIcon((0, 0), hicon)
            renders.append(
                Image.frombuffer(
                    "RGBA", (size, size), bitmap.GetBitmapBits(True), "raw", "BGRA", 0, 1
                ).convert("RGB")
            )
            win32gui.DeleteObject(bitmap.GetHandle())
            memory_dc.DeleteDC()
    finally:
        screen_dc.DeleteDC()
        win32gui.ReleaseDC(0, screen_handle)

    on_black, on_white = renders
    result = Image.new("RGBA", (size, size))
    black_pixels, white_pixels = on_black.load(), on_white.load()
    out = result.load()
    for y in range(size):
        for x in range(size):
            br, bg, bb = black_pixels[x, y]
            wr, wg, wb = white_pixels[x, y]
            alpha = 255 - max(0, min(255, ((wr - br) + (wg - bg) + (wb - bb)) // 3))
            if alpha == 0:
                out[x, y] = (0, 0, 0, 0)
            else:
                scale = 255.0 / alpha
                out[x, y] = (
                    min(255, int(br * scale)),
                    min(255, int(bg * scale)),
                    min(255, int(bb * scale)),
                    alpha,
                )
    return result


def windows_icon(path: str, size: int):
    """Ask the Windows shell for the icon of a file, folder or executable."""
    if not IS_WINDOWS:
        return None
    try:
        import win32gui  # type: ignore

        target = resolve_shortcut(path)
        handles = []
        if os.path.splitext(target)[1].lower() in (".exe", ".dll", ".ico", ".msi", ".cpl"):
            large, small = win32gui.ExtractIconEx(target, 0)
            handles = list(large) + list(small)
        if not handles:
            shgfi_icon, shgfi_large_icon = 0x000000100, 0x000000000
            _, info = win32gui.SHGetFileInfo(target, 0, shgfi_icon | shgfi_large_icon)
            handle = info[0] if isinstance(info, (tuple, list)) else info
            if handle:
                handles = [handle]
        if not handles:
            return None
        image = _icon_from_handle(handles[0], 32)
        for handle in handles:
            try:
                win32gui.DestroyIcon(handle)
            except Exception:
                pass
        return fit_square(image, size)
    except Exception:
        return None


# --------------------------------------------------------------------------- #
# Favicons
# --------------------------------------------------------------------------- #

def favicon_cache_path(url: str) -> str:
    host = urllib.parse.urlparse(url).netloc or "link"
    safe = "".join(char if char.isalnum() or char in "-._" else "_" for char in host)
    return os.path.join(ICON_CACHE_DIR, "fav_%s.png" % safe[:60])


def fetch_favicon(url: str, size: int = 64):
    """Download and cache a site icon. Returns a PIL image, or None."""
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme not in ("http", "https") or not parsed.netloc:
        return None
    cache_path = favicon_cache_path(url)
    if os.path.exists(cache_path):
        try:
            return Image.open(cache_path).convert("RGBA")
        except OSError:
            pass

    host = parsed.netloc
    sources = (
        "https://icons.duckduckgo.com/ip3/%s.ico" % host,
        "%s://%s/favicon.ico" % (parsed.scheme, host),
    )
    for source in sources:
        try:
            request = urllib.request.Request(
                source, headers={"User-Agent": "Mozilla/5.0 DesktopDock"}
            )
            with urllib.request.urlopen(request, timeout=6) as response:
                payload = response.read(1024 * 512)
            image = Image.open(io.BytesIO(payload))
            if getattr(image, "n_frames", 1) > 1:
                # .ico files hold several sizes; take the biggest.
                best, best_area = image, 0
                for frame in range(image.n_frames):
                    image.seek(frame)
                    if image.width * image.height > best_area:
                        best_area = image.width * image.height
                        best = image.copy()
                image = best
            image = fit_square(image, size)
            if image.getbbox() is None:
                continue
            os.makedirs(ICON_CACHE_DIR, exist_ok=True)
            image.save(cache_path)
            return image
        except Exception:
            continue
    return None


def local_icon(pin, size: int):
    """Resolve an icon without touching the network."""
    if pin.icon and os.path.exists(pin.icon):
        try:
            return fit_square(Image.open(pin.icon), size)
        except OSError:
            pass
    if pin.kind == "link":
        cache_path = favicon_cache_path(pin.target)
        if os.path.exists(cache_path):
            try:
                return fit_square(Image.open(cache_path), size)
            except OSError:
                pass
        return None
    extension = os.path.splitext(pin.target)[1].lower()
    if extension in IMAGE_EXTENSIONS and os.path.exists(pin.target):
        try:
            return fit_square(Image.open(pin.target), size)
        except OSError:
            pass
    return windows_icon(pin.target, size)
