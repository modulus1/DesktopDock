using System.Text.RegularExpressions;

namespace DesktopDock.Core;

/// <summary>
/// Turns whatever Windows hands over on a drop - a browser tab, an executable,
/// a shortcut, a folder, a stretch of text - into pins.
/// </summary>
public static partial class DropParser
{
    private static readonly HashSet<string> AppExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".bat", ".cmd", ".com", ".msi", ".ps1", ".appref-ms", ".pif",
    };

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*://\S+$")]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"^(?:www\.)?[\w\-]+(?:\.[\w\-]+)+(?:[/?#]\S*)?$")]
    private static partial Regex BareHostPattern();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*://")]
    private static partial Regex SchemePattern();

    /// <summary>An absolute Windows or POSIX path, recognised the same way on any platform.</summary>
    [GeneratedRegex(@"^(?:[A-Za-z]:[\\/]|\\\\[^\\]|/)")]
    private static partial Regex AbsolutePathPattern();

    public static bool LooksLikeUrl(string? text)
    {
        string candidate = (text ?? string.Empty).Trim();
        if (candidate.Length == 0 || candidate.Length > 2048 || candidate.Contains('\n'))
        {
            return false;
        }

        if (candidate.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return UrlPattern().IsMatch(candidate)
            || (BareHostPattern().IsMatch(candidate) && !candidate.Contains(' '));
    }

    public static string EnsureScheme(string? url)
    {
        string candidate = (url ?? string.Empty).Trim();
        if (candidate.Length == 0 || SchemePattern().IsMatch(candidate))
        {
            return candidate;
        }

        return candidate.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? candidate
            : "https://" + candidate;
    }

    /// <summary>The last component of a path, splitting on both separators.</summary>
    public static string BaseNameAny(string? path)
    {
        string cleaned = (path ?? string.Empty).TrimEnd('\\', '/');
        if (cleaned.Length == 0)
        {
            return string.Empty;
        }

        int cut = cleaned.LastIndexOfAny(new[] { '\\', '/' });
        return cut >= 0 ? cleaned[(cut + 1)..] : cleaned;
    }

    /// <summary>A short, friendly name for a URL: the page, or failing that the site.</summary>
    public static string LabelForUrl(string url)
    {
        if (!Uri.TryCreate(EnsureScheme(url), UriKind.Absolute, out Uri? parsed))
        {
            return Shorten(url);
        }

        string host = parsed.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        if (host.Length == 0)
        {
            return Shorten(url);
        }

        // Drop the public suffix: news.ycombinator.com -> ycombinator, claude.ai -> claude.
        string[] hostParts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        string siteName = (hostParts.Length > 1 ? hostParts[^2] : hostParts[0]).Replace('-', ' ');

        string[] segments = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0 && segments[^1].Length <= 24)
        {
            string leaf = Uri.UnescapeDataString(segments[^1]);
            leaf = Path.GetFileNameWithoutExtension(leaf).Replace('-', ' ').Replace('_', ' ').Trim();
            if (leaf.Length > 0 && !siteName.Contains(leaf, StringComparison.OrdinalIgnoreCase))
            {
                return Shorten(Capitalise(leaf));
            }
        }

        return Shorten(Capitalise(siteName));
    }

    public static string LabelForPath(string path)
    {
        string name = BaseNameAny(path);
        if (name.Length == 0)
        {
            return Shorten(path);
        }

        string extension = Path.GetExtension(name);
        if (AppExtensions.Contains(extension) || extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        return Shorten(name);
    }

    /// <summary>Reads the target out of a Windows .url internet shortcut.</summary>
    public static string ReadUrlShortcut(string path)
    {
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                if (line.TrimStart().StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    return line[(line.IndexOf('=') + 1)..].Trim();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not readable - treat it as a plain file instead.
        }

        return string.Empty;
    }

    /// <summary>Normalises a file:// URI or a quoted path into a plain filesystem path.</summary>
    public static string NormalizePath(string? candidate)
    {
        string path = (candidate ?? string.Empty).Trim().Trim('"');
        if (!path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (!Uri.TryCreate(path, UriKind.Absolute, out Uri? uri))
        {
            return path;
        }

        string local = Uri.UnescapeDataString(uri.AbsolutePath);
        if (!string.IsNullOrEmpty(uri.Host) && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return $"//{uri.Host}{local}";
        }

        // file:///C:/x -> C:/x
        return Regex.IsMatch(local, @"^/[A-Za-z]:") ? local[1..] : local;
    }

    public static Pin? FromPath(string? candidate)
    {
        string path = NormalizePath(candidate);
        if (path.Length == 0)
        {
            return null;
        }

        string extension = Path.GetExtension(path);
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            string url = ReadUrlShortcut(path);
            return url.Length > 0 ? new Pin(PinKind.Link, LabelForPath(path), url) : null;
        }

        if (Directory.Exists(path))
        {
            return new Pin(PinKind.Folder, LabelForPath(path), path);
        }

        PinKind kind = AppExtensions.Contains(extension) ? PinKind.App : PinKind.File;
        return new Pin(kind, LabelForPath(path), path);
    }

    public static Pin? FromUrl(string? url, string? label = null)
    {
        string target = EnsureScheme(url);
        if (target.Length == 0)
        {
            return null;
        }

        string name = (label ?? string.Empty).Trim();
        return new Pin(PinKind.Link, name.Length > 0 ? Shorten(name) : LabelForUrl(target), target);
    }

    public static IReadOnlyList<Pin> FromFiles(IEnumerable<string>? paths)
    {
        var pins = new List<Pin>();
        foreach (string path in paths ?? Array.Empty<string>())
        {
            Pin? pin = FromPath(path);
            if (pin is not null)
            {
                pins.Add(pin);
            }
        }

        return pins;
    }

    /// <summary>
    /// Parses dropped text: a single URL (a browser tab), the URL-then-title pair
    /// Firefox sends, a file:// URI list, or a plain path.
    /// </summary>
    public static IReadOnlyList<Pin> FromText(string? payload)
    {
        var pins = new List<Pin>();
        string text = (payload ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return pins;
        }

        string[] lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (lines.Length == 2 && LooksLikeUrl(lines[0]) && !LooksLikeUrl(lines[1]))
        {
            Pin? titled = FromUrl(lines[0], lines[1]);
            if (titled is not null)
            {
                pins.Add(titled);
            }

            return pins;
        }

        foreach (string line in lines)
        {
            Pin? pin = null;
            if (LooksLikeUrl(line))
            {
                pin = FromUrl(line);
            }
            else
            {
                string path = NormalizePath(line);
                if (File.Exists(path) || Directory.Exists(path) || AbsolutePathPattern().IsMatch(path))
                {
                    pin = FromPath(path);
                }
            }

            if (pin is not null)
            {
                pins.Add(pin);
            }
        }

        return pins;
    }

    private static string Capitalise(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string Shorten(string value) => value.Length <= 32 ? value : value[..32];
}
