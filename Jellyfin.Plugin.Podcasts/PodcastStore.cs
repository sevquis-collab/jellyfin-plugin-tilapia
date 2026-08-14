using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace Jellyfin.Plugin.Podcasts;

public sealed class PodcastStore
{
    private readonly string _root;
    private readonly string _subscriptionsFile;
    private readonly string _revisionFile;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDataProtector _protector;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PodcastStore(IApplicationPaths paths)
    {
        _root = Path.Combine(paths.DataPath, "podcasts-v01");
        _subscriptionsFile = Path.Combine(_root, "subscriptions.json");
        _revisionFile = Path.Combine(_root, "channel-revision.txt");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(ArtworkCachePath);
        var keys = Path.Combine(_root, "keys");
        Directory.CreateDirectory(keys);
        _protector = DataProtectionProvider.Create(new DirectoryInfo(keys)).CreateProtector("Tilapia.PrivateRss.v1");
    }

    public string CachePath => Path.Combine(_root, "cache");
    public string ArtworkCachePath => Path.Combine(_root, "artwork");

    public string CacheRevision
    {
        get
        {
            var subscriptions = File.Exists(_subscriptionsFile) ? File.GetLastWriteTimeUtc(_subscriptionsFile).Ticks : 0;
            var media = File.Exists(_revisionFile) ? File.GetLastWriteTimeUtc(_revisionFile).Ticks : 0;
            return Math.Max(subscriptions, media).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public void TouchChannelRevision() => File.WriteAllText(_revisionFile, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

    public async Task<IReadOnlyList<Subscription>> GetForUserAsync(Guid userId, CancellationToken token)
        => (await ReadAllAsync(token)).Where(x => x.UserId == userId || x.SharedWithUserId == userId).ToArray();

    public async Task<IReadOnlyList<Subscription>> GetPrivateAsync(CancellationToken token)
        => (await ReadAllAsync(token)).Where(x => x.IsPrivate).ToArray();

    public async Task<Subscription?> GetByIdAsync(Guid id, CancellationToken token)
        => (await ReadAllAsync(token)).FirstOrDefault(x => x.Id == id);

    public async Task<Subscription> AddAsync(Guid userId, string normalizedFeedUrl, PlaybackMode mode, bool isPrivate, int episodeLimit, Guid? sharedId, string? sharedName, int retentionWeeks, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var all = (await ReadAllUnlockedAsync(token)).ToList();
            var existing = all.FirstOrDefault(x => x.UserId == userId && string.Equals(x.FeedUrl, normalizedFeedUrl, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
            var item = new Subscription(Guid.NewGuid(), userId, normalizedFeedUrl, mode, DateTimeOffset.UtcNow, isPrivate, NormalizeLimit(episodeLimit), sharedId, sharedName, retentionWeeks is 1 or 2 or 4 or 8 ? retentionWeeks : 4);
            all.Add(item);
            await WriteAllUnlockedAsync(all, token);
            return item;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid subscriptionId, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var all = (await ReadAllUnlockedAsync(token)).ToList();
            var removed = all.RemoveAll(x => x.UserId == userId && x.Id == subscriptionId) > 0;
            if (removed) await WriteAllUnlockedAsync(all, token);
            return removed;
        }
        finally { _gate.Release(); }
    }

    public async Task<Subscription?> UpdateModeAsync(Guid userId, Guid subscriptionId, PlaybackMode mode, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var all = (await ReadAllUnlockedAsync(token)).ToList();
            var index = all.FindIndex(x => x.UserId == userId && x.Id == subscriptionId);
            if (index < 0) return null;
            var updated = all[index] with { Mode = mode };
            all[index] = updated;
            await WriteAllUnlockedAsync(all, token);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public async Task<Subscription?> UpdateEpisodeLimitAsync(Guid userId, Guid subscriptionId, int episodeLimit, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var all = (await ReadAllUnlockedAsync(token)).ToList();
            var index = all.FindIndex(x => x.UserId == userId && x.Id == subscriptionId);
            if (index < 0) return null;
            var updated = all[index] with { EpisodeLimit = NormalizeLimit(episodeLimit) };
            all[index] = updated;
            await WriteAllUnlockedAsync(all, token);
            return updated;
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<Subscription>> ReadAllAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { return await ReadAllUnlockedAsync(token); }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<Subscription>> ReadAllUnlockedAsync(CancellationToken token)
    {
        if (!File.Exists(_subscriptionsFile)) return Array.Empty<Subscription>();
        await using var stream = File.OpenRead(_subscriptionsFile);
        var stored = await JsonSerializer.DeserializeAsync<List<StoredSubscription>>(stream, JsonOptions, token) ?? [];
        return stored.Select(x => new Subscription(x.Id, x.UserId, UnprotectUrl(x.FeedUrl, x.IsPrivate), x.Mode, x.CreatedAt, x.IsPrivate, NormalizeLimit(x.EpisodeLimit ?? 10), x.SharedWithUserId, x.SharedWithUserName, x.RetentionWeeks ?? 4)).ToArray();
    }

    private async Task WriteAllUnlockedAsync(List<Subscription> items, CancellationToken token)
    {
        var temp = _subscriptionsFile + ".tmp";
        var stored = items.Select(x => new StoredSubscription(x.Id, x.UserId, ProtectUrl(x.FeedUrl, x.IsPrivate), x.Mode, x.CreatedAt, x.IsPrivate, x.EpisodeLimit, x.SharedWithUserId, x.SharedWithUserName, x.RetentionWeeks)).ToList();
        await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, stored, JsonOptions, token);
        File.Move(temp, _subscriptionsFile, true);
    }

    private string ProtectUrl(string url, bool isPrivate) => isPrivate ? "protected:" + _protector.Protect(url) : url;

    private string UnprotectUrl(string value, bool isPrivate)
        => isPrivate && value.StartsWith("protected:", StringComparison.Ordinal) ? _protector.Unprotect(value[10..]) : value;

    public static int NormalizeLimit(int value) => value is 0 or 10 or 50 or 100 or 200 or 500 ? value : 10;

    private sealed record StoredSubscription(Guid Id, Guid UserId, string FeedUrl, PlaybackMode Mode, DateTimeOffset CreatedAt, bool IsPrivate = false, int? EpisodeLimit = null, Guid? SharedWithUserId = null, string? SharedWithUserName = null, int? RetentionWeeks = null);
}
