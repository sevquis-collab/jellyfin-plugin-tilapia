namespace Jellyfin.Plugin.Podcasts;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum PlaybackMode { Stream, Cache }

public sealed record Subscription(Guid Id, Guid UserId, string FeedUrl, PlaybackMode Mode, DateTimeOffset CreatedAt, bool IsPrivate = false, int EpisodeLimit = 10, Guid? SharedWithUserId = null, string? SharedWithUserName = null, int RetentionWeeks = 4);
public sealed record PodcastFeed(string Title, string? Description, string? ImageUrl, IReadOnlyList<PodcastEpisode> Episodes);
public sealed record PodcastEpisode(string Id, string Title, string? Description, string AudioUrl, string? MimeType, DateTimeOffset? Published, long? Length, long? RuntimeTicks);
public sealed record PodcastSummary(string Title, string? Description, string? ImageUrl);
public sealed record AddSubscriptionRequest(string FeedUrl, [property: System.Text.Json.Serialization.JsonRequired] PlaybackMode Mode, [property: System.Text.Json.Serialization.JsonRequired] bool IsPrivate = false, [property: System.Text.Json.Serialization.JsonRequired] int EpisodeLimit = 10, string? SharedWithUserName = null, [property: System.Text.Json.Serialization.JsonRequired] int RetentionWeeks = 4);
public sealed record PreviewFeedRequest(string FeedUrl, [property: System.Text.Json.Serialization.JsonRequired] bool IsPrivate = false);
public sealed record FeedPreview(string FeedUrl, PodcastSummary Feed, int EpisodeCount);
public sealed record UpdatePlaybackModeRequest([property: System.Text.Json.Serialization.JsonRequired] PlaybackMode Mode);
public sealed record UpdateEpisodeLimitRequest([property: System.Text.Json.Serialization.JsonRequired] int EpisodeLimit);
public sealed record SubscriptionView(Subscription Subscription, PodcastSummary Feed, bool IsAvailable = true, DateTimeOffset? LastChecked = null, string? Error = null);
public sealed record PodcastDirectoryResult(string FeedUrl, string Title, string? Publisher, string? Description, string? ImageUrl, string? DirectoryUrl, int? EpisodeCount, string? Genre);
public sealed record ImportOpmlRequest(string Opml);
public sealed record OpmlImportIssue(string? Title, string Message);
public sealed record OpmlImportResult(int Found, int Added, int Skipped, IReadOnlyList<OpmlImportIssue> Issues);
public sealed record OpmlFeed(string? Title, string FeedUrl);
public sealed record FeedFetchStatus(DateTimeOffset LastChecked, DateTimeOffset? LastSuccessful, string? Error);
