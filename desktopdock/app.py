"""The floating dock window."""

from __future__ import annotations

import contextlib
import os
import queue
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, simpledialog

from PIL import Image, ImageDraw, ImageTk

from . import icons, store, system
from .dnd import ensure_scheme, label_for_url, pin_for_path, pins_from_drop
from .store import DATA_FILE, Pin

try:  # drag and drop is what makes this app worth using, but it stays optional
    from tkinterdnd2 import DND_FILES, DND_TEXT, TkinterDnD

    DND_AVAILABLE = True
except Exception:  # pragma: no cover - only hit when tkinterdnd2 is missing
    DND_AVAILABLE = False
    DND_FILES = DND_TEXT = None
    TkinterDnD = None

BG = "#1b1e26"
BG_HOVER = "#2b3140"
BORDER = "#3a4150"
ACCENT = "#5b8cff"
TEXT = "#e6e9ef"
MUTED = "#8b93a5"

PAD = 6


class Dock:
    """A small always-on-top window holding pinned apps, files and links."""

    def __init__(self):
        self.data = store.load()
        self.root = TkinterDnD.Tk() if DND_AVAILABLE else tk.Tk()
        self.root.title("DesktopDock")

        self.photos: dict = {}
        self.tile_widgets: list = []
        self.icon_queue: "queue.Queue" = queue.Queue()
        self.drag = None
        self.window_drag = None
        self.position = None
        self.tooltip = None
        self.tooltip_after = None
        self._pending_icons = set()
        self._failed_icons = set()

        self._build_window()
        self._build_widgets()
        self.render()
        self.root.after(200, self._drain_icon_queue)

    # ------------------------------------------------------------------ setup
    @property
    def icon_size(self) -> int:
        return max(24, min(128, self.data.get_int("icon_size", 48)))

    @property
    def vertical(self) -> bool:
        return self.data.get("orientation", "vertical") != "horizontal"

    def _build_window(self) -> None:
        self.root.overrideredirect(True)
        self.root.configure(bg=BORDER)
        self.root.attributes("-topmost", self.data.get_bool("always_on_top", True))
        try:
            self.root.attributes("-alpha", max(0.3, min(1.0, self.data.get_float("opacity", 0.96))))
        except tk.TclError:
            pass
        system.hide_from_taskbar(self.root)
        self.root.geometry("+%d+%d" % (self.data.get_int("x", 80), self.data.get_int("y", 80)))
        self.root.protocol("WM_DELETE_WINDOW", self.quit)
        self.root.bind("<Control-q>", lambda _event: self.quit())

    def _build_widgets(self) -> None:
        # A 1px border is drawn by the root's background showing through.
        self.container = tk.Frame(self.root, bg=BG)
        self.container.pack(padx=1, pady=1, fill="both", expand=True)

        self.header = tk.Frame(self.container, bg=BG)
        self.avatar = tk.Label(self.header, bg=BG, cursor="hand2", bd=0)
        self.avatar.bind("<ButtonPress-1>", self._avatar_press)
        self.avatar.bind("<B1-Motion>", self._window_motion)
        self.avatar.bind("<ButtonRelease-1>", self._avatar_release)
        self.avatar.bind("<Button-3>", self.show_menu)

        self.separator = tk.Frame(self.container, bg=BORDER)
        self.items = tk.Frame(self.container, bg=BG)
        self.footer = tk.Frame(self.container, bg=BG)

        self.add_button = tk.Label(
            self.footer, text="+", bg=BG, fg=MUTED, cursor="hand2",
            font=("Segoe UI", 14, "bold"), bd=0,
        )
        self.add_button.bind("<Button-1>", self.show_menu)
        self.add_button.bind("<Enter>", lambda _e: self.add_button.configure(fg=TEXT, bg=BG_HOVER))
        self.add_button.bind("<Leave>", lambda _e: self.add_button.configure(fg=MUTED, bg=BG))
        self._bind_tooltip(self.add_button, "Add an app, file or link")

        for widget in (self.root, self.container, self.header, self.items, self.footer, self.avatar):
            self._enable_drop(widget)
        for widget in (self.container, self.header, self.items, self.footer):
            self._enable_window_drag(widget)
            widget.bind("<Button-3>", self.show_menu)

    # ------------------------------------------------------------ drag & drop
    def _enable_drop(self, widget) -> None:
        if not DND_AVAILABLE:
            return
        try:
            widget.drop_target_register(DND_FILES, DND_TEXT)
        except Exception:
            return
        widget.dnd_bind("<<DropEnter>>", self._on_drag_enter)
        widget.dnd_bind("<<DropLeave>>", self._on_drag_leave)
        widget.dnd_bind("<<Drop>>", self._on_drop)

    def _on_drag_enter(self, event):
        self.root.configure(bg=ACCENT)
        return event.action

    def _on_drag_leave(self, event):
        self.root.configure(bg=BORDER)
        return event.action

    def _on_drop(self, event):
        self.root.configure(bg=BORDER)
        added = 0
        for pin in pins_from_drop(getattr(event, "data", "") or ""):
            if not any(existing.target == pin.target for existing in self.data.pins):
                self.data.pins.append(pin)
                added += 1
        if added:
            self.save()
            self.render()
        return getattr(event, "action", None)

    # ------------------------------------------------------------ window move
    def _enable_window_drag(self, widget) -> None:
        widget.bind("<ButtonPress-1>", self._window_press)
        widget.bind("<B1-Motion>", self._window_motion)
        widget.bind("<ButtonRelease-1>", self._window_release)

    def _window_press(self, event):
        if self.data.get_bool("locked", False):
            self.window_drag = None
            return
        self.window_drag = {
            "dx": event.x_root - self.root.winfo_x(),
            "dy": event.y_root - self.root.winfo_y(),
            "moved": False,
        }
        self._hide_tooltip()

    def _window_motion(self, event):
        if not self.window_drag:
            return
        x = event.x_root - self.window_drag["dx"]
        y = event.y_root - self.window_drag["dy"]
        self.window_drag["moved"] = True
        self.root.geometry("+%d+%d" % (x, y))
        self.position = (x, y)

    def _window_release(self, _event=None):
        if self.window_drag:
            moved = self.window_drag["moved"]
            self.window_drag = None
            if moved:
                self.remember_position()

    def _avatar_press(self, event):
        self._window_press(event)

    def _avatar_release(self, event):
        moved = bool(self.window_drag and self.window_drag["moved"])
        self._window_release(event)
        if not moved:
            self.choose_profile_picture()

    def remember_position(self) -> None:
        """Store the dock position, kept inside the visible screen area."""
        self.root.update_idletasks()
        x, y = self.position if self.position else (self.root.winfo_x(), self.root.winfo_y())
        max_x = max(0, self.root.winfo_screenwidth() - self.root.winfo_width())
        max_y = max(0, self.root.winfo_screenheight() - self.root.winfo_height())
        x, y = max(0, min(x, max_x)), max(0, min(y, max_y))
        if (x, y) != (self.root.winfo_x(), self.root.winfo_y()):
            self.root.geometry("+%d+%d" % (x, y))
        self.position = (x, y)
        self.data.set("x", x)
        self.data.set("y", y)
        self.save()

    # ----------------------------------------------------------------- layout
    def render(self) -> None:
        """Rebuild the whole dock from the current data."""
        side = "top" if self.vertical else "left"
        fill = "x" if self.vertical else "y"

        for frame in (self.header, self.separator, self.items, self.footer):
            frame.pack_forget()
        self.header.pack(side=side, fill=fill)
        self.separator.pack(side=side, fill=fill, padx=4, pady=3)
        self.separator.configure(height=1 if self.vertical else 0, width=0 if self.vertical else 1)
        self.items.pack(side=side, fill=fill)
        self.footer.pack(side=side, fill=fill)

        self._render_avatar()
        self._render_pins()

        self.add_button.pack(side=side, padx=PAD, pady=(0, PAD) if self.vertical else (PAD, PAD))
        self.root.attributes("-topmost", self.data.get_bool("always_on_top", True))
        self.root.update_idletasks()

    def _render_avatar(self) -> None:
        size = max(28, int(self.icon_size * 0.8))
        path = self.data.get("profile_pic", "")
        image = None
        if path and os.path.exists(path):
            try:
                image = icons.circular(Image.open(path), size)
            except OSError:
                image = None
        if image is None:
            image = self._placeholder_avatar(size, self.data.get("profile_name", ""))
        self.photos["avatar"] = ImageTk.PhotoImage(image)
        self.avatar.configure(image=self.photos["avatar"])
        self.avatar.pack(padx=PAD, pady=(PAD, 2) if self.vertical else (PAD, PAD))
        self._bind_tooltip(
            self.avatar,
            (self.data.get("profile_name") or "You") + " - click to change your picture",
        )

    def _placeholder_avatar(self, size: int, name: str) -> Image.Image:
        if name.strip():
            return icons.circular(icons.letter_tile(name, size), size)
        image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        draw = ImageDraw.Draw(image)
        draw.ellipse((0, 0, size - 1, size - 1), fill=(58, 65, 80, 255))
        head = size * 0.26
        draw.ellipse(
            (size / 2 - head / 2, size * 0.2, size / 2 + head / 2, size * 0.2 + head),
            fill=(139, 147, 165, 255),
        )
        draw.ellipse(
            (size * 0.22, size * 0.56, size * 0.78, size * 1.12), fill=(139, 147, 165, 255)
        )
        return image

    def _render_pins(self) -> None:
        for widget in self.items.winfo_children():
            widget.destroy()
        self.photos = {key: value for key, value in self.photos.items() if key == "avatar"}
        self.tile_widgets = []
        for pin in self.data.pins:
            self.tile_widgets.append(self._make_tile(pin))
        self._repack_tiles()

    def _make_tile(self, pin: Pin) -> tk.Label:
        size = self.icon_size
        show_labels = self.data.get_bool("show_labels", False)
        tile = tk.Label(
            self.items, bg=BG, bd=0, cursor="hand2",
            text=pin.label[:14] if show_labels else "",
            fg=TEXT, font=("Segoe UI", 8), compound="top",
        )
        tile.pin = pin
        self._set_tile_image(tile, pin, size)
        tile.bind("<Enter>", lambda _e, w=tile: w.configure(bg=BG_HOVER))
        tile.bind("<Leave>", lambda _e, w=tile: w.configure(bg=BG))
        tile.bind("<ButtonPress-1>", self._tile_press)
        tile.bind("<B1-Motion>", self._tile_motion)
        tile.bind("<ButtonRelease-1>", self._tile_release)
        tile.bind("<Button-3>", lambda event, p=pin: self.show_pin_menu(event, p))
        self._bind_tooltip(tile, "%s\n%s" % (pin.label, pin.target))
        self._enable_drop(tile)
        return tile

    def _set_tile_image(self, tile: tk.Label, pin: Pin, size: int) -> None:
        image = icons.local_icon(pin, size)
        if image is None:
            image = icons.letter_tile(pin.label or pin.target, size)
            if pin.kind == "link" and self.data.get_bool("fetch_favicons", True):
                self._request_favicon(pin)
        photo = ImageTk.PhotoImage(image)
        self.photos[id(tile)] = photo
        tile.configure(image=photo)

    def _repack_tiles(self) -> None:
        side, padx, pady = ("top", PAD, 3) if self.vertical else ("left", 3, PAD)
        for tile in self.tile_widgets:
            tile.pack_forget()
            tile.pack(side=side, padx=padx, pady=pady)

    # ------------------------------------------------------------ pin gestures
    def _tile_press(self, event):
        tile = event.widget
        self.drag = {"tile": tile, "moved": False, "x": event.x_root, "y": event.y_root}
        self._hide_tooltip()

    def _tile_motion(self, event):
        if not self.drag:
            return
        moved = abs(event.x_root - self.drag["x"]) + abs(event.y_root - self.drag["y"])
        if moved < 8:
            return
        if self.data.get_bool("locked", False):
            return
        self.drag["moved"] = True
        self.drag["tile"].configure(bg=BG_HOVER)
        target = self._index_at(event.x_root, event.y_root)
        current = self.tile_widgets.index(self.drag["tile"])
        if target != current and 0 <= target <= len(self.tile_widgets):
            if target > current:
                target -= 1
            tile = self.tile_widgets.pop(current)
            pin = self.data.pins.pop(current)
            self.tile_widgets.insert(target, tile)
            self.data.pins.insert(target, pin)
            self._repack_tiles()

    def _tile_release(self, event):
        drag, self.drag = self.drag, None
        if not drag:
            return
        drag["tile"].configure(bg=BG)
        if drag["moved"]:
            self.save()
            return
        pin = getattr(drag["tile"], "pin", None)
        if pin is not None:
            self.open_pin(pin)

    def _index_at(self, x_root: int, y_root: int) -> int:
        for index, tile in enumerate(self.tile_widgets):
            if self.vertical:
                middle = tile.winfo_rooty() + tile.winfo_height() / 2
                if y_root < middle:
                    return index
            else:
                middle = tile.winfo_rootx() + tile.winfo_width() / 2
                if x_root < middle:
                    return index
        return len(self.tile_widgets)

    # -------------------------------------------------------------- favicons
    def _request_favicon(self, pin: Pin) -> None:
        if pin.target in self._pending_icons or pin.target in self._failed_icons:
            return
        self._pending_icons.add(pin.target)

        def worker(url=pin.target):
            image = None
            try:
                image = icons.fetch_favicon(url, max(64, self.icon_size))
            except Exception:
                image = None
            self.icon_queue.put((url, image is not None))

        threading.Thread(target=worker, daemon=True).start()

    def _drain_icon_queue(self) -> None:
        refresh = False
        try:
            while True:
                url, ok = self.icon_queue.get_nowait()
                self._pending_icons.discard(url)
                if ok:
                    refresh = True
                else:
                    self._failed_icons.add(url)
        except queue.Empty:
            pass
        if refresh:
            for tile in self.tile_widgets:
                if tile.winfo_exists():
                    self._set_tile_image(tile, tile.pin, self.icon_size)
        self.root.after(500, self._drain_icon_queue)

    # --------------------------------------------------------------- tooltips
    def _bind_tooltip(self, widget, text: str) -> None:
        widget.tooltip_text = text
        widget.bind("<Enter>", lambda _e, w=widget: self._schedule_tooltip(w), add="+")
        widget.bind("<Leave>", lambda _e: self._hide_tooltip(), add="+")
        widget.bind("<ButtonPress>", lambda _e: self._hide_tooltip(), add="+")

    def _schedule_tooltip(self, widget) -> None:
        self._hide_tooltip()
        self.tooltip_after = self.root.after(500, lambda: self._show_tooltip(widget))

    def _show_tooltip(self, widget) -> None:
        text = getattr(widget, "tooltip_text", "")
        if not text or not widget.winfo_exists():
            return
        self.tooltip = tk.Toplevel(self.root)
        self.tooltip.overrideredirect(True)
        self.tooltip.attributes("-topmost", True)
        tk.Label(
            self.tooltip, text=text, bg="#0f1116", fg=TEXT, justify="left",
            font=("Segoe UI", 8), padx=6, pady=3, bd=1, relief="solid",
        ).pack()
        if self.vertical:
            x = widget.winfo_rootx() + widget.winfo_width() + 8
            y = widget.winfo_rooty()
        else:
            x = widget.winfo_rootx()
            y = widget.winfo_rooty() + widget.winfo_height() + 8
        self.tooltip.geometry("+%d+%d" % (x, y))

    def _hide_tooltip(self) -> None:
        if self.tooltip_after:
            self.root.after_cancel(self.tooltip_after)
            self.tooltip_after = None
        if self.tooltip is not None:
            self.tooltip.destroy()
            self.tooltip = None

    # ----------------------------------------------------------------- actions
    @contextlib.contextmanager
    def _dialog(self):
        """Drop 'always on top' while a dialog is open, so it cannot hide behind us."""
        on_top = self.data.get_bool("always_on_top", True)
        if on_top:
            self.root.attributes("-topmost", False)
        try:
            yield self.root
        finally:
            if on_top and self.root.winfo_exists():
                self.root.attributes("-topmost", True)

    def open_pin(self, pin: Pin) -> None:
        try:
            system.launch(pin)
        except Exception as error:
            with self._dialog():
                messagebox.showerror("DesktopDock", "Could not open %s\n\n%s" % (pin.label, error))

    def add_link(self) -> None:
        with self._dialog() as parent:
            url = simpledialog.askstring("Add link", "Web address:", parent=parent)
            if not url:
                return
            url = ensure_scheme(url.strip())
            name = simpledialog.askstring(
                "Add link", "Name:", parent=parent, initialvalue=label_for_url(url)
            )
        self.data.pins.append(Pin("link", (name or label_for_url(url)).strip(), url))
        self.save()
        self.render()

    def add_files(self) -> None:
        with self._dialog() as parent:
            paths = filedialog.askopenfilenames(
                parent=parent,
                title="Pin an application or file",
                filetypes=[
                    ("Programs and shortcuts", "*.exe *.lnk *.bat *.cmd *.url"),
                    ("All files", "*.*"),
                ],
            )
        self._add_paths(paths)

    def add_folder(self) -> None:
        with self._dialog() as parent:
            path = filedialog.askdirectory(parent=parent, title="Pin a folder")
        self._add_paths([path] if path else [])

    def _add_paths(self, paths) -> None:
        added = False
        for path in paths:
            pin = pin_for_path(path)
            if pin and not any(existing.target == pin.target for existing in self.data.pins):
                self.data.pins.append(pin)
                added = True
        if added:
            self.save()
            self.render()

    def rename_pin(self, pin: Pin) -> None:
        with self._dialog() as parent:
            name = simpledialog.askstring("Rename", "Name:", parent=parent, initialvalue=pin.label)
        if name:
            pin.label = name.strip()
            self.save()
            self.render()

    def change_icon(self, pin: Pin) -> None:
        with self._dialog() as parent:
            path = filedialog.askopenfilename(
                parent=parent,
                title="Choose an icon",
                filetypes=[
                    ("Images", "*.png *.jpg *.jpeg *.gif *.bmp *.webp *.ico"),
                    ("All files", "*.*"),
                ],
            )
        if path:
            pin.icon = path
            self.save()
            self.render()

    def reset_icon(self, pin: Pin) -> None:
        pin.icon = ""
        if pin.kind == "link":
            cache = icons.favicon_cache_path(pin.target)
            if os.path.exists(cache):
                try:
                    os.remove(cache)
                except OSError:
                    pass
        self.save()
        self.render()

    def remove_pin(self, pin: Pin) -> None:
        if pin in self.data.pins:
            self.data.pins.remove(pin)
            self.save()
            self.render()

    def copy_link(self, pin: Pin) -> None:
        self.root.clipboard_clear()
        self.root.clipboard_append(pin.target)

    def choose_profile_picture(self) -> None:
        with self._dialog() as parent:
            path = filedialog.askopenfilename(
                parent=parent,
                title="Choose your picture",
                filetypes=[
                    ("Images", "*.png *.jpg *.jpeg *.gif *.bmp *.webp"),
                    ("All files", "*.*"),
                ],
            )
        if path:
            self.data.set("profile_pic", path)
            self.save()
            self.render()

    def clear_profile_picture(self) -> None:
        self.data.set("profile_pic", "")
        self.save()
        self.render()

    def set_profile_name(self) -> None:
        with self._dialog() as parent:
            name = simpledialog.askstring(
                "Your name", "Shown when no picture is set:",
                parent=parent, initialvalue=self.data.get("profile_name", ""),
            )
        if name is not None:
            self.data.set("profile_name", name.strip())
            self.save()
            self.render()

    def apply_setting(self, key: str, value) -> None:
        self.data.set(key, value)
        if key == "opacity":
            try:
                self.root.attributes("-alpha", float(value))
            except (tk.TclError, ValueError):
                pass
        if key == "always_on_top":
            self.root.attributes("-topmost", bool(value))
        self.save()
        self.render()

    def reload_file(self) -> None:
        self.data = store.load()
        self._build_window()
        self.render()

    def save(self) -> None:
        try:
            store.save(self.data)
        except OSError as error:
            with self._dialog():
                messagebox.showerror("DesktopDock", "Could not save %s\n\n%s" % (DATA_FILE, error))

    def quit(self) -> None:
        self.remember_position()
        self.root.destroy()

    # ------------------------------------------------------------------ menus
    def show_menu(self, event=None):
        menu = tk.Menu(self.root, tearoff=0)
        menu.add_command(label="Add link...", command=self.add_link)
        menu.add_command(label="Add app or file...", command=self.add_files)
        menu.add_command(label="Add folder...", command=self.add_folder)
        menu.add_separator()

        menu.add_command(label="Set your picture...", command=self.choose_profile_picture)
        if self.data.get("profile_pic"):
            menu.add_command(label="Remove your picture", command=self.clear_profile_picture)
        menu.add_command(label="Set your name...", command=self.set_profile_name)
        menu.add_separator()

        layout = tk.Menu(menu, tearoff=0)
        for name, value in (("Vertical", "vertical"), ("Horizontal", "horizontal")):
            layout.add_command(
                label=("* " if self.data.get("orientation") == value else "   ") + name,
                command=lambda v=value: self.apply_setting("orientation", v),
            )
        layout.add_separator()
        for size in (32, 40, 48, 64, 80):
            layout.add_command(
                label=("* " if self.icon_size == size else "   ") + "Icons %dpx" % size,
                command=lambda s=size: self.apply_setting("icon_size", s),
            )
        layout.add_separator()
        for percent in (100, 95, 85, 70, 50):
            layout.add_command(
                label=("* " if round(self.data.get_float("opacity", 0.96) * 100) == percent else "   ")
                + "Opacity %d%%" % percent,
                command=lambda p=percent: self.apply_setting("opacity", p / 100.0),
            )
        menu.add_cascade(label="Appearance", menu=layout)

        toggles = (
            ("Show names", "show_labels"),
            ("Always on top", "always_on_top"),
            ("Lock position", "locked"),
            ("Fetch site icons", "fetch_favicons"),
        )
        for label, key in toggles:
            menu.add_command(
                label=("[x] " if self.data.get_bool(key) else "[  ] ") + label,
                command=lambda k=key: self.apply_setting(k, not self.data.get_bool(k)),
            )
        if system.IS_WINDOWS:
            menu.add_command(
                label=("[x] " if system.autostart_enabled() else "[  ] ") + "Start with Windows",
                command=lambda: system.set_autostart(not system.autostart_enabled()),
            )
        menu.add_separator()
        menu.add_command(label="Open pins.txt", command=lambda: system.open_in_editor(DATA_FILE))
        menu.add_command(label="Reload pins.txt", command=self.reload_file)
        if not DND_AVAILABLE:
            menu.add_command(label="(drag and drop unavailable - install tkinterdnd2)", state="disabled")
        menu.add_separator()
        menu.add_command(label="Quit DesktopDock", command=self.quit)
        self._popup(menu, event)
        return "break"

    def show_pin_menu(self, event, pin: Pin):
        menu = tk.Menu(self.root, tearoff=0)
        menu.add_command(label="Open", command=lambda: self.open_pin(pin))
        if pin.kind == "link":
            menu.add_command(label="Copy link", command=lambda: self.copy_link(pin))
        else:
            menu.add_command(label="Open file location", command=lambda: system.reveal(pin.target))
        menu.add_separator()
        menu.add_command(label="Rename...", command=lambda: self.rename_pin(pin))
        menu.add_command(label="Change icon...", command=lambda: self.change_icon(pin))
        if pin.icon or pin.kind == "link":
            menu.add_command(label="Reset icon", command=lambda: self.reset_icon(pin))
        menu.add_separator()
        menu.add_command(label="Remove from dock", command=lambda: self.remove_pin(pin))
        self._popup(menu, event)
        return "break"

    def _popup(self, menu: tk.Menu, event) -> None:
        x = getattr(event, "x_root", self.root.winfo_x())
        y = getattr(event, "y_root", self.root.winfo_y())
        try:
            menu.tk_popup(x, y)
        finally:
            menu.grab_release()

    # ------------------------------------------------------------------- main
    def run(self) -> None:
        self.root.mainloop()


def main() -> None:
    if not os.path.exists(DATA_FILE):
        seed = store.DockData()
        seed.pins = [
            Pin("link", "Claude", "https://claude.ai"),
            Pin("link", "GitHub", "https://github.com"),
        ]
        store.save(seed)
    Dock().run()
