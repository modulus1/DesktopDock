using Avalonia;

namespace DesktopDock;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // One dock is enough; a second launch just exits.
        using var single = new Mutex(initiallyOwned: true, "DesktopDock.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DockApp>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
