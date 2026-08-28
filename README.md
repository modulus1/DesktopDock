# DesktopDock

A small floating dock for Windows. It sits on top of your desktop and holds the
apps, folders, files and web pages you use most. Drag a browser tab or an
`.exe` onto it and it is pinned; click it to open. Everything it remembers is
kept in one plain text file next to the app.

![The dock](docs/dock.png)

## Install

1. Install [Python 3.9+ for Windows](https://www.python.org/downloads/windows/)
   (tick *Add python.exe to PATH* in the installer).
2. Double-click **`install.bat`** — it installs the three dependencies:
   `tkinterdnd2` (drag and drop), `pillow` (icons) and `pywin32`
   (real Windows application icons).
3. Double-click **`run.bat`** to start the dock.

From a terminal instead:

```bat
python -m pip install -r requirements.txt
pythonw DesktopDock.pyw
```

## Using it

| What you want | How |
| --- | --- |
| Pin a web page | Drag the tab out of Chrome/Edge/Firefox and drop it on the dock |
| Pin an app | Drag an `.exe`, a shortcut or a Start-menu tile onto the dock |
| Pin a folder or file | Drag it from Explorer onto the dock |
| Open something | Left-click it |
| Reorder | Drag a tile up or down (left or right in horizontal mode) |
| Move the dock | Drag any empty part of it, or the profile picture |
| Rename, change icon, remove | Right-click the tile |
| Your picture | Click the round avatar at the top, or right-click → *Set your picture…* |
| Everything else | Right-click the dock, or click the **+** button |

The right-click menu also has orientation (vertical/horizontal), icon size,
opacity, name labels, always-on-top, lock position, *Start with Windows*, and
quit.

Icons are found automatically: the real application icon from Windows, the
site's favicon for a web link (cached in `icons/`), a thumbnail for an image
file, and a coloured initials tile when nothing else is available. You can
always override one with *Change icon…*.

## Where your data lives

Everything is in **`pins.txt`** in the app directory — no registry keys, no
`AppData`, nothing hidden. Put the folder on a USB stick and the dock travels
with you. The file is meant to be edited by hand; use *Reload pins.txt* in the
menu afterwards.

```ini
[settings]
x = 1180
y = 220
orientation = vertical
icon_size = 48
opacity = 0.96
always_on_top = true
locked = false
show_labels = false
fetch_favicons = true
profile_pic = C:\Users\me\Pictures\me.jpg
profile_name = Michael

[pins]
link | Claude | https://claude.ai |
app | Notepad | C:\Windows\notepad.exe |
folder | Projects | C:\Users\me\Projects |
file | Budget | C:\Users\me\budget.xlsx |
```

Each pin is `type | label | target | icon`, where `type` is `app`, `file`,
`folder` or `link` and `icon` is an optional path to a custom image. Inside a
field, `|`, `%` and newlines are written as `%7C`, `%25` and `%0A`; Windows
paths otherwise appear exactly as they are.

## Layout

```
desktopdock/
  app.py       the dock window: layout, drag and drop, menus, gestures
  store.py     reading and writing pins.txt
  dnd.py       turning a drop (tab, file, folder, .url, text) into a pin
  icons.py     Windows icons, favicons, thumbnails, initials tiles
  system.py    launching, Explorer, autostart
tests/         unit tests - python -m unittest discover -s tests
```

## Notes

- Runs on Windows; it also starts on Linux and macOS, where the Windows-only
  extras (shell icons, *Start with Windows*, Explorer) simply do nothing.
- Without `tkinterdnd2` the dock still works — you just add pins from the menu
  instead of dropping them.
- Nothing is sent anywhere. The only network requests are favicon downloads,
  which you can turn off with *Fetch site icons*.
