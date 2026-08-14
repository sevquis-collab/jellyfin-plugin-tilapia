# Path to official Jellyfin distribution

Tilapia is currently an independent community plugin. An official submission should happen only after the public-feed release has been exercised on supported Jellyfin server and client versions.

## Before proposing inclusion

1. Keep the source and compiled plugin GPL-3.0 licensed.
2. Track the current Jellyfin stable ABI and test upgrades from the previous supported ABI.
3. Keep CI green for a clean build, unit tests and static analysis.
4. Publish reproducible release ZIPs and a working third-party catalog manifest.
5. Document installation, upgrade, uninstall, privacy, storage and support behaviour.
6. Resolve any known security issues, especially SSRF protection and private-feed secret handling.
7. Collect real-world results from Jellyfin Web, Android, Android TV, iOS and desktop clients.

## Submission approach

Open a proposal with the Jellyfin project and follow the maintainers' current plugin-repository contribution process. Include:

- the source repository and GPL-3.0 license;
- the stable release and catalog URL;
- the plugin GUID and supported Jellyfin ABI;
- a concise feature and privacy description;
- build and test evidence;
- screenshots of the manager and channel in supported clients;
- known limitations, including provider-dependent private feeds.

Do not describe Tilapia as officially supported until the Jellyfin project has accepted and published it.

## Release checklist

- Update versions in `build.yaml`, the project file, channel data version, user agent and manager asset query strings.
- Update `CHANGELOG.md` and compatibility documentation.
- Run `dotnet restore`, `dotnet build -c Release` and `dotnet test -c Release`.
- Build the ZIP with the Jellyfin plugin repository tooling (`jprm`).
- Publish the ZIP on a GitHub Release.
- Add the release URL and MD5 checksum to `manifest.json`.
- Test a catalog installation, upgrade and removal on a disposable Jellyfin instance.
