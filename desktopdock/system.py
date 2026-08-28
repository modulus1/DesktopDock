"""Small OS-level helpers: launching pins, revealing files, autostart."""

from __future__ import annotations

import os
import subprocess
import sys
import webbrowser

from .dnd import ensure_scheme
from .store import APP_DIR

IS_WINDOWS = sys.platform.startswith("win")
STARTUP_ENTRY = "DesktopDock.cmd"


def startup_dir() -> str:
    return os.path.join(
        os.environ.get("APPDATA", ""),
        "Microsoft", "Windows", "Start Menu", "Programs", "Startup",
    )


def launch(pin) -> None:
    """Open a pin: a URL in the browser, anything else through the shell."""
    if pin.kind == "link":
        webbrowser.open(ensure_scheme(pin.target))
        return
    target = os.path.expandvars(pin.target)
    if IS_WINDOWS:
        os.startfile(target)  # noqa: B606 - the documented way to launch on Windows
    elif sys.platform == "darwin":
        subprocess.Popen(["open", target])
    else:
        subprocess.Popen(["xdg-open", target])


def reveal(path: str) -> None:
    """Show a file or folder in Explorer / Finder / the default file manager."""
    path = os.path.expandvars(path or "")
    if not path:
        return
    if IS_WINDOWS:
        if os.path.isdir(path):
            os.startfile(path)  # noqa: B606
        else:
            # explorer wants the switch and the path glued into one argument.
            subprocess.Popen('explorer /select,"%s"' % os.path.normpath(path))
    elif sys.platform == "darwin":
        subprocess.Popen(["open", "-R", path])
    else:
        subprocess.Popen(["xdg-open", os.path.dirname(path) or "."])


def open_in_editor(path: str) -> None:
    """Open the data file in the user's text editor."""
    if IS_WINDOWS:
        os.startfile(path)  # noqa: B606
    elif sys.platform == "darwin":
        subprocess.Popen(["open", "-t", path])
    else:
        subprocess.Popen(["xdg-open", path])


def autostart_enabled() -> bool:
    return IS_WINDOWS and os.path.exists(os.path.join(startup_dir(), STARTUP_ENTRY))


def set_autostart(enabled: bool) -> bool:
    """Add or remove a launcher in the Windows Startup folder."""
    if not IS_WINDOWS:
        return False
    directory = startup_dir()
    entry = os.path.join(directory, STARTUP_ENTRY)
    if not enabled:
        try:
            os.remove(entry)
        except OSError:
            pass
        return False
    try:
        os.makedirs(directory, exist_ok=True)
        runtime = os.path.join(os.path.dirname(sys.executable), "pythonw.exe")
        if not os.path.exists(runtime):
            runtime = sys.executable
        script = os.path.join(APP_DIR, "DesktopDock.pyw")
        with open(entry, "w", encoding="utf-8") as handle:
            handle.write("@echo off\r\n")
            handle.write('cd /d "%s"\r\n' % APP_DIR)
            handle.write('start "" "%s" "%s"\r\n' % (runtime, script))
        return True
    except OSError:
        return False


def hide_from_taskbar(window) -> None:
    """Keep the dock out of the taskbar and out of Alt+Tab."""
    if not IS_WINDOWS:
        return
    try:
        window.attributes("-toolwindow", True)
    except Exception:
        pass
