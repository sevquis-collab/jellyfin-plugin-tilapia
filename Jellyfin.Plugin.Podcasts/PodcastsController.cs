using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Podcasts;

[ApiController]
[Authorize]
[Route("Podcasts")]
public sealed class PodcastsController : ControllerBase
{
    private readonly PodcastStore _store;
    private readonly PodcastFeedClient _feeds;
    private readonly ILogger<PodcastsController> _logger;
    private readonly IUserManager _users;

    public PodcastsController(PodcastStore store, PodcastFeedClient feeds, ILogger<PodcastsController> logger, IUserManager users)
        => (_store, _feeds, _logger, _users) = (store, feeds, logger, users);

    [HttpGet("Subscriptions")]
    public async Task<ActionResult<IReadOnlyList<SubscriptionView>>> GetSubscriptions(CancellationToken token)
    {
        var userId = GetUserId();
        _logger.LogInformation("Listing podcast subscriptions for user {UserId}", userId);
        var subscriptions = await _store.GetForUserAsync(userId, token);
        var result = new List<SubscriptionView>();
        foreach (var subscription in subscriptions)
        {
            var feed = await _feeds.GetFeedAsync(subscription.FeedUrl, token);
            result.Add(new(ToSafeSubscription(subscription), Summary(feed)));
        }
        return result;
    }

    [HttpPost("Subscriptions")]
    public async Task<ActionResult<SubscriptionView>> AddSubscription([FromBody] AddSubscriptionRequest request, CancellationToken token)
    {
        var userId = GetUserId();
        _logger.LogInformation("Validating {FeedKind} podcast feed for user {UserId}", request.IsPrivate ? "private" : "public", userId);
        try
        {
            var url = await _feeds.NormalizeAndValidateAsync(request.FeedUrl, token);
            if (request.IsPrivate && IsPatreon(url))
                return BadRequest(new { error = "Patreon private RSS is currently unsupported because Patreon rejects its episode media requests. Other standards-based private RSS providers can still be used." });
            var feed = await _feeds.GetFeedAsync(url, token);
            var mode = request.IsPrivate ? PlaybackMode.Cache : request.Mode;
            Guid? sharedId = null;
            string? sharedName = null;
            if (request.IsPrivate && !string.IsNullOrWhiteSpace(request.SharedWithUserName))
            {
                var sharedUser = _users.GetUserByName(request.SharedWithUserName.Trim());
                if (sharedUser is null) return BadRequest(new { error = "That exact Jellyfin username was not found." });
                if (sharedUser.Id == userId) return BadRequest(new { error = "The additional account must be different from your account." });
                sharedId = sharedUser.Id;
                sharedName = sharedUser.Username;
            }
            var privateLimit = request.IsPrivate && request.EpisodeLimit is not (1 or 3 or 5 or 10) ? 3 : request.EpisodeLimit;
            var subscription = await _store.AddAsync(userId, url, mode, request.IsPrivate, privateLimit, sharedId, sharedName, request.RetentionWeeks, token);
            if (request.IsPrivate)
            {
                try { await _feeds.RefreshPrivateAsync(subscription, token); }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or HttpRequestException or IOException)
                {
                    _logger.LogWarning("Private podcast was saved but its first episode could not be downloaded: {Reason}", ex.Message);
                }
            }
            _logger.LogInformation("Added {FeedKind} podcast feed for user {UserId} in {PlaybackMode} mode", request.IsPrivate ? "private" : "public", userId, mode);
            return Ok(new SubscriptionView(ToSafeSubscription(subscription), Summary(feed)));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or HttpRequestException or System.Xml.XmlException)
        {
            _logger.LogWarning("Rejected {FeedKind} podcast feed for user {UserId}: {Reason}", request.IsPrivate ? "private" : "public", userId, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("Feeds/Preview")]
    public async Task<ActionResult<FeedPreview>> PreviewFeed([FromBody] PreviewFeedRequest request, CancellationToken token)
    {
        _ = GetUserId();
        try
        {
            var url = await _feeds.NormalizeAndValidateAsync(request.FeedUrl, token);
            if (request.IsPrivate && IsPatreon(url))
                return BadRequest(new { error = "Patreon private RSS is currently unsupported because Patreon rejects its episode media requests. Other standards-based private RSS providers can still be tested." });
            var feed = await _feeds.GetFeedAsync(url, token);
            return Ok(new FeedPreview(request.IsPrivate ? "Private RSS feed" : url, Summary(feed), feed.Episodes.Count));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or HttpRequestException or System.Xml.XmlException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("Subscriptions/{subscriptionId:guid}/Mode")]
    public async Task<ActionResult<Subscription>> UpdatePlaybackMode(Guid subscriptionId, [FromBody] UpdatePlaybackModeRequest request, CancellationToken token)
    {
        var userId = GetUserId();
        var existing = (await _store.GetForUserAsync(userId, token)).FirstOrDefault(x => x.Id == subscriptionId);
        if (existing is null) return NotFound();
        if (existing.IsPrivate && request.Mode != PlaybackMode.Cache) return BadRequest(new { error = "Private RSS subscriptions must remain locally cached." });
        var updated = await _store.UpdateModeAsync(userId, subscriptionId, request.Mode, token);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPut("Subscriptions/{subscriptionId:guid}/EpisodeLimit")]
    public async Task<ActionResult<Subscription>> UpdateEpisodeLimit(Guid subscriptionId, [FromBody] UpdateEpisodeLimitRequest request, CancellationToken token)
    {
        var updated = await _store.UpdateEpisodeLimitAsync(GetUserId(), subscriptionId, request.EpisodeLimit, token);
        return updated is null ? NotFound() : Ok(ToSafeSubscription(updated));
    }

    [HttpDelete("Subscriptions/{subscriptionId:guid}")]
    public async Task<IActionResult> RemoveSubscription(Guid subscriptionId, CancellationToken token)
        => await _store.RemoveAsync(GetUserId(), subscriptionId, token) ? NoContent() : NotFound();

    private Guid GetUserId()
    {
        var value = User.Claims.FirstOrDefault(x => string.Equals(x.Type, "Jellyfin-UserId", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty) throw new UnauthorizedAccessException("A user access token is required; server API keys are not accepted.");
        return id;
    }

    private static PodcastSummary Summary(PodcastFeed feed) => new(feed.Title, feed.Description, feed.ImageUrl);

    private static bool IsPatreon(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.DnsSafeHost.Equals("patreon.com", StringComparison.OrdinalIgnoreCase) || uri.DnsSafeHost.EndsWith(".patreon.com", StringComparison.OrdinalIgnoreCase));

    private static Subscription ToSafeSubscription(Subscription subscription)
        => subscription.IsPrivate ? subscription with { FeedUrl = "Private RSS feed" } : subscription;
}
