# Contributing

Thank you for helping improve Tilapia.

## Before opening a change

- Search existing issues first.
- Keep provider-specific integrations separate from the stable public RSS path.
- Do not include private RSS URLs, enclosure tokens, Jellyfin access tokens, passwords or unredacted server logs.
- Do not add code intended to bypass provider access controls or terms of service.

## Development

The current Tilapia line targets Jellyfin Server 12 and .NET 10. The 1.0 release remains available for Jellyfin 10.11.5 through 10.11.11.

```powershell
dotnet restore .\Tilapia.sln
dotnet build .\Tilapia.sln -c Release --no-restore -warnaserror
dotnet test .\Tilapia.sln -c Release --no-build
node --check .\Jellyfin.Plugin.Podcasts\Manager\manager.js
snyk test --all-projects --severity-threshold=low --policy-path=.\.snyk
```

New parsing or security behaviour should include a focused automated test. Changes that accept external URLs must preserve public-address validation, redirect revalidation, response-size limits and cancellation.

## Pull requests

- Explain the user-visible result and why it is needed.
- Keep unrelated formatting or refactors out of the change.
- Update `CHANGELOG.md` for user-visible changes.
- Confirm that no Jellyfin host assemblies are present in release artifacts.
- Confirm that public feeds still work for separate non-admin Jellyfin users.

By contributing, you agree that your contribution is licensed under GPL-3.0.
