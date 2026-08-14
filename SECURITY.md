# Security policy

## Reporting a vulnerability

Please do not disclose suspected vulnerabilities in a public issue. Use GitHub's **Report a vulnerability** option on the repository Security page to open a private security advisory.

Include the affected Tilapia version, Jellyfin version, reproduction steps and a description of the impact. Remove private RSS URLs, enclosure query strings, authentication tokens, passwords, usernames and unrelated log content.

## Security boundaries

Tilapia:

- derives subscription ownership from the authenticated Jellyfin token;
- does not accept a caller-supplied owner ID;
- rejects feed and redirect destinations resolving to loopback, private, link-local or multicast addresses;
- limits feed, artwork and media response sizes;
- protects private feed URLs at rest and masks them in responses;
- only exposes fully downloaded private media as local files;
- does not directly modify Jellyfin's database or existing media libraries.

No plugin can eliminate all risk because it executes inside the Jellyfin server process. Keep Jellyfin and Tilapia updated, use HTTPS for remote access, restrict server administration, and maintain normal Jellyfin backups.

## Supported versions

Security fixes are provided for the latest published Tilapia release compatible with a supported Jellyfin server line.
