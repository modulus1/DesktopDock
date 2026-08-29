using System.Text;

namespace DesktopDock.Core;

/// <summary>
/// Reads and writes the one plain-text file the dock keeps beside the
/// executable. The format is deliberately simple enough to edit by hand.
/// </summary>
public static class PinStore
{
    public const string FileName = "pins.txt";

    private const string Header =
        "# DesktopDock data file\n" +
        "#\n" +
        "# Pins are 'type | label | target | icon'.\n" +
        "#   type   : app, file, folder or link\n" +
        "#   label  : the name shown in the dock\n" +
        "#   target : the path to launch, or the URL to open\n" +
        "#   icon   : optional path to a custom icon image (blank = automatic)\n" +
        "#\n" +
        "# Edit this file by hand if you like, then choose 'Reload pins.txt' in the\n" +
        "# dock menu. Inside a field '|', '%' and line breaks are written as %7C,\n" +
        "# %25 and %0A; everything else, Windows paths included, is stored as-is.\n";

    /// <summary>The data file lives next to the executable, so the app stays portable.</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static string IconCacheDirectory => Path.Combine(AppContext.BaseDirectory, "icons");

    public static DockData Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DockData();
        }
    }

    public static DockData Parse(string text)
    {
        var data = new DockData();
        string section = "pins";

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }

            if (section == "settings")
            {
                int separator = line.IndexOf('=');
                if (separator > 0)
                {
                    data.Set(line[..separator].Trim(), Pin.Decode(line[(separator + 1)..].Trim()));
                }
            }
            else if (section == "pins")
            {
                Pin? pin = Pin.TryParse(line);
                if (pin is not null)
                {
                    data.Pins.Add(pin);
                }
            }
        }

        return data;
    }

    public static string Serialize(DockData data)
    {
        var builder = new StringBuilder(Header);
        builder.Append("\n[settings]\n");
        foreach (string key in data.Settings.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            builder.Append(key).Append(" = ").Append(Pin.Encode(data.Settings[key])).Append('\n');
        }

        builder.Append("\n[pins]\n");
        foreach (Pin pin in data.Pins)
        {
            builder.Append(pin.ToLine()).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Writes through a temporary file so an interrupted save cannot truncate the data.</summary>
    public static void Save(DockData data, string? path = null)
    {
        path ??= DefaultPath;
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(directory);

        string temporary = Path.Combine(directory, $".pins-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, Serialize(data), new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file is not worth failing the save for.
        }
    }
}
