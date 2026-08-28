import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from PIL import Image

from desktopdock import icons
from desktopdock.store import Pin


class IconTests(unittest.TestCase):
    def test_letter_tile_is_a_square_rgba_image(self):
        tile = icons.letter_tile("Notepad", 48)
        self.assertEqual(tile.size, (48, 48))
        self.assertEqual(tile.mode, "RGBA")
        self.assertIsNotNone(tile.getbbox())

    def test_letter_tile_initials(self):
        self.assertEqual(icons._initials("Hacker News"), "HN")
        self.assertEqual(icons._initials("Claude"), "CL")
        self.assertEqual(icons._initials(""), "?")

    def test_tile_colour_is_stable_per_name(self):
        self.assertEqual(icons._tile_color("GitHub"), icons._tile_color("GitHub"))
        self.assertNotEqual(icons._tile_color("GitHub"), icons._tile_color("Notepad"))

    def test_fit_square_keeps_aspect_ratio(self):
        wide = Image.new("RGB", (200, 50), "red")
        fitted = icons.fit_square(wide, 64)
        self.assertEqual(fitted.size, (64, 64))
        self.assertEqual(fitted.getpixel((32, 32))[:3], (255, 0, 0))
        self.assertEqual(fitted.getpixel((2, 2))[3], 0)  # padding stays transparent

    def test_circular_crops_corners(self):
        image = Image.new("RGB", (100, 100), "blue")
        avatar = icons.circular(image, 64)
        self.assertEqual(avatar.size, (64, 64))
        self.assertEqual(avatar.getpixel((32, 32))[3], 255)
        self.assertEqual(avatar.getpixel((0, 0))[3], 0)

    def test_custom_icon_file_is_used(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "icon.png")
            Image.new("RGB", (32, 32), "green").save(path)
            image = icons.local_icon(Pin("app", "X", r"C:\x.exe", path), 40)
            self.assertEqual(image.size, (40, 40))

    def test_image_file_pin_uses_its_own_thumbnail(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "photo.png")
            Image.new("RGB", (120, 60), "purple").save(path)
            image = icons.local_icon(Pin("file", "Photo", path), 48)
            self.assertEqual(image.size, (48, 48))

    def test_link_without_cached_favicon_has_no_local_icon(self):
        self.assertIsNone(icons.local_icon(Pin("link", "Nope", "https://not.cached.test"), 48))

    def test_favicon_cache_paths_are_per_host_and_safe(self):
        path = icons.favicon_cache_path("https://news.ycombinator.com/news")
        self.assertTrue(os.path.basename(path).startswith("fav_news.ycombinator.com"))
        self.assertNotIn("/", os.path.basename(icons.favicon_cache_path("https://a/b?c=d")))


if __name__ == "__main__":
    unittest.main()
