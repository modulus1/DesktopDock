using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

using DesktopDock.Core;

namespace DesktopDock;

/// <summary>The dock itself: a small, borderless, always-on-top strip of tiles.</summary>
public sealed class DockWindow : Window
{
    private readonly Border shell = new();
    private readonly StackPanel host = new();
    private readonly StackPanel items = new();
    private readonly Border avatar = new();
    private readonly Rectangle separator = new();
    private readonly Border addButton = new();
    private readonly DispatcherTimer positionSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private DockData data;
    private Point pressOrigin;
    private bool isReordering;
    private Border? pressedTile;

    public DockWindow()
    {
        data = PinStore.Load();
        if (!File.Exists(PinStore.DefaultPath))
        {
            Save();
        }

        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "DesktopDock";

        BuildChrome();
        ApplyWindowSettings();
        Render();

        // Avalonia picks the drop target by hit-testing and then checking
        // AllowDrop on that exact control - it does not look at ancestors - so
        // every control the pointer can land on has to allow drops.
        DragDrop.SetAllowDrop(this, true);
        AllowDropOnEverything();
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        Opened += (_, _) => AdaptToTransparencySupport();

        // Ctrl+V pins whatever address or path is on the clipboard.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                _ = PasteAsync();
            }
        };

        PositionChanged += (_, _) => positionSaveTimer.Start();
        positionSaveTimer.Tick += (_, _) =>
        {
            positionSaveTimer.Stop();
            RememberPosition();
        };
    }

    /// <summary>
    /// Rounded corners need real window transparency. Where the desktop cannot
    /// provide it (an X11 session with no compositor, say) the corners would be
    /// painted as opaque rectangles, so the dock squares itself off instead.
    /// </summary>
    private void AdaptToTransparencySupport()
    {
        if (ActualTransparencyLevel == WindowTransparencyLevel.Transparent)
        {
            return;
        }

        shell.CornerRadius = new CornerRadius(0);
        Background = Palette.Panel;
    }

    private int IconSize => Math.Clamp(data.GetInt("icon_size", 48), 24, 128);

    private bool IsVertical => data.IsVertical;

    private double AvatarSize => Math.Max(28, IconSize * 0.8);

    // ------------------------------------------------------------------ setup
    private void BuildChrome()
    {
        items.Spacing = Palette.TileSpacing;
        host.Spacing = 2;

        separator.Fill = Palette.Border;
        avatar.ClipToBounds = true;
        avatar.Cursor = new Cursor(StandardCursorType.Hand);
        avatar.PointerPressed += OnAvatarPressed;

        addButton.Cursor = new Cursor(StandardCursorType.Hand);
        addButton.CornerRadius = new CornerRadius(8);
        addButton.Padding = new Thickness(6, 0, 6, 2);
        addButton.Background = Palette.Transparent;
        addButton.Child = new TextBlock
        {
            Text = "+",
            FontSize = 18,
            Foreground = Palette.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        addButton.PointerEntered += (_, _) => addButton.Background = Palette.Hover;
        addButton.PointerExited += (_, _) => addButton.Background = Palette.Transparent;
        addButton.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            ShowDockMenu(addButton);
        };
        ToolTip.SetTip(addButton, "Add an app, file or link\nDrop one on the dock, or press Ctrl+V");

        host.Children.Add(avatar);
        host.Children.Add(separator);
        host.Children.Add(items);
        host.Children.Add(addButton);

        shell.CornerRadius = new CornerRadius(Palette.CornerRadius);
        shell.BorderThickness = new Thickness(1);
        shell.BorderBrush = Palette.Border;
        shell.Background = Palette.Panel;
        shell.Padding = new Thickness(Palette.ShellPadding);
        shell.Child = host;
        shell.PointerPressed += OnBackgroundPressed;

        Content = shell;
    }

    private void ApplyWindowSettings()
    {
        Topmost = data.GetBool("always_on_top", true);
        Opacity = Math.Clamp(data.GetDouble("opacity", 0.96), 0.3, 1.0);
        Position = new PixelPoint(data.GetInt("x", 80), data.GetInt("y", 80));
    }

    // ----------------------------------------------------------------- layout
    /// <summary>Marks the whole visual tree as a drop target. See the note in the constructor.</summary>
    private void AllowDropOnEverything()
    {
        DragDrop.SetAllowDrop(this, true);
        foreach (Visual visual in this.GetSelfAndVisualDescendants())
        {
            if (visual is Control control)
            {
                DragDrop.SetAllowDrop(control, true);
            }
        }
    }

    private void Render()
    {
        host.Orientation = IsVertical ? Orientation.Vertical : Orientation.Horizontal;
        items.Orientation = host.Orientation;
        host.HorizontalAlignment = HorizontalAlignment.Center;
        host.VerticalAlignment = VerticalAlignment.Center;
        addButton.HorizontalAlignment = HorizontalAlignment.Center;
        addButton.VerticalAlignment = VerticalAlignment.Center;
        avatar.HorizontalAlignment = HorizontalAlignment.Center;
        avatar.VerticalAlignment = VerticalAlignment.Center;

        separator.Height = IsVertical ? 1 : AvatarSize;
        separator.Width = IsVertical ? AvatarSize : 1;
        separator.Margin = IsVertical ? new Thickness(4, 3, 4, 3) : new Thickness(3, 4, 3, 4);

        RenderAvatar();

        items.Children.Clear();
        if (data.Pins.Count == 0)
        {
            items.Children.Add(CreateEmptyHint());
        }

        foreach (Pin pin in data.Pins)
        {
            items.Children.Add(CreateTile(pin));
        }

        AllowDropOnEverything();
        _ = FetchMissingIconsAsync();
    }

    private void RenderAvatar()
    {
        double size = AvatarSize;
        avatar.Width = size;
        avatar.Height = size;
        avatar.CornerRadius = new CornerRadius(size / 2);
        avatar.Margin = new Thickness(2);

        Bitmap? picture = IconFactory.TryLoad(data.GetString("profile_pic"));
        if (picture is not null)
        {
            avatar.Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill };
            avatar.Child = null;
        }
        else
        {
            string name = data.GetString("profile_name");
            avatar.Background = new SolidColorBrush(
                name.Length > 0 ? IconFactory.TileColor(name) : Color.Parse("#3A4150"));
            avatar.Child = new TextBlock
            {
                Text = name.Length > 0 ? IconFactory.Initials(name) : "?",
                Foreground = Palette.Text,
                FontSize = size * 0.36,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        string who = data.GetString("profile_name");
        ToolTip.SetTip(avatar, (who.Length > 0 ? who : "You") + " - click to change your picture");
    }

    /// <summary>Shown while the dock is empty, so a new dock explains itself.</summary>
    private Border CreateEmptyHint()
    {
        var hint = new Border
        {
            Padding = new Thickness(8, 10, 8, 10),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Palette.Border,
            Background = Palette.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = IsVertical ? "Drop an app\nor a link\nhere" : "Drop an app or a link here",
                Foreground = Palette.Muted,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            },
        };

        ToolTip.SetTip(hint, "Drag a browser tab, an app or a folder onto the dock.\nOr press Ctrl+V, or click + to pick one.");
        hint.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            ShowDockMenu(hint);
        };

        return hint;
    }

    private Border CreateTile(Pin pin)
    {
        int size = IconSize;
        Bitmap image = IconFactory.LocalIcon(pin, size) ?? IconFactory.LetterTile(pin.Label, size);

        var picture = new Image
        {
            Source = image,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
        };

        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        content.Children.Add(picture);
        if (data.GetBool("show_labels", false))
        {
            content.Children.Add(new TextBlock
            {
                Text = pin.Label.Length > 14 ? pin.Label[..14] : pin.Label,
                Foreground = Palette.Text,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        var tile = new Border
        {
            Child = content,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(10),
            Background = Palette.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = pin,
        };

        ToolTip.SetTip(tile, $"{pin.Label}\n{pin.Target}");
        tile.PointerEntered += (_, _) => tile.Background = Palette.Hover;
        tile.PointerExited += (_, _) => tile.Background = Palette.Transparent;
        tile.PointerPressed += OnTilePressed;
        tile.PointerMoved += OnTileMoved;
        tile.PointerReleased += OnTileReleased;
        return tile;
    }

    // --------------------------------------------------------------- gestures
    private void OnBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            ShowDockMenu(shell);
            return;
        }

        if (point.Properties.IsLeftButtonPressed && !data.GetBool("locked", false))
        {
            BeginMoveDrag(e);
        }
    }

    private void OnAvatarPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        e.Handled = true;

        if (point.Properties.IsRightButtonPressed)
        {
            ShowDockMenu(avatar);
        }
        else
        {
            _ = ChooseProfilePictureAsync();
        }
    }

    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border tile)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            ShowPinMenu(tile);
            return;
        }

        pressedTile = tile;
        pressOrigin = e.GetPosition(items);
        isReordering = false;
        e.Handled = true;
    }

    private void OnTileMoved(object? sender, PointerEventArgs e)
    {
        if (pressedTile is null || data.GetBool("locked", false))
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point position = e.GetPosition(items);
        if (!isReordering)
        {
            double travelled = Math.Abs(position.X - pressOrigin.X) + Math.Abs(position.Y - pressOrigin.Y);
            if (travelled < 8)
            {
                return;
            }

            isReordering = true;
            pressedTile.Opacity = 0.6;
        }

        int current = items.Children.IndexOf(pressedTile);
        int target = IndexAt(position);
        if (target > current)
        {
            target--;
        }

        if (target >= 0 && target < items.Children.Count && target != current)
        {
            items.Children.Move(current, target);
            Pin pin = data.Pins[current];
            data.Pins.RemoveAt(current);
            data.Pins.Insert(target, pin);
        }
    }

    private void OnTileReleased(object? sender, PointerReleasedEventArgs e)
    {
        Border? tile = pressedTile;
        pressedTile = null;
        if (tile is null || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        tile.Opacity = 1;
        if (isReordering)
        {
            isReordering = false;
            Save();
            return;
        }

        if (tile.Tag is Pin pin)
        {
            try
            {
                ShellActions.Launch(pin);
            }
            catch (Exception)
            {
                // A missing target should not take the dock down with it.
                ToolTip.SetTip(tile, $"Could not open {pin.Target}");
            }
        }
    }

    /// <summary>Where in the strip the pointer currently sits.</summary>
    private int IndexAt(Point position)
    {
        for (int index = 0; index < items.Children.Count; index++)
        {
            Rect bounds = items.Children[index].Bounds;
            double middle = IsVertical ? bounds.Y + (bounds.Height / 2) : bounds.X + (bounds.Width / 2);
            double along = IsVertical ? position.Y : position.X;
            if (along < middle)
            {
                return index;
            }
        }

        return items.Children.Count;
    }

    // ------------------------------------------------------------ drag & drop
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        Trace($"drag enter, formats: {string.Join(",", e.Data.GetDataFormats())}");
        shell.BorderBrush = Palette.Accent;
        OnDragOver(sender, e);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => shell.BorderBrush = Palette.Border;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        Trace($"drop, formats: {string.Join(",", e.Data.GetDataFormats())}");
        shell.BorderBrush = Palette.Border;
        e.Handled = true;

        var dropped = new List<Pin>();

        if (e.Data.Contains(DataFormats.Files))
        {
            IEnumerable<IStorageItem>? files = e.Data.GetFiles();
            if (files is not null)
            {
                IEnumerable<string> paths = files
                    .Select(file => file.TryGetLocalPath())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!);

                dropped.AddRange(DropParser.FromFiles(paths));
            }
        }

        if (dropped.Count == 0)
        {
            // A browser tab arrives as text holding the page address.
            dropped.AddRange(DropParser.FromText(e.Data.GetText()));
        }

        Trace($"drop produced {dropped.Count} pin(s)");
        AddPins(dropped);
    }

    /// <summary>Diagnostics, off unless DESKTOPDOCK_TRACE is set.</summary>
    private static void Trace(string message)
    {
        if (Environment.GetEnvironmentVariable("DESKTOPDOCK_TRACE") is not null)
        {
            Console.Error.WriteLine($"[dock] {message}");
        }
    }

    private void AddPins(IEnumerable<Pin> pins)
    {
        bool added = false;
        foreach (Pin pin in pins)
        {
            if (data.Pins.Any(existing =>
                string.Equals(existing.Target, pin.Target, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            data.Pins.Add(pin);
            added = true;
        }

        if (added)
        {
            Save();
            Render();
        }
    }

    // --------------------------------------------------------------- favicons
    private async Task FetchMissingIconsAsync()
    {
        if (!data.GetBool("fetch_favicons", true))
        {
            return;
        }

        foreach (Control child in items.Children.ToList())
        {
            if (child is not Border tile || tile.Tag is not Pin pin || !pin.IsLink)
            {
                continue;
            }

            if (IconFactory.LocalIcon(pin, IconSize) is not null)
            {
                continue;
            }

            Bitmap? icon = await FaviconService.TryFetchAsync(pin.Target).ConfigureAwait(true);
            if (icon is not null && tile.Child is StackPanel panel && panel.Children[0] is Image image)
            {
                image.Source = icon;
            }
        }
    }

    // ------------------------------------------------------------------ menus
    private void ShowDockMenu(Control anchor)
    {
        var menu = new ContextMenu();
        var entries = new List<Control>
        {
            Item("Add link...", () => _ = AddLinkAsync()),
            Item("Add app or file...", () => _ = AddFilesAsync()),
            Item("Add folder...", () => _ = AddFolderAsync()),
            new Separator(),
            Item("Set your picture...", () => _ = ChooseProfilePictureAsync()),
        };

        if (data.GetString("profile_pic").Length > 0)
        {
            entries.Add(Item("Remove your picture", () =>
            {
                data.Set("profile_pic", string.Empty);
                SaveAndRender();
            }));
        }

        entries.Add(Item("Set your name...", () => _ = SetProfileNameAsync()));
        entries.Add(new Separator());

        var appearance = new MenuItem { Header = "Appearance" };
        var appearanceItems = new List<Control>
        {
            Choice("Vertical", IsVertical, () => ApplySetting("orientation", "vertical")),
            Choice("Horizontal", !IsVertical, () => ApplySetting("orientation", "horizontal")),
            new Separator(),
        };

        foreach (int size in new[] { 32, 40, 48, 64, 80 })
        {
            int captured = size;
            appearanceItems.Add(Choice(
                $"Icons {size}px",
                IconSize == size,
                () => ApplySetting("icon_size", captured.ToString())));
        }

        appearanceItems.Add(new Separator());
        foreach (int percent in new[] { 100, 95, 85, 70, 50 })
        {
            int captured = percent;
            appearanceItems.Add(Choice(
                $"Opacity {percent}%",
                (int)Math.Round(data.GetDouble("opacity", 0.96) * 100) == percent,
                () => ApplySetting("opacity", (captured / 100.0).ToString("0.##"))));
        }

        appearance.ItemsSource = appearanceItems;
        entries.Add(appearance);

        entries.Add(Toggle("Show names", "show_labels"));
        entries.Add(Toggle("Always on top", "always_on_top"));
        entries.Add(Toggle("Lock position", "locked"));
        entries.Add(Toggle("Fetch site icons", "fetch_favicons"));

        if (OperatingSystem.IsWindows())
        {
            bool enabled = ShellActions.IsAutostartEnabled();
            entries.Add(Choice("Start with Windows", enabled, () =>
            {
                if (OperatingSystem.IsWindows())
                {
                    ShellActions.SetAutostart(!enabled);
                }
            }));
        }

        entries.Add(new Separator());
        entries.Add(Item("Open pins.txt", ShellActions.OpenDataFile));
        entries.Add(Item("Reload pins.txt", ReloadFromDisk));
        entries.Add(new Separator());
        entries.Add(Item("Quit DesktopDock", Close));

        menu.ItemsSource = entries;
        menu.Open(anchor);
    }

    private void ShowPinMenu(Border tile)
    {
        if (tile.Tag is not Pin pin)
        {
            return;
        }

        var menu = new ContextMenu();
        var entries = new List<Control> { Item("Open", () => ShellActions.Launch(pin)) };

        if (pin.IsLink)
        {
            entries.Add(Item("Copy link", () => _ = Clipboard?.SetTextAsync(pin.Target)));
        }
        else
        {
            entries.Add(Item("Open file location", () => ShellActions.Reveal(pin.Target)));
        }

        entries.Add(new Separator());
        entries.Add(Item("Rename...", () => _ = RenameAsync(pin)));
        entries.Add(Item("Change icon...", () => _ = ChangeIconAsync(pin)));

        if (pin.IconPath.Length > 0 || pin.IsLink)
        {
            entries.Add(Item("Reset icon", () => ResetIcon(pin)));
        }

        entries.Add(new Separator());
        entries.Add(Item("Remove from dock", () =>
        {
            data.Pins.Remove(pin);
            SaveAndRender();
        }));

        menu.ItemsSource = entries;
        menu.Open(tile);
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Choice(string header, bool selected, Action action) =>
        Item((selected ? "•  " : "     ") + header, action);

    private MenuItem Toggle(string header, string key) =>
        Item((data.GetBool(key) ? "✓  " : "     ") + header,
            () => ApplySetting(key, data.GetBool(key) ? "false" : "true"));

    // ---------------------------------------------------------------- actions
    /// <summary>Pins the address or path currently on the clipboard.</summary>
    private async Task PasteAsync()
    {
        if (Clipboard is null)
        {
            return;
        }

        string? text = await Clipboard.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            AddPins(DropParser.FromText(text));
        }
    }

    private async Task AddLinkAsync()
    {
        string? url = await PromptWindow.AskAsync(this, "Add link", "Web address:");
        if (url is null)
        {
            return;
        }

        string address = DropParser.EnsureScheme(url);
        string? name = await PromptWindow.AskAsync(this, "Add link", "Name:", DropParser.LabelForUrl(address));
        Pin? pin = DropParser.FromUrl(address, name);
        if (pin is not null)
        {
            AddPins(new[] { pin });
        }
    }

    private async Task AddFilesAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pin an application or file",
            AllowMultiple = true,
        });

        AddPins(DropParser.FromFiles(LocalPaths(files)));
    }

    private async Task AddFolderAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Pin a folder", AllowMultiple = false });

        AddPins(DropParser.FromFiles(LocalPaths(folders)));
    }

    private async Task ChooseProfilePictureAsync()
    {
        string? path = (await PickImageAsync("Choose your picture")).FirstOrDefault();
        if (path is not null)
        {
            data.Set("profile_pic", path);
            SaveAndRender();
        }
    }

    private async Task SetProfileNameAsync()
    {
        string? name = await PromptWindow.AskAsync(
            this, "Your name", "Shown when no picture is set:", data.GetString("profile_name"));

        if (name is not null)
        {
            data.Set("profile_name", name);
            SaveAndRender();
        }
    }

    private async Task RenameAsync(Pin pin)
    {
        string? name = await PromptWindow.AskAsync(this, "Rename", "Name:", pin.Label);
        if (name is not null)
        {
            pin.Label = name;
            SaveAndRender();
        }
    }

    private async Task ChangeIconAsync(Pin pin)
    {
        string? path = (await PickImageAsync("Choose an icon")).FirstOrDefault();
        if (path is not null)
        {
            pin.IconPath = path;
            SaveAndRender();
        }
    }

    private async Task<IReadOnlyList<string>> PickImageAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp", "*.ico" },
                },
                FilePickerFileTypes.All,
            },
        });

        return LocalPaths(files).ToList();
    }

    private static IEnumerable<string> LocalPaths(IEnumerable<IStorageItem> items) => items
        .Select(item => item.TryGetLocalPath())
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path!);

    private void ResetIcon(Pin pin)
    {
        pin.IconPath = string.Empty;
        if (pin.IsLink)
        {
            try
            {
                File.Delete(FaviconService.CachePathFor(pin.Target));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Keeping the stale icon is better than crashing.
            }
        }

        SaveAndRender();
    }

    private void ApplySetting(string key, string value)
    {
        data.Set(key, value);
        ApplyWindowSettings();
        SaveAndRender();
    }

    private void ReloadFromDisk()
    {
        data = PinStore.Load();
        ApplyWindowSettings();
        Render();
    }

    // ----------------------------------------------------------------- saving
    private void RememberPosition()
    {
        PixelPoint position = Position;
        PixelRect area = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        int x = Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - (int)Math.Max(1, Bounds.Width)));
        int y = Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Max(1, Bounds.Height)));

        if (x != position.X || y != position.Y)
        {
            Position = new PixelPoint(x, y);
        }

        data.Set("x", x);
        data.Set("y", y);
        Save();
    }

    private void SaveAndRender()
    {
        Save();
        Render();
    }

    private void Save()
    {
        try
        {
            PinStore.Save(data);
        }
        catch (Exception)
        {
            // A read-only folder should not stop the dock from working.
        }
    }
}
