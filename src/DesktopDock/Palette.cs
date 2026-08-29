using Avalonia.Media;

namespace DesktopDock;

/// <summary>The handful of colours and measurements the dock is built from.</summary>
internal static class Palette
{
    public static readonly IBrush Panel = new SolidColorBrush(Color.Parse("#F21B1E26"));
    public static readonly IBrush Border = new SolidColorBrush(Color.Parse("#3A4150"));
    public static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#5B8CFF"));
    public static readonly IBrush Hover = new SolidColorBrush(Color.Parse("#2B3140"));
    public static readonly IBrush Transparent = Brushes.Transparent;
    public static readonly IBrush Text = new SolidColorBrush(Color.Parse("#E6E9EF"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8B93A5"));

    public const double TileSpacing = 4;
    public const double ShellPadding = 6;
    public const double CornerRadius = 14;
}
