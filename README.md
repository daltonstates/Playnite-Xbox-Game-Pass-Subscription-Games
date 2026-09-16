# Subscription Libraries for Playnite

Subscription Libraries is a Playnite library extension that makes games available through temporary gaming subscriptions searchable beside games you own. Version 1 imports the current **PC Game Pass** catalog as separate, clearly labeled, uninstalled Playnite entries.

It never merges with or modifies Steam, Epic, GOG, Amazon, Xbox, or other library records. If you own a game on Steam and can also access it through PC Game Pass, both records can coexist.

> **Screenshot placeholder:** settings page and an example search showing owned and PC Game Pass records side by side will be added before an add-on database release.

## Current status

- Production provider: PC Game Pass
- Default market/language: United States (`US`), English (`en-US`)
- Authentication: none
- Cache lifetime: 24 hours
- Playnite SDK: 6.17.0
- Plugin target: .NET Framework 4.6.2

The live US validation on 2026-09-16 retrieved 528 catalog IDs, resolved all 528 products, accepted 522 with Windows-PC evidence, rejected 6 Xbox-only records, and left 0 unclassified. Counts are expected to change with the catalog.

## Requirements

- Playnite 10.60 (current stable during verification) or a later version compatible with PlayniteSDK 6.17.0
- Windows supported by Playnite
- Internet access for catalog refreshes
- No Xbox username, Microsoft password, OAuth login, or API key

For source builds:

- Visual Studio 2022 with the .NET desktop development workload, or a current .NET SDK capable of building `net462` and `net8.0`
- PowerShell for the packaging script
- `Toolbox.exe` from a current Playnite installation to create `.pext` packages

### Visual Studio 2022 setup

1. Install Visual Studio 2022 with the **.NET desktop development** workload and the .NET Framework 4.6.2 targeting pack.
2. Open `SubscriptionLibraries.sln` and restore NuGet packages when prompted.
3. Select `Debug` or `Release` for `Any CPU`, then choose **Build > Build Solution**.
4. Use Test Explorer for the fixture suite; the live integration test remains opt-in and is not contacted by a normal test run.

## Install

### Packaged extension

1. Open the generated `.pext` file with Playnite.
2. Confirm the installation and restart Playnite.
3. Open **Add-ons > Extension settings > Libraries > Subscription Libraries - PC Game Pass**.
4. Leave PC Game Pass enabled, choose a market/language, and save.
5. Run **Update Game Library** and select PC Game Pass.

### Developer installation

1. Build the Debug configuration.
2. In Playnite, open **Settings > For developers**.
3. Add `SubscriptionLibraries/bin/Debug/net462` as a development plugin path.
4. Restart Playnite after each plugin rebuild; managed plugins cannot be reloaded in-process.

## Build and test

From the repository root:

```powershell
dotnet restore .\SubscriptionLibraries.sln
dotnet build .\SubscriptionLibraries.sln -c Debug
dotnet test .\SubscriptionLibraries.Tests\SubscriptionLibraries.Tests.csproj -c Debug
```

The normal suite uses sanitized local JSON fixtures and does not contact Microsoft. To opt into the live integration test:

```powershell
$env:SUBSCRIPTIONLIBRARIES_RUN_LIVE_TESTS = '1'
dotnet test .\SubscriptionLibraries.Tests\SubscriptionLibraries.Tests.csproj -c Release --filter Category=Integration
```

## Live catalog diagnostic tool

The CLI verifies Microsoft behavior without Playnite:

```powershell
dotnet run --project .\SubscriptionLibraries.CatalogTool -- --region US --language en-US --include-rejected
```

Optional arguments include `--json <path>` and `--sigl-id <guid>`. The output reports total IDs, resolved products, accepted PC products, rejected non-PC products, unclassified products, missing metadata, and samples.

## Release package

Build, stage only the required runtime files, and invoke Playnite's official packer:

```powershell
.\build\pack.ps1 -Configuration Release -ToolboxPath 'C:\Path\To\Playnite\Toolbox.exe'
```

The script writes the extension staging directory and `.pext` package under `artifacts/`. If Playnite is installed in a standard location, `-ToolboxPath` can be omitted. `Playnite.SDK.dll` and the Playnite-hosted JSON assembly are deliberately not included because the host supplies them.

## How synchronization works

1. The provider requests the configured PC Game Pass collection from `catalog.gamepass.com/sigls/v2`.
2. Stable Microsoft product IDs are deduplicated.
3. Product metadata is resolved from `displaycatalog.mp.microsoft.com/v7.0/products` in batches of at most 200.
4. Every product must have affirmative Windows PC evidence. Xbox-only and unclassified records are excluded.
5. Accepted products become Playnite entries with:
   - Library/source: **PC Game Pass**
   - Platform: Playnite's canonical **PC (Windows)** platform
   - Tags: **Subscription: PC Game Pass** and **Access: Subscription**
   - Microsoft Store link, cover/background art, description, companies, and release date when supplied
6. A plugin-owned reconciliation pass adds new records and refreshes only the extension's status tags on existing records.
7. Results are cached in Playnite's plugin data directory.

The stable Microsoft product ID is the library `GameId`; titles are never primary identifiers.

## Temporary access and duplicates

Subscription access is modeled as `Active`, `LeavingSoon`, or `Removed`. Microsoft does not currently expose dependable leaving dates through the selected collection, so V1 does not invent them or add a “Leaving Game Pass” tag without evidence.

On a successful refresh, a previously cached game that disappears is retained in cache history as `Removed` with the detection timestamp. Its plugin-owned Playnite record is retained for auditability, loses the **Access: Subscription** tag, and receives **Access: Removed** plus **Left PC Game Pass**. If the title returns, those managed tags switch back to active. Unrelated user tags are preserved, manually deleted records remain on Playnite's import-exclusion list, and records from other plugins are never changed.

New additions detected after the first successful baseline receive a detected `DateAdded`; recent entries can receive a temporary “Recently Added to Game Pass” tag. The first catalog is not falsely labeled as newly added.

Matching support reports likely relationships by stable IDs, metadata IDs, exact normalized title, then conservative fuzzy title. It never automatically merges title-only matches.

## Cache and failure behavior

- Default freshness: 24 hours, configurable from 1 to 720 hours.
- A fresh matching market/language cache avoids network traffic.
- Disabling “Refresh during Playnite library update” uses the last cache; an initial fetch still occurs if no cache exists.
- **Refresh Now** forces a network refresh of the cache. Run **Update Game Library** afterward to apply it to Playnite.
- If Microsoft fails after a valid cache exists, the plugin logs a warning and returns the cached catalog, even when stale.
- If an ID remains in the PC collection but its current metadata is missing or unclassifiable, the last verified active record is preserved instead of falsely declaring that the game left the service.
- Cache writes are atomic, and malformed cache files are ignored.
- A suspicious live drop below half of a prior sizable catalog is rejected to prevent a transient upstream issue from wiping the library view.

## Data sources and reliability

The two Microsoft endpoints are public-facing catalog interfaces used by Microsoft experiences and community implementations, but Microsoft does not document them as a guaranteed third-party API. Their schemas, identifiers, or availability may change without notice. Endpoint definitions and the PC collection ID are isolated in `GamePassConstants`; the SIGL ID is also available under Advanced settings for recovery.

See [docs/RESEARCH.md](docs/RESEARCH.md) for exact request formats, platform evidence, current diagnostics, and source references.

## Privacy and security

V1 sends only the configured market, language, public collection ID, and public Microsoft product IDs. It does not access personal Xbox data, request credentials, persist tokens, bypass authentication, or run a hosted service. Logs omit secrets and full request queries.

## Troubleshooting

- **No games appear:** confirm PC Game Pass is enabled, save settings, then run Update Game Library with the PC Game Pass library selected.
- **Refresh failed but games still appear:** this is expected stale-cache fallback. Review the last error in settings and Playnite's logs.
- **The cache reflects another country:** change both Region and Language, save, then use Refresh Now. Caches are separated by provider/market/language.
- **A console game is missing:** intentional; the provider imports only products with reliable Windows-PC evidence.
- **A current PC title is missing:** run the diagnostic tool with `--include-rejected` and attach its counts/reason (not a full private Playnite database) to an issue.
- **The endpoint changed:** update the advanced SIGL ID only when a verified replacement is known; do not substitute a console collection.

## Limitations and roadmap

- This plugin represents catalog availability, not proof that a particular account has an active subscription.
- Imported records are uninstalled catalog entries with a Store link; V1 does not install or launch games.
- Removed records remain visible and explicitly labeled rather than being silently deleted; users can hide or delete them normally.
- Exact subscription start/leaving dates are unavailable for most entries.
- EA Play is not enabled because Microsoft's current EA collection mixes PC and console and cannot be reduced reliably to PC using the available metadata.
- Amazon Luna is not enabled because no stable public unauthenticated Standard/Prime/Premium catalog API was found.
- Amazon/Twitch permanently claimed PC games remain the responsibility of Playnite's existing Amazon library plugin.
- This V1 binary targets the current stable Playnite 10.x SDK. Playnite 11's announced .NET/runtime and package-format changes will require a separate migration and validation pass after that release becomes the supported target.

Future providers remain isolated behind `ISubscriptionProvider`. They will only be enabled when a stable, legal, accurately classifiable source exists.

## License

MIT. See [LICENSE](LICENSE).
