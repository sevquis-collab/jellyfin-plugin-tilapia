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

Tilapia contacts only the feed, artwork and media hosts selected by users, plus redirect destinations returned by those hosts. Those providers receive the Jellyfin server's public IP address and Tilapia's honest podcast-client User-Agent.

## Sharing

Public feed caches may be reused when multiple server users subscribe to the same public URL. Private subscriptions are visible to their owner and may optionally grant playback of locally cached episodes to one additional Jellyfin account.

## Deletion

Removing a subscription removes it from the manager but may leave shared cached bytes needed by another subscription. Deleting the isolated `podcasts-v01` directory while Jellyfin is stopped removes all Tilapia subscriptions and caches.
