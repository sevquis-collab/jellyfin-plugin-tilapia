# Privacy and data handling

Tilapia is self-hosted. It has no Tilapia-operated cloud service, analytics or telemetry.

## Stored data

Tilapia stores:

- the Jellyfin user ID owning each subscription;
- public feed URLs;
- protected private feed URLs;
- playback mode and episode-limit preferences;
- shared public feed metadata and artwork caches;
- public cached episodes requested for playback;
- locally downloaded episodes for compatible private feeds.

This data is stored below Jellyfin's data directory in the isolated `podcasts-v01` folder.

## Credentials

Jellyfin passwords entered in the Tilapia manager are submitted to Jellyfin's normal authentication endpoint and are not stored by Tilapia. Jellyfin access tokens are held by the browser using session storage or, when the user selects persistent sign-in, local storage.

Private RSS URLs normally contain credentials. Tilapia protects them at rest using ASP.NET Core Data Protection, masks them in management responses and avoids writing them to logs. Server administrators with control of the Jellyfin host should still be considered trusted.

## External requests

When a user searches for a podcast, Tilapia sends the search terms and a two-letter country code from the Jellyfin server to Apple's public podcast directory. Apple receives the server's public IP address, while an artwork host may receive the browser's IP address when displaying search-result artwork. Tilapia does not send the user's Jellyfin identity or listening history.

Tilapia also contacts the feed, artwork and media hosts selected by users, plus redirect destinations returned by those hosts. Those providers receive the Jellyfin server's public IP address and Tilapia's honest podcast-client User-Agent.

## Sharing

Public feed caches may be reused when multiple server users subscribe to the same public URL. Private subscriptions are visible to their owner and may optionally grant playback of locally cached episodes to one additional Jellyfin account.

OPML export contains public subscription URLs only. Private/member feed addresses are excluded because they commonly act as credentials. Imported OPML feeds are validated using the same network protections as feeds added manually.

## Deletion

Removing a subscription removes it from the manager but may leave shared cached bytes needed by another subscription. Deleting the isolated `podcasts-v01` directory while Jellyfin is stopped removes all Tilapia subscriptions and caches.
