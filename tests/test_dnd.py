import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from desktopdock import dnd


class TclListTests(unittest.TestCase):
    def test_braced_paths_with_spaces(self):
        payload = r"{C:\Program Files\App\app.exe} C:\tools\x.exe"
        self.assertEqual(
            dnd.split_tcl_list(payload),
            [r"C:\Program Files\App\app.exe", r"C:\tools\x.exe"],
        )

    def test_single_plain_path(self):
        self.assertEqual(dnd.split_tcl_list(r"C:\x.exe"), [r"C:\x.exe"])

    def test_multiple_braced_paths(self):
        self.assertEqual(
            dnd.split_tcl_list("{/home/a b/one.txt} {/home/a b/two.txt}"),
            ["/home/a b/one.txt", "/home/a b/two.txt"],
        )


class UrlTests(unittest.TestCase):
    def test_url_detection(self):
        self.assertTrue(dnd.looks_like_url("https://claude.ai/chat"))
        self.assertTrue(dnd.looks_like_url("example.com/page"))
        self.assertFalse(dnd.looks_like_url(r"C:\Windows\notepad.exe"))
        self.assertFalse(dnd.looks_like_url("file:///home/user/x.txt"))
        self.assertFalse(dnd.looks_like_url("just some text"))

    def test_scheme_is_added(self):
        self.assertEqual(dnd.ensure_scheme("example.com"), "https://example.com")
        self.assertEqual(dnd.ensure_scheme("http://x.test"), "http://x.test")

    def test_labels(self):
        self.assertEqual(dnd.label_for_url("https://www.github.com"), "Github")
        self.assertEqual(dnd.label_for_url("https://news.ycombinator.com/news"), "News")
        self.assertEqual(dnd.label_for_path(r"C:\Windows\notepad.exe"), "notepad")
        self.assertEqual(dnd.label_for_path("/home/user/Documents"), "Documents")


class DropTests(unittest.TestCase):
    def test_browser_tab_drop_makes_a_link(self):
        pins = dnd.pins_from_drop("https://claude.ai/new")
        self.assertEqual(len(pins), 1)
        self.assertEqual(pins[0].kind, "link")
        self.assertEqual(pins[0].target, "https://claude.ai/new")

    def test_firefox_url_and_title_pair(self):
        pins = dnd.pins_from_drop("https://claude.ai/new\nClaude")
        self.assertEqual([(p.kind, p.label) for p in pins], [("link", "Claude")])

    def test_executable_drop_makes_an_app(self):
        pins = dnd.pins_from_drop(r"{C:\Program Files\Editor\ed.exe}")
        self.assertEqual([(p.kind, p.label, p.target) for p in pins],
                         [("app", "ed", r"C:\Program Files\Editor\ed.exe")])

    def test_folder_drop_makes_a_folder_pin(self):
        with tempfile.TemporaryDirectory() as directory:
            pins = dnd.pins_from_drop(directory)
            self.assertEqual(pins[0].kind, "folder")

    def test_file_uri_is_normalised(self):
        self.assertEqual(dnd.normalize_path("file:///C:/Users/me/a%20file.txt"),
                         "C:/Users/me/a file.txt")
        self.assertEqual(dnd.normalize_path("file:///home/me/x.txt"), "/home/me/x.txt")

    def test_url_shortcut_file_becomes_a_link(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "Claude.url")
            with open(path, "w", encoding="utf-8") as handle:
                handle.write("[InternetShortcut]\nURL=https://claude.ai\n")
            pins = dnd.pins_from_drop(path)
            self.assertEqual([(p.kind, p.label, p.target) for p in pins],
                             [("link", "Claude", "https://claude.ai")])

    def test_multiple_files_in_one_drop(self):
        pins = dnd.pins_from_drop(r"{C:\a\one.exe} {C:\b\two.txt}")
        self.assertEqual([p.kind for p in pins], ["app", "file"])

    def test_empty_drop(self):
        self.assertEqual(dnd.pins_from_drop("   "), [])


if __name__ == "__main__":
    unittest.main()
