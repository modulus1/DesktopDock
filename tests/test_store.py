import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from desktopdock import store
from desktopdock.store import DockData, Pin


class RoundTripTests(unittest.TestCase):
    def test_pin_line_round_trip(self):
        pin = Pin("app", "Note | pad", r"C:\Program Files\np.exe", "")
        parsed = Pin.from_line(pin.to_line())
        self.assertEqual(parsed, pin)

    def test_windows_paths_stay_readable(self):
        line = Pin("app", "Notepad", r"C:\Windows\notepad.exe").to_line()
        self.assertIn(r"C:\Windows\notepad.exe", line)

    def test_percent_and_newline_escaping(self):
        pin = Pin("link", "50%\noff", "https://x.test/a%20b")
        parsed = Pin.from_line(pin.to_line())
        self.assertEqual(parsed.label, "50%\noff")
        self.assertEqual(parsed.target, "https://x.test/a%20b")

    def test_bad_lines_are_ignored(self):
        self.assertIsNone(Pin.from_line("nonsense"))
        self.assertIsNone(Pin.from_line("app |  | "))

    def test_file_round_trip(self):
        data = DockData()
        data.set("icon_size", 64)
        data.set("locked", True)
        data.pins = [Pin("link", "Claude", "https://claude.ai"), Pin("app", "NP", r"C:\np.exe")]
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "pins.txt")
            store.save(data, path)
            loaded = store.load(path)
        self.assertEqual(loaded.get_int("icon_size"), 64)
        self.assertTrue(loaded.get_bool("locked"))
        self.assertEqual(loaded.pins, data.pins)

    def test_missing_file_gives_defaults(self):
        data = store.load(os.path.join(tempfile.gettempdir(), "does-not-exist-dock.txt"))
        self.assertEqual(data.pins, [])
        self.assertEqual(data.get_int("icon_size"), 48)

    def test_hand_edited_file_is_accepted(self):
        text = """
        # a comment
        [settings]
        icon_size = 32
        orientation = horizontal

        [pins]
        link | Claude | https://claude.ai |
        app | Notepad | C:\\Windows\\notepad.exe
        """
        data = store.parse("\n".join(line.strip() for line in text.splitlines()))
        self.assertEqual(data.get("orientation"), "horizontal")
        self.assertEqual([p.label for p in data.pins], ["Claude", "Notepad"])

    def test_save_is_atomic_and_leaves_no_temp_files(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "pins.txt")
            store.save(DockData(), path)
            store.save(DockData(), path)
            self.assertEqual(os.listdir(directory), ["pins.txt"])


if __name__ == "__main__":
    unittest.main()
