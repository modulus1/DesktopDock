using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace DesktopDock;

public class DockApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());

        // The dock is dark, so its menus and dialogs should be too, whatever the
        // rest of the desktop is set to.
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new DockWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
