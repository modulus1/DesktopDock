namespace DesktopDock.Core;

/// <summary>What a pin points at.</summary>
public enum PinKind
{
    App,
    File,
    Folder,
    Link,
}

/// <summary>
/// A single item in the dock. Pins are stored as one line of text:
/// <c>type | label | target | icon</c>.
/// </summary>
public sealed class Pin
{
    public Pin(PinKind kind, string label, string target, string iconPath = "")
    {
        Kind = kind;
        Label = label;
        Target = target;
        IconPath = iconPath;
    }

    public PinKind Kind { get; set; }

    public string Label { get; set; }

    /// <summary>A path to launch, or the URL to open.</summary>
    public string Target { get; set; }

    /// <summary>Optional path to a custom icon image; empty means "work it out".</summary>
    public string IconPath { get; set; }

    public bool IsLink => Kind == PinKind.Link;

    public string ToLine() => string.Join(
        " | ",
        Encode(Kind.ToString().ToLowerInvariant()),
        Encode(Label),
        Encode(Target),
        Encode(IconPath));

    public static Pin? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        string[] parts = line.Split('|');
        string kindText = Decode(Field(parts, 0));
        string label = Decode(Field(parts, 1));
        string target = Decode(Field(parts, 2));
        string icon = Decode(Field(parts, 3));

        if (target.Length == 0 || !Enum.TryParse(kindText, ignoreCase: true, out PinKind kind))
        {
            return null;
        }

        return new Pin(kind, label.Length > 0 ? label : target, target, icon);
    }

    /// <summary>
    /// Escapes the three characters that would otherwise break a line, and nothing
    /// else, so Windows paths stay readable in the file.
    /// </summary>
    public static string Encode(string? value) => (value ?? string.Empty)
        .Replace("%", "%25", StringComparison.Ordinal)
        .Replace("|", "%7C", StringComparison.Ordinal)
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Replace("\n", "%0A", StringComparison.Ordinal);

    public static string Decode(string? value) => (value ?? string.Empty)
        .Replace("%0A", "\n", StringComparison.Ordinal)
        .Replace("%7C", "|", StringComparison.Ordinal)
        .Replace("%25", "%", StringComparison.Ordinal);

    private static string Field(string[] parts, int index) =>
        index < parts.Length ? parts[index].Trim() : string.Empty;
}
