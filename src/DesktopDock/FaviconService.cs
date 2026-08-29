using Avalonia.Media.Imaging;

using DesktopDock.Core;

namespace DesktopDock;

/// <summary>
/// Downloads and caches the little icon a website uses, so a pinned tab looks
/// like the site rather than like a coloured square.
/// </summary>
internal static class FaviconService
{
    private static readonly HttpClient Client = CreateClient();

    private static readonly HashSet<string> Attempted = new(StringComparer.OrdinalIgnoreCase);

    public static string CachePathFor(string url)
    {
        string host = Uri.TryCreate(DropParser.EnsureScheme(url), UriKind.Absolute, out Uri? parsed)
            ? parsed.Host
            : "link";

        var safe = new string(host.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '_').ToArray());
        return Path.Combine(PinStore.IconCacheDirectory, $"fav_{Truncate(safe, 60)}.png");
    }

    /// <summary>
    /// Fetches the icon for a link once per session and writes it to the cache
    /// directory. Returns the cached image, or null when the site has none.
    /// </summary>
    public static async Task<Bitmap?> TryFetchAsync(string url)
    {
        string cachePath = CachePathFor(url);
        if (File.Exists(cachePath))
        {
            return IconFactory.TryLoad(cachePath);
        }

        lock (Attempted)
        {
            if (!Attempted.Add(url))
            {
                return null;
            }
        }

        if (!Uri.TryCreate(DropParser.EnsureScheme(url), UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        string[] sources =
        {
            $"https://icons.duckduckgo.com/ip3/{parsed.Host}.ico",
            $"{parsed.Scheme}://{parsed.Host}/favicon.ico",
        };

        foreach (string source in sources)
        {
            Bitmap? bitmap = await TryDownloadAsync(source, cachePath).ConfigureAwait(false);
            if (bitmap is not null)
            {
                return bitmap;
            }
        }

        return null;
    }

    private static async Task<Bitmap?> TryDownloadAsync(string source, string cachePath)
    {
        try
        {
            byte[] payload = await Client.GetByteArrayAsync(source).ConfigureAwait(false);
            if (payload.Length == 0)
            {
                return null;
            }

            using var stream = new MemoryStream(payload);
            var bitmap = new Bitmap(stream);
            Directory.CreateDirectory(PinStore.IconCacheDirectory);
            bitmap.Save(cachePath);
            return bitmap;
        }
        catch (Exception)
        {
            // No icon, an unreadable one, or no network: the tile falls back to initials.
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.Add("User-Agent", "DesktopDock");
        client.MaxResponseContentBufferSize = 1024 * 512;
        return client;
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
