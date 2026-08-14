namespace Jellyfin.Plugin.Podcasts;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum PlaybackMode { Stream, Cache }

public sealed record Subscription(Guid Id, Guid UserId, string FeedUrl, PlaybackMode Mode, DateTimeOffset CreatedAt, bool IsPrivate = false, int EpisodeLimit = 10, Guid? SharedWithUserId = null, string? SharedWithUserName = null, int RetentionWeeks = 4);
public sealed record PodcastFeed(string Title, string? Description, string? ImageUrl, IReadOnlyList<PodcastEpisode> Episodes);
public sealed record PodcastEpisode(string Id, string Title, string? Description, string AudioUrl, string? MimeType, DateTimeOffset? Published, long? Length, long? RuntimeTicks);
public sealed record PodcastSummary(string Title, string? Description, string? ImageUrl);
public sealed record AddSubscriptionRequest(string FeedUrl, PlaybackMode Mode, bool IsPrivate = false, int EpisodeLimit = 10, string? SharedWithUserName = null, int RetentionWeeks = 4);
public sealed record PreviewFeedRequest(string FeedUrl, bool IsPrivate = false);
public sealed record FeedPreview(string FeedUrl, PodcastSummary Feed, int EpisodeCount);
public sealed record UpdatePlaybackModeRequest(PlaybackMode Mode);
public sealed record UpdateEpisodeLimitRequest(int EpisodeLimit);
public sealed record SubscriptionView(Subscription Subscription, PodcastSummary Feed);
