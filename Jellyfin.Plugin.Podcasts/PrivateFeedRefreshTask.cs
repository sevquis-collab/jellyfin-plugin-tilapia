using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts;

public sealed class PrivateFeedRefreshTask : IScheduledTask
{
    private readonly PodcastStore _store;
    private readonly PodcastFeedClient _feeds;
    private readonly ILogger<PrivateFeedRefreshTask> _logger;
    public PrivateFeedRefreshTask(PodcastStore store, PodcastFeedClient feeds, ILogger<PrivateFeedRefreshTask> logger) => (_store, _feeds, _logger) = (store, feeds, logger);
    public string Name => "Refresh Tilapia private podcasts";
    public string Key => "TilapiaRefreshPrivateFeeds";
    public string Description => "Downloads new episodes and applies retention limits for private RSS subscriptions.";
    public string Category => "Tilapia";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [new() { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(1).Ticks }];
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var subscriptions = await _store.GetPrivateAsync(cancellationToken);
        for (var i = 0; i < subscriptions.Count; i++)
        {
            if (IsPatreon(subscriptions[i].FeedUrl))
            {
                _logger.LogInformation("Skipping unsupported Patreon private RSS subscription {SubscriptionId}", subscriptions[i].Id);
                progress.Report((i + 1d) / Math.Max(1, subscriptions.Count) * 100);
                continue;
            }
            try { await _feeds.RefreshPrivateAsync(subscriptions[i], cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Unable to refresh private podcast {SubscriptionId}", subscriptions[i].Id); }
            progress.Report((i + 1d) / Math.Max(1, subscriptions.Count) * 100);
        }
    }

    private static bool IsPatreon(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.DnsSafeHost.Equals("patreon.com", StringComparison.OrdinalIgnoreCase) || uri.DnsSafeHost.EndsWith(".patreon.com", StringComparison.OrdinalIgnoreCase));
}
