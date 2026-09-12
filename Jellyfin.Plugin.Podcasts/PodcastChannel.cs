using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts;

public sealed class PodcastChannel : IChannel, IRequiresMediaInfoCallback, IHasCacheKey
{
    private readonly PodcastStore _store;
    private readonly PodcastFeedClient _feeds;
    private readonly ILogger<PodcastChannel> _logger;

    public PodcastChannel(PodcastStore store, PodcastFeedClient feeds, ILogger<PodcastChannel> logger)
        => (_store, _feeds, _logger) = (store, feeds, logger);

    public string Name => "Podcasts";
    public string Description => "Your podcasts, inside Jellyfin. Public RSS feeds are supported; private feeds depend on the provider.";
    public string DataVersion => "1.1.0";
    public string HomePageUrl => "https://github.com/sevquis-collab/jellyfin-plugin-tilapia";
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;
    public bool IsEnabledFor(string userId) => Guid.TryParse(userId, out _);
    public string GetCacheKey(string? userId) => $"v110-{userId ?? "anonymous"}-{_store.CacheRevision}";
    public IEnumerable<ImageType> GetSupportedChannelImages() => Array.Empty<ImageType>();
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken) => throw new NotSupportedException();

    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        MediaTypes = [ChannelMediaType.Audio],
        ContentTypes = [ChannelMediaContentType.Podcast],
        DefaultSortFields = [ChannelItemSortField.PremiereDate],
        SupportsSortOrderToggle = true,
        MaxPageSize = 200,
        AutoRefreshLevels = 1,
        SupportsContentDownloading = false
    };

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken token)
    {
        var subscriptions = (await _store.GetForUserAsync(query.UserId, token)).Where(x => !IsPatreon(x.FeedUrl)).ToArray();
        if (string.IsNullOrWhiteSpace(query.FolderId))
        {
            var folders = new List<ChannelItemInfo>();
            foreach (var subscription in subscriptions)
            {
                var feed = await _feeds.GetFeedAsync(subscription.FeedUrl, token);
                folders.Add(new ChannelItemInfo { Id = subscription.Id.ToString("N"), Name = feed.Title, Overview = feed.Description, ImageUrl = await GetArtworkPathAsync(feed.ImageUrl, subscription.IsPrivate, token), Type = ChannelItemType.Folder, FolderType = ChannelFolderType.Container });
            }
            return Page(folders, query);
        }

        if (!Guid.TryParse(query.FolderId, out var subscriptionId)) return new ChannelItemResult();
        var selected = subscriptions.FirstOrDefault(x => x.Id == subscriptionId);
        if (selected is null) return new ChannelItemResult();
        var podcast = await _feeds.GetFeedAsync(selected.FeedUrl, token);
        var artworkPath = await GetArtworkPathAsync(podcast.ImageUrl, selected.IsPrivate, token);
        var availableEpisodes = selected.IsPrivate ? podcast.Episodes.Where(x => _feeds.GetPrivateEpisodePath(selected.Id, x) is not null).ToArray() : podcast.Episodes;
        var recentEpisodes = selected.EpisodeLimit == 0
            ? availableEpisodes
            : availableEpisodes.Select((episode, index) => (Episode: episode, Index: index))
                .OrderByDescending(x => x.Episode.Published.HasValue)
                .ThenByDescending(x => x.Episode.Published)
                .ThenBy(x => x.Index)
                .Take(selected.EpisodeLimit)
                .Select(x => x.Episode)
                .ToArray();
        var limitedEpisodes = SortEpisodes(recentEpisodes, query.SortBy, query.SortDescending);
        var episodeNumbers = podcast.Episodes.Select((episode, index) => (episode.Id, Number: podcast.Episodes.Count - index))
            .ToDictionary(x => x.Id, x => x.Number, StringComparer.Ordinal);
        IEnumerable<PodcastEpisode> requestedEpisodes = limitedEpisodes.Skip(query.StartIndex ?? 0);
        if (query.Limit is int pageLimit) requestedEpisodes = requestedEpisodes.Take(pageLimit);
        var episodes = requestedEpisodes.Select(episode => new ChannelItemInfo
        {
            Id = $"{selected.Id:N}:{episode.Id}", Name = episode.Title, SeriesName = podcast.Title, Overview = episode.Description,
            Type = ChannelItemType.Media, MediaType = ChannelMediaType.Audio, ContentType = ChannelMediaContentType.Podcast,
            ImageUrl = artworkPath,
            PremiereDate = episode.Published?.UtcDateTime, DateCreated = episode.Published?.UtcDateTime, IndexNumber = episodeNumbers[episode.Id],
            RunTimeTicks = episode.RuntimeTicks,
            MediaSources = selected.Mode == PlaybackMode.Stream ? [CreateSource(episode.AudioUrl, episode)] : []
        }).ToList();
        return new ChannelItemResult { Items = episodes.ToArray(), TotalRecordCount = limitedEpisodes.Count };
    }

    private static IReadOnlyList<PodcastEpisode> SortEpisodes(IReadOnlyList<PodcastEpisode> episodes, ChannelItemSortField? sortBy, bool descending)
    {
        var indexed = episodes.Select((episode, index) => (Episode: episode, Index: index));
        if (sortBy is null or ChannelItemSortField.PremiereDate or ChannelItemSortField.DateCreated)
        {
            var newestFirst = sortBy is null || descending;
            return (newestFirst
                    ? indexed.OrderByDescending(x => x.Episode.Published.HasValue).ThenByDescending(x => x.Episode.Published).ThenBy(x => x.Index)
                    : indexed.OrderByDescending(x => x.Episode.Published.HasValue).ThenBy(x => x.Episode.Published).ThenBy(x => x.Index))
                .Select(x => x.Episode).ToArray();
        }

        if (sortBy == ChannelItemSortField.Name)
        {
            return (descending ? indexed.OrderByDescending(x => x.Episode.Title) : indexed.OrderBy(x => x.Episode.Title))
                .ThenBy(x => x.Index).Select(x => x.Episode).ToArray();
        }

        if (sortBy == ChannelItemSortField.Runtime)
        {
            return (descending ? indexed.OrderByDescending(x => x.Episode.RuntimeTicks) : indexed.OrderBy(x => x.Episode.RuntimeTicks))
                .ThenBy(x => x.Index).Select(x => x.Episode).ToArray();
        }

        return indexed.Select(x => x.Episode).ToArray();
    }

    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken token)
    {
        var parts = id.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var subscriptionId)) return [];
        // Channel callback lacks a user parameter; the opaque id only resolves a subscription and never exposes other feeds in browsing.
        // Search is limited to subscription metadata stored by this plugin, not Jellyfin's database.
        var subscription = await _store.GetByIdAsync(subscriptionId, token);
        if (subscription is null || IsPatreon(subscription.FeedUrl)) return [];
        var feed = await _feeds.GetFeedAsync(subscription.FeedUrl, token);
        var episode = feed.Episodes.FirstOrDefault(x => x.Id == parts[1]);
        if (episode is null) return [];
        var path = subscription.IsPrivate ? _feeds.GetPrivateEpisodePath(subscription.Id, episode) : subscription.Mode == PlaybackMode.Cache ? await _feeds.GetCachedEpisodeAsync(episode.AudioUrl, token) : episode.AudioUrl;
        if (path is null) return [];
        return [CreateSource(path, episode)];
    }

    private async Task<string?> GetArtworkPathAsync(string? imageUrl, bool isPrivate, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return null;
        try
        {
            return await _feeds.GetCachedArtworkAsync(imageUrl, token);
        }
        catch (Exception ex) when (ex is ArgumentException or HttpRequestException or IOException)
        {
            if (isPrivate)
            {
                _logger.LogWarning("Unable to cache artwork for a private podcast; remote artwork will not be exposed: {Reason}", ex.Message);
                return null;
            }
            _logger.LogWarning(ex, "Unable to cache public podcast artwork {ImageUrl}; retaining the remote URL", imageUrl);
            return imageUrl;
        }
    }

    private static MediaSourceInfo CreateSource(string path, PodcastEpisode episode)
    {
        var container = GetContainer(path, episode.MimeType);
        return new MediaSourceInfo
        {
            Id = episode.Id,
            Path = path,
            Protocol = Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? MediaProtocol.Http : MediaProtocol.File,
            Container = container,
            IsRemote = path.StartsWith("http", StringComparison.OrdinalIgnoreCase),
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            RunTimeTicks = episode.RuntimeTicks,
            MediaStreams =
            [
                new MediaStream
                {
                    Type = MediaStreamType.Audio,
                    Index = -1,
                    Codec = GetCodec(container, episode.MimeType)
                }
            ]
        };
    }

    private static string? GetContainer(string path, string? mimeType)
    {
        var subtype = mimeType?.Split(';', 2)[0].Split('/').LastOrDefault()?.ToLowerInvariant();
        if (subtype is "mpeg" or "mp3") return "mp3";
        if (subtype is "mp4" or "m4a" or "x-m4a") return "m4a";
        if (subtype is "aac" or "aacp") return "aac";
        if (subtype is "ogg" or "opus" or "flac" or "wav" or "webm") return subtype;
        var extension = Path.GetExtension(Uri.TryCreate(path, UriKind.Absolute, out var uri) ? uri.AbsolutePath : path).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(extension) ? subtype : extension;
    }

    private static bool IsPatreon(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.DnsSafeHost.Equals("patreon.com", StringComparison.OrdinalIgnoreCase) || uri.DnsSafeHost.EndsWith(".patreon.com", StringComparison.OrdinalIgnoreCase));

    private static string? GetCodec(string? container, string? mimeType)
    {
        if (container == "mp3" || mimeType?.Contains("mpeg", StringComparison.OrdinalIgnoreCase) == true) return "mp3";
        if (container is "m4a" or "aac") return "aac";
        if (container == "webm" && mimeType?.Contains("opus", StringComparison.OrdinalIgnoreCase) == true) return "opus";
        return container;
    }

    private static ChannelItemResult Page(List<ChannelItemInfo> items, InternalChannelItemQuery query)
    {
        var total = items.Count;
        IEnumerable<ChannelItemInfo> page = items.Skip(query.StartIndex ?? 0);
        if (query.Limit is int limit) page = page.Take(limit);
        return new ChannelItemResult { Items = page.ToArray(), TotalRecordCount = total };
    }
}
