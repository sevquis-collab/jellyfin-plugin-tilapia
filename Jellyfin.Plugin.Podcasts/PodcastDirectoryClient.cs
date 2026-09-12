using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Jellyfin.Plugin.Podcasts;

public sealed class PodcastDirectoryClient
{
    private const int MaximumResults = 25;
    private readonly HttpClient _http = new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromSeconds(12)
    };
    private readonly ConcurrentDictionary<string, (DateTimeOffset Time, IReadOnlyList<PodcastDirectoryResult> Results)> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _requests = new(1, 1);

    public PodcastDirectoryClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Tilapia/1.1.0");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("(Jellyfin-Podcast-Client)");
    }

    public async Task<IReadOnlyList<PodcastDirectoryResult>> SearchAsync(string query, string? country, CancellationToken token)
    {
        query = query.Trim();
        if (query.Length < 2) throw new ArgumentException("Enter at least two characters to search.");
        if (query.Length > 100) throw new ArgumentException("Search terms cannot exceed 100 characters.");
        country = NormalizeCountry(country);
        var key = $"{country}:{query.ToUpperInvariant()}";
        if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.Time < TimeSpan.FromMinutes(10)) return cached.Results;

        await _requests.WaitAsync(token);
        try
        {
            if (_cache.TryGetValue(key, out cached) && DateTimeOffset.UtcNow - cached.Time < TimeSpan.FromMinutes(10)) return cached.Results;
            var uri = new Uri($"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&media=podcast&entity=podcast&limit={MaximumResults}&country={country}");
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var results = await ParseAsync(stream, token);
            _cache[key] = (DateTimeOffset.UtcNow, results);
            return results;
        }
        finally
        {
            _requests.Release();
        }
    }

    internal static async Task<IReadOnlyList<PodcastDirectoryResult>> ParseAsync(Stream json, CancellationToken token = default)
    {
        using var document = await JsonDocument.ParseAsync(json, cancellationToken: token);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return [];
        var output = new List<PodcastDirectoryResult>();
        foreach (var item in results.EnumerateArray())
        {
            var feedUrl = Text(item, "feedUrl");
            var title = Text(item, "collectionName") ?? Text(item, "trackName");
            if (string.IsNullOrWhiteSpace(feedUrl) || string.IsNullOrWhiteSpace(title)
                || !Uri.TryCreate(feedUrl, UriKind.Absolute, out var feedUri)
                || feedUri.Scheme is not ("http" or "https")) continue;
            output.Add(new PodcastDirectoryResult(
                feedUri.AbsoluteUri,
                title,
                Text(item, "artistName"),
                null,
                Text(item, "artworkUrl600") ?? Text(item, "artworkUrl100"),
                Text(item, "collectionViewUrl"),
                Number(item, "trackCount"),
                Text(item, "primaryGenreName")));
        }

        return output;
    }

    private static string NormalizeCountry(string? value)
    {
        value = value?.Trim().ToUpperInvariant();
        return value is { Length: 2 } && value.All(char.IsAsciiLetterUpper) ? value : "US";
    }

    private static string? Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static int? Number(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : null;
}
