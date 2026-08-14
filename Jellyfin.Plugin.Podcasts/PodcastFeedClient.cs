using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts;

public sealed class PodcastFeedClient
{
    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All })
    { Timeout = TimeSpan.FromSeconds(20) };
    private readonly PodcastStore _store;
    private readonly Dictionary<string, (DateTimeOffset Time, PodcastFeed Feed)> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _feedGate = new(1, 1);
    private readonly ILogger<PodcastFeedClient> _logger;

    public PodcastFeedClient(PodcastStore store, ILogger<PodcastFeedClient> logger)
    {
        _store = store;
        _logger = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Tilapia/1.0.0");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("(Jellyfin-Podcast-Client)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }

    public async Task<PodcastFeed> GetFeedAsync(string url, CancellationToken token)
    {
        if (_feeds.TryGetValue(url, out var cached) && DateTimeOffset.UtcNow - cached.Time < TimeSpan.FromMinutes(15)) return cached.Feed;
        await _feedGate.WaitAsync(token);
        try
        {
            if (_feeds.TryGetValue(url, out cached) && DateTimeOffset.UtcNow - cached.Time < TimeSpan.FromMinutes(15)) return cached.Feed;
            var bytes = await GetBytesSafeAsync(new Uri(url), 0, 5 * 1024 * 1024, token);
            var feed = Parse(bytes);
            _feeds[url] = (DateTimeOffset.UtcNow, feed);
            return feed;
        }
        finally { _feedGate.Release(); }
    }

    public async Task<string> GetCachedEpisodeAsync(string audioUrl, CancellationToken token)
    {
        var extension = Path.GetExtension(new Uri(audioUrl).AbsolutePath);
        if (extension.Length > 8 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.')) extension = ".bin";
        var path = Path.Combine(_store.CachePath, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(audioUrl))).ToLowerInvariant() + extension);
        if (File.Exists(path)) return path;
        var bytes = await GetBytesSafeAsync(new Uri(audioUrl), 0, 1024L * 1024 * 1024, token);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, token);
        File.Move(temp, path, true);
        return path;
    }

    public string? GetPrivateEpisodePath(Guid subscriptionId, PodcastEpisode episode)
    {
        var folder = Path.Combine(_store.CachePath, "private", subscriptionId.ToString("N"));
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder, episode.Id + ".*").FirstOrDefault();
    }

    public async Task RefreshPrivateAsync(Subscription subscription, CancellationToken token)
    {
        PodcastFeed feed;
        try { feed = await GetFeedAsync(subscription.FeedUrl, token); }
        catch (HttpRequestException ex) { throw new HttpRequestException($"Private RSS feed request failed: {ex.StatusCode?.ToString() ?? ex.Message}", ex, ex.StatusCode); }
        var newest = feed.Episodes.OrderByDescending(x => x.Published).ToArray();
        if (newest.Length == 0) return;
        var folder = Path.Combine(_store.CachePath, "private", subscription.Id.ToString("N"));
        Directory.CreateDirectory(folder);
        var marker = Path.Combine(folder, "seen.txt");
        var seen = File.Exists(marker) ? new HashSet<string>(await File.ReadAllLinesAsync(marker, token)) : [];
        var hasCachedMedia = newest.Any(x => GetPrivateEpisodePath(subscription.Id, x) is not null);
        var candidates = !hasCachedMedia ? newest.Take(1) : newest.TakeWhile(x => !seen.Contains(x.Id));
        var changed = false;
        foreach (var episode in candidates.Reverse())
        {
            var extension = Path.GetExtension(new Uri(episode.AudioUrl).AbsolutePath);
            if (extension.Length is 0 or > 8) extension = ".bin";
            var path = Path.Combine(folder, episode.Id + extension);
            if (!File.Exists(path))
            {
                try { await DownloadPrivateEpisodeAsync(new Uri(episode.AudioUrl), path + ".tmp", 0, 1024L * 1024 * 1024, token); }
                catch (HttpRequestException ex) { throw new HttpRequestException($"Private episode download was rejected by {new Uri(episode.AudioUrl).DnsSafeHost}: {ex.StatusCode?.ToString() ?? ex.Message}", ex, ex.StatusCode); }
                File.Move(path + ".tmp", path, true);
                changed = true;
            }
        }
        await File.WriteAllLinesAsync(marker, newest.Select(x => x.Id), token);
        var cachedNewest = newest.Where(x => GetPrivateEpisodePath(subscription.Id, x) is not null).ToArray();
        var keepIds = cachedNewest
            .Where((x, index) => index == 0 || !x.Published.HasValue || x.Published >= DateTimeOffset.UtcNow.AddDays(-7 * subscription.RetentionWeeks))
            .Take(subscription.EpisodeLimit).Select(x => x.Id).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(folder).Where(x => Path.GetFileName(x) != "seen.txt" && !keepIds.Contains(Path.GetFileNameWithoutExtension(x)))) { File.Delete(file); changed = true; }
        if (changed) _store.TouchChannelRevision();
    }

    private async Task DownloadPrivateEpisodeAsync(Uri uri, string destination, int redirects, long maxBytes, CancellationToken token)
    {
        if (redirects > 6) throw new HttpRequestException("Too many media redirects.");
        await EnsurePublicHostAsync(uri, token);
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, uri);
        headRequest.Headers.Accept.ParseAdd("audio/mpeg, audio/*;q=0.9, application/octet-stream;q=0.8, */*;q=0.1");
        using var head = await _http.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, token);
        _logger.LogInformation("Tilapia private media HEAD host={Host} status={Status} type={ContentType} length={Length} ranges={Ranges} server={Server}", uri.DnsSafeHost, (int)head.StatusCode, head.Content.Headers.ContentType?.MediaType ?? "none", head.Content.Headers.ContentLength?.ToString() ?? "unknown", string.Join(',', head.Headers.AcceptRanges), string.Join(' ', head.Headers.Server.Select(x => x.ToString())));

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, null);
        request.Headers.Accept.ParseAdd("audio/mpeg, audio/*;q=0.9, application/octet-stream;q=0.8, */*;q=0.1");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null)
        {
            var next = new Uri(uri, response.Headers.Location);
            _logger.LogInformation("Tilapia private media GET redirect {FromHost} -> {ToHost} status={Status}", uri.DnsSafeHost, next.DnsSafeHost, (int)response.StatusCode);
            await DownloadPrivateEpisodeAsync(next, destination, redirects + 1, maxBytes, token);
            return;
        }
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "none";
        _logger.LogInformation("Tilapia private media GET host={Host} status={Status} type={ContentType} length={Length} ranges={Ranges} contentRange={ContentRange} server={Server}", uri.DnsSafeHost, (int)response.StatusCode, mediaType, response.Content.Headers.ContentLength?.ToString() ?? "unknown", string.Join(',', response.Headers.AcceptRanges), response.Content.Headers.ContentRange?.ToString() ?? "none", string.Join(' ', response.Headers.Server.Select(x => x.ToString())));
        if (!response.IsSuccessStatusCode)
        {
            var diagnostic = await ReadSafeErrorDiagnosticAsync(response, token);
            response.Headers.TryGetValues("cf-mitigated", out var mitigatedValues);
            response.Headers.TryGetValues("cf-ray", out var rayValues);
            _logger.LogWarning("Tilapia private media rejection host={Host} status={Status} cfMitigated={CfMitigated} cfRay={CfRay} error={Error}", uri.DnsSafeHost, (int)response.StatusCode, mitigatedValues is null ? "none" : string.Join(',', mitigatedValues), rayValues is null ? "none" : string.Join(',', rayValues), diagnostic);
            throw new HttpRequestException($"Ranged GET returned {(int)response.StatusCode} {response.StatusCode}; type={mediaType}; host={uri.DnsSafeHost}; error={diagnostic}", null, response.StatusCode);
        }
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) throw new HttpRequestException($"Media request returned non-audio content type {mediaType} from {uri.DnsSafeHost}.");
        if (response.Content.Headers.ContentLength > maxBytes) throw new HttpRequestException("Remote media exceeds the size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total > maxBytes) throw new HttpRequestException("Remote media exceeds the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        _logger.LogInformation("Tilapia private media download completed host={Host} bytes={Bytes}", uri.DnsSafeHost, total);
    }

    private static async Task<string> ReadSafeErrorDiagnosticAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            if (bytes.Length == 0) return "empty response";
            using var document = JsonDocument.Parse(bytes.AsMemory(0, Math.Min(bytes.Length, 32 * 1024)));
            var parts = new List<string>();
            CollectSafeJsonFields(document.RootElement, parts);
            return parts.Count == 0 ? "JSON response without code/title/detail/message" : string.Join("; ", parts.Take(8));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return "unreadable non-audio response";
        }
    }

    private static void CollectSafeJsonFields(JsonElement element, List<string> parts)
    {
        if (parts.Count >= 8) return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Name is "code" or "title" or "detail" or "message" or "status")
                {
                    var value = property.Value.GetString() ?? string.Empty;
                    value = Regex.Replace(value, @"https?://[^\s\""']+", "[URL redacted]", RegexOptions.IgnoreCase);
                    if (value.Length > 300) value = value[..300] + "...";
                    parts.Add($"{property.Name}={value}");
                }
                else CollectSafeJsonFields(property.Value, parts);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectSafeJsonFields(item, parts);
        }
    }

    public async Task<string> GetCachedArtworkAsync(string imageUrl, CancellationToken token)
    {
        var uri = new Uri(imageUrl);
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif")) extension = ".img";
        var path = Path.Combine(_store.ArtworkCachePath, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl))).ToLowerInvariant() + extension);
        if (File.Exists(path)) return path;
        var bytes = await GetBytesSafeAsync(uri, 0, 15L * 1024 * 1024, token);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, token);
        File.Move(temp, path, true);
        return path;
    }

    public async Task<string> NormalizeAndValidateAsync(string value, CancellationToken token)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) throw new ArgumentException("Only absolute HTTP(S) public feed URLs are allowed.");
        await EnsurePublicHostAsync(uri, token);
        return uri.AbsoluteUri;
    }

    private async Task<byte[]> GetBytesSafeAsync(Uri uri, int redirects, long maxBytes, CancellationToken token)
    {
        if (redirects > 4) throw new HttpRequestException("Too many redirects.");
        await EnsurePublicHostAsync(uri, token);
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null)
            return await GetBytesSafeAsync(new Uri(uri, response.Headers.Location), redirects + 1, maxBytes, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new HttpRequestException("Remote content exceeds the size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > maxBytes) throw new HttpRequestException("Remote content exceeds the size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static async Task EnsurePublicHostAsync(Uri uri, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("URLs containing credentials are not allowed.");
        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, token);
        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress)) throw new ArgumentException("Feed and media hosts must resolve only to public IP addresses.");
    }

    internal static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return b[0] is 0 or 10 or 127 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] >= 224;
        return address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None) || (b[0] & 0xfe) == 0xfc;
    }

    internal static PodcastFeed Parse(byte[] xml)
    {
        var doc = XDocument.Load(new MemoryStream(xml), LoadOptions.None);
        var root = doc.Root ?? throw new InvalidDataException("Empty feed.");
        var channel = root.Name.LocalName == "rss" ? root.Elements().FirstOrDefault(x => x.Name.LocalName == "channel") : root;
        if (channel is null) throw new InvalidDataException("RSS/Atom channel not found.");
        string? Value(XElement e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value?.Trim();
        var title = Value(channel, "title") ?? throw new InvalidDataException("Feed title is missing.");
        var imageElement = channel.Descendants().FirstOrDefault(x => x.Name.LocalName == "image");
        var image = ((string?)imageElement?.Attribute("href"))?.Trim()
            ?? ((string?)imageElement?.Attribute("url"))?.Trim()
            ?? imageElement?.Elements().FirstOrDefault(x => x.Name.LocalName is "url" or "href")?.Value?.Trim();
        var entries = channel.Elements().Where(x => x.Name.LocalName is "item" or "entry").Select((e, index) =>
        {
            var enclosure = e.Elements().FirstOrDefault(x => x.Name.LocalName == "enclosure") ?? e.Elements().FirstOrDefault(x => x.Name.LocalName == "link" && ((string?)x.Attribute("rel") == "enclosure"));
            var audio = (string?)enclosure?.Attribute("url") ?? (string?)enclosure?.Attribute("href");
            if (string.IsNullOrWhiteSpace(audio) || !Uri.TryCreate(audio, UriKind.Absolute, out _)) return null;
            var id = Value(e, "guid") ?? Value(e, "id") ?? audio;
            DateTimeOffset? published = DateTimeOffset.TryParse(Value(e, "pubDate") ?? Value(e, "published") ?? Value(e, "updated"), out var parsed) ? parsed : null;
            long? length = long.TryParse((string?)enclosure?.Attribute("length"), out var len) ? len : null;
            var runtimeTicks = ParseDurationTicks(Value(e, "duration"));
            return new PodcastEpisode(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant(), Value(e, "title") ?? $"Episode {index + 1}", Value(e, "description") ?? Value(e, "summary"), audio, (string?)enclosure?.Attribute("type"), published, length, runtimeTicks);
        }).Where(x => x is not null).Cast<PodcastEpisode>().ToArray();
        return new PodcastFeed(title, Value(channel, "description") ?? Value(channel, "subtitle"), image, entries);
    }

    private static long? ParseDurationTicks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (long.TryParse(value, out var seconds) && seconds >= 0) return TimeSpan.FromSeconds(seconds).Ticks;
        var parts = value.Split(':');
        if (parts.Length is < 2 or > 3 || parts.Any(x => !int.TryParse(x, out _))) return null;
        var numbers = parts.Select(int.Parse).ToArray();
        var duration = parts.Length == 3
            ? new TimeSpan(numbers[0], numbers[1], numbers[2])
            : new TimeSpan(0, numbers[0], numbers[1]);
        return duration.Ticks;
    }
}
