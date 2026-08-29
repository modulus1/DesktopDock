using System.Globalization;

namespace DesktopDock.Core;

/// <summary>Everything the dock remembers: a handful of settings and the pins.</summary>
public sealed class DockData
{
    public static readonly IReadOnlyDictionary<string, string> Defaults =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["x"] = "80",
            ["y"] = "80",
            ["orientation"] = "vertical",
            ["icon_size"] = "48",
            ["opacity"] = "0.96",
            ["always_on_top"] = "true",
            ["locked"] = "false",
            ["show_labels"] = "false",
            ["fetch_favicons"] = "true",
            ["profile_pic"] = "",
            ["profile_name"] = "",
        };

    public Dictionary<string, string> Settings { get; } =
        new(Defaults, StringComparer.OrdinalIgnoreCase);

    public List<Pin> Pins { get; } = new();

    public string GetString(string key) =>
        Settings.TryGetValue(key, out string? value) ? value
        : Defaults.TryGetValue(key, out string? fallback) ? fallback
        : string.Empty;

    public int GetInt(string key, int fallback = 0) =>
        double.TryParse(GetString(key), NumberStyles.Any, CultureInfo.InvariantCulture, out double value)
            ? (int)value
            : fallback;

    public double GetDouble(string key, double fallback = 0) =>
        double.TryParse(GetString(key), NumberStyles.Any, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    public bool GetBool(string key, bool fallback = false) => GetString(key).Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "on" => true,
        "0" or "false" or "no" or "off" => false,
        _ => fallback,
    };

    public void Set(string key, string value) => Settings[key] = value;

    public void Set(string key, bool value) => Settings[key] = value ? "true" : "false";

    public void Set(string key, int value) => Settings[key] = value.ToString(CultureInfo.InvariantCulture);

    public void Set(string key, double value) =>
        Settings[key] = value.ToString("0.###", CultureInfo.InvariantCulture);

    public bool IsVertical =>
        !string.Equals(GetString("orientation"), "horizontal", StringComparison.OrdinalIgnoreCase);
}
