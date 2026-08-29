using DesktopDock.Core;

using static DesktopDock.Tests.Harness;

// ---------------------------------------------------------------- pin lines
Test("a pin survives a round trip through its text line", () =>
{
    var pin = new Pin(PinKind.App, "Notepad", @"C:\Windows\notepad.exe");
    Pin parsed = NotNull(Pin.TryParse(pin.ToLine()));
    AreEqual(PinKind.App, parsed.Kind);
    AreEqual("Notepad", parsed.Label);
    AreEqual(@"C:\Windows\notepad.exe", parsed.Target);
});

Test("windows paths are stored verbatim", () =>
    IsTrue(new Pin(PinKind.App, "Notepad", @"C:\Windows\notepad.exe").ToLine()
        .Contains(@"C:\Windows\notepad.exe", StringComparison.Ordinal)));

Test("pipes, percents and newlines survive escaping", () =>
{
    var pin = new Pin(PinKind.Link, "50% | off\nsale", "https://x.test/a%20b");
    Pin parsed = NotNull(Pin.TryParse(pin.ToLine()));
    AreEqual("50% | off\nsale", parsed.Label);
    AreEqual("https://x.test/a%20b", parsed.Target);
});

Test("nonsense lines are ignored", () =>
{
    IsNull(Pin.TryParse("just some text"));
    IsNull(Pin.TryParse("app |  | "));
    IsNull(Pin.TryParse(string.Empty));
});

// ------------------------------------------------------------------- store
Test("settings and pins survive a file round trip", () =>
{
    string path = Path.Combine(Path.GetTempPath(), $"dock-{Guid.NewGuid():N}.txt");
    var data = new DockData();
    data.Set("icon_size", 64);
    data.Set("locked", true);
    data.Set("opacity", 0.85);
    data.Pins.Add(new Pin(PinKind.Link, "Claude", "https://claude.ai"));
    data.Pins.Add(new Pin(PinKind.Folder, "Projects", @"C:\Projects"));

    PinStore.Save(data, path);
    DockData loaded = PinStore.Load(path);
    File.Delete(path);

    AreEqual(64, loaded.GetInt("icon_size"));
    AreEqual(true, loaded.GetBool("locked"));
    AreEqual(0.85, loaded.GetDouble("opacity"));
    AreEqual(2, loaded.Pins.Count);
    AreEqual("Claude", loaded.Pins[0].Label);
    AreEqual(@"C:\Projects", loaded.Pins[1].Target);
});

Test("a missing file gives defaults rather than an error", () =>
{
    DockData data = PinStore.Load(Path.Combine(Path.GetTempPath(), "no-such-dock-file.txt"));
    AreEqual(0, data.Pins.Count);
    AreEqual(48, data.GetInt("icon_size"));
    IsTrue(data.IsVertical);
});

Test("a hand edited file is accepted", () =>
{
    DockData data = PinStore.Parse("""
        # my dock
        [settings]
        icon_size = 32
        orientation = horizontal

        [pins]
        link | Claude | https://claude.ai |
        app | Notepad | C:\Windows\notepad.exe
        """);

    AreEqual(32, data.GetInt("icon_size"));
    IsFalse(data.IsVertical);
    AreEqual("Claude, Notepad", string.Join(", ", data.Pins.Select(pin => pin.Label)));
});

Test("saving twice leaves no temporary files behind", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), $"dock-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    string path = Path.Combine(directory, "pins.txt");
    PinStore.Save(new DockData(), path);
    PinStore.Save(new DockData(), path);
    AreEqual(1, Directory.GetFiles(directory).Length);
    Directory.Delete(directory, recursive: true);
});

Test("settings are case insensitive", () =>
{
    DockData data = PinStore.Parse("[settings]\nIcon_Size = 40\n");
    AreEqual(40, data.GetInt("icon_size"));
});

// -------------------------------------------------------------------- urls
Test("urls are told apart from paths", () =>
{
    IsTrue(DropParser.LooksLikeUrl("https://claude.ai/chat"));
    IsTrue(DropParser.LooksLikeUrl("example.com/page"));
    IsFalse(DropParser.LooksLikeUrl(@"C:\Windows\notepad.exe"));
    IsFalse(DropParser.LooksLikeUrl("file:///home/user/x.txt"));
    IsFalse(DropParser.LooksLikeUrl("just some text"));
});

Test("a bare host gets a scheme", () =>
{
    AreEqual("https://example.com", DropParser.EnsureScheme("example.com"));
    AreEqual("http://x.test", DropParser.EnsureScheme("http://x.test"));
    AreEqual("mailto:me@x.test", DropParser.EnsureScheme("mailto:me@x.test"));
});

Test("links and paths get readable names", () =>
{
    AreEqual("Github", DropParser.LabelForUrl("https://www.github.com"));
    AreEqual("Claude", DropParser.LabelForUrl("https://claude.ai"));
    AreEqual("News", DropParser.LabelForUrl("https://news.ycombinator.com/news"));
    AreEqual("Claude code", DropParser.LabelForUrl("https://github.com/anthropics/claude-code"));
    AreEqual("notepad", DropParser.LabelForPath(@"C:\Windows\notepad.exe"));
    AreEqual("budget.xlsx", DropParser.LabelForPath(@"C:\Users\me\budget.xlsx"));
    AreEqual("Documents", DropParser.LabelForPath(@"C:\Users\me\Documents\"));
});

// ------------------------------------------------------------------- drops
Test("a dropped browser tab becomes a link", () =>
{
    IReadOnlyList<Pin> pins = DropParser.FromText("https://claude.ai/new");
    AreEqual(1, pins.Count);
    AreEqual(PinKind.Link, pins[0].Kind);
    AreEqual("https://claude.ai/new", pins[0].Target);
});

Test("the url and title pair firefox sends keeps the title", () =>
{
    IReadOnlyList<Pin> pins = DropParser.FromText("https://claude.ai/new\nClaude");
    AreEqual(1, pins.Count);
    AreEqual("Claude", pins[0].Label);
});

Test("dropped executables and documents get the right kind", () =>
{
    IReadOnlyList<Pin> pins = DropParser.FromFiles(new[]
    {
        @"C:\Program Files\Editor\ed.exe",
        @"C:\Users\me\budget.xlsx",
        @"C:\Users\me\Desktop\Game.lnk",
    });

    AreEqual("App, File, App", string.Join(", ", pins.Select(pin => pin.Kind.ToString())));
    AreEqual("ed", pins[0].Label);
    AreEqual("Game", pins[2].Label);
});

Test("a dropped folder becomes a folder pin", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), $"dock-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    IReadOnlyList<Pin> pins = DropParser.FromFiles(new[] { directory });
    Directory.Delete(directory);
    AreEqual(PinKind.Folder, pins[0].Kind);
});

Test("a dropped .url shortcut becomes a link", () =>
{
    string path = Path.Combine(Path.GetTempPath(), $"Claude-{Guid.NewGuid():N}.url");
    File.WriteAllText(path, "[InternetShortcut]\r\nURL=https://claude.ai\r\n");
    IReadOnlyList<Pin> pins = DropParser.FromFiles(new[] { path });
    File.Delete(path);
    AreEqual(PinKind.Link, pins[0].Kind);
    AreEqual("https://claude.ai", pins[0].Target);
});

Test("file uris are turned back into paths", () =>
{
    AreEqual("C:/Users/me/a file.txt", DropParser.NormalizePath("file:///C:/Users/me/a%20file.txt"));
    AreEqual("/home/me/x.txt", DropParser.NormalizePath("file:///home/me/x.txt"));
    AreEqual(@"C:\x.exe", DropParser.NormalizePath(@"""C:\x.exe"""));
});

Test("dropped plain text that is neither a url nor a path is refused", () =>
{
    AreEqual(0, DropParser.FromText("some words a user selected").Count);
    AreEqual(0, DropParser.FromText("   ").Count);
});

Test("several file uris in one drop all become pins", () =>
{
    IReadOnlyList<Pin> pins = DropParser.FromText("file:///home/me/one.txt\nfile:///home/me/two.txt");
    AreEqual(2, pins.Count);
    AreEqual("one.txt, two.txt", string.Join(", ", pins.Select(pin => pin.Label)));
});

return Report();
