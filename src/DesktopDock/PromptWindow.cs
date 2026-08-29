using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace DesktopDock;

/// <summary>A small "type something" dialog, used for adding links and renaming pins.</summary>
internal sealed class PromptWindow : Window
{
    private readonly TextBox input;

    public PromptWindow(string title, string question, string initialValue = "")
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#22262F"));

        input = new TextBox
        {
            Text = initialValue,
            Watermark = question,
            Margin = new Thickness(0, 8, 0, 12),
        };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => Close(input.Text ?? string.Empty);
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = question, Foreground = Palette.Text },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };

        Opened += (_, _) =>
        {
            input.SelectAll();
            input.Focus();
        };
    }

    /// <summary>Shows the dialog and returns what was typed, or null if it was cancelled.</summary>
    public static async Task<string?> AskAsync(Window owner, string title, string question, string initialValue = "")
    {
        var dialog = new PromptWindow(title, question, initialValue);
        string? answer = await dialog.ShowDialog<string?>(owner);
        return string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
    }
}
