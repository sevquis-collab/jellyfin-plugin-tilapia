# Architecture

## Design goals

Tilapia uses Jellyfin's Channel API so remote podcasts remain separate from conventional media libraries. It does not create library folders, call library scans or write directly to Jellyfin's database.

## Components

- `PodcastChannel` presents the authenticated user's subscriptions and episodes through `IChannel` and supplies media information on demand.
- `PodcastsController` exposes authenticated per-user subscription endpoints.
- `TilapiaManagerController` serves the same-origin responsive manager and static assets.
- `PodcastDirectoryClient` performs bounded, cached searches against Apple's public podcast directory. Directory results are never trusted as subscriptions until the normal feed validation path succeeds.
- `PodcastFeedClient` validates destinations, downloads and parses feeds, and manages artwork/media caches.
- `OpmlService` parses and exports OPML with DTDs and external XML resolution disabled. Private feed addresses are excluded from export.
- `PodcastStore` keeps subscriptions in isolated JSON storage and protects private URLs using ASP.NET Core Data Protection.
- `PrivateFeedRefreshTask` periodically refreshes compatible private subscriptions while skipping unsupported Patreon feeds.

## Identity and isolation

Every management endpoint derives the listener identity from Jellyfin's authenticated principal. Subscriptions are filtered by owner, with one optional explicitly named account for locally cached private episodes. Shared public feed metadata and cached bytes may be deduplicated, but subscription visibility remains per-user.

## Playback

Public stream mode returns the feed enclosure as a remote media source. Public cache mode downloads an enclosure into Tilapia's isolated cache before returning a local source. Private mode is always local-cache-only and channel items remain hidden until their complete local file exists.

## Network safety

External URLs must use HTTP or HTTPS and resolve exclusively to public addresses. Redirect destinations are validated again. Feed, artwork and media downloads have explicit size limits and honour cancellation. Partial media is written to a temporary file and moved into place only after completion.

## Removal

The executable plugin folder and isolated `podcasts-v01` data are independent. Removing the executable leaves all existing Jellyfin libraries and database records untouched.
