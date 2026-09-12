# Changelog

All notable changes to Tilapia are documented here.

## 1.1.0.0 — 2026-09-12

- Rebuilt for Jellyfin Server 12 and .NET 10.
- Added public podcast discovery search with one-click subscriptions.
- Added OPML import and public-subscription export.
- Added feed availability, last-checked information and manual rechecks.
- Reworked the responsive manager so direct RSS and private feeds live under Advanced.
- Preserved the existing `podcasts-v01` data location for in-place upgrades.

## 1.0.0.0 — 2026-08-14

- Added per-user public RSS and Atom podcast subscriptions.
- Added a responsive user-facing manager with Quick Connect and password sign-in.
- Added newest-first browsing and configurable public episode limits.
- Added direct streaming and cache-when-played modes for public episodes.
- Added shared metadata, artwork and media cache deduplication.
- Added feed artwork support across channel folders and episodes.
- Added experimental provider-dependent private RSS caching and one-user sharing.
- Added explicit rejection of unsupported Patreon private RSS feeds.
- Added SSRF protections, redirect validation, download limits and secret redaction.
- Confirmed that installation and removal do not alter existing Jellyfin libraries.
