using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using DesktopDock.Core;

namespace DesktopDock;

/// <summary>Builds the picture that goes on a tile.</summary>
internal static class IconFactory
{
    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico",
    };

    /// <summary>Loads an image file, or returns null when it is missing or unreadable.</summary>
    public static Bitmap? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The always-available fallback: initials on a coloured rounded tile.</summary>
    public static Bitmap LetterTile(string label, int size)
    {
        string text = Initials(label);
        Color color = TileColor(label);

        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            context.DrawRectangle(
                new SolidColorBrush(color),
                null,
                new RoundedRect(new Rect(0, 0, size, size), size / 5.0));

            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
                size * (text.Length > 1 ? 0.42 : 0.55),
                new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)));

            context.DrawText(
                formatted,
                new Point((size - formatted.Width) / 2, (size - formatted.Height) / 2));
        }

        return bitmap;
    }

    /// <summary>Resolves a pin's icon without touching the network.</summary>
    public static Bitmap? LocalIcon(Pin pin, int size)
    {
        Bitmap? custom = TryLoad(pin.IconPath);
        if (custom is not null)
        {
            return custom;
        }

        if (pin.IsLink)
        {
            return TryLoad(FaviconService.CachePathFor(pin.Target));
        }

        string extension = Path.GetExtension(pin.Target);
        if (ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            Bitmap? thumbnail = TryLoad(pin.Target);
            if (thumbnail is not null)
            {
                return thumbnail;
            }
        }

        return OperatingSystem.IsWindows() ? WindowsIcons.Extract(pin.Target, size) : null;
    }

    public static string Initials(string? label)
    {
        string[] words = (label ?? "?")
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => (string.Concat(words[0][0], words[1][0])).ToUpperInvariant(),
        };
    }

    /// <summary>A stable, pleasant colour derived from the name.</summary>
    public static Color TileColor(string? seed)
    {
        byte[] digest = MD5.HashData(Encoding.UTF8.GetBytes(seed ?? "?"));
        return FromHsv(digest[0] / 255.0, 0.55, 0.85);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        int sector = (int)(hue * 6) % 6;
        double fraction = (hue * 6) - Math.Floor(hue * 6);
        double p = value * (1 - saturation);
        double q = value * (1 - (fraction * saturation));
        double t = value * (1 - ((1 - fraction) * saturation));

        (double red, double green, double blue) = sector switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };

        return Color.FromRgb((byte)(red * 255), (byte)(green * 255), (byte)(blue * 255));
    }
}
