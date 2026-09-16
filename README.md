# Subscription Libraries for Playnite

Subscription Libraries is a Playnite library extension that makes games available through temporary gaming subscriptions searchable beside games you own. Version 1.3.0 imports **PC and Xbox console Game Pass** games with independent subscription-plan, platform, and console-generation filters, as separate, clearly labeled, uninstalled Playnite entries. It can also hide or clean up entries that are no longer in your selected view.

It never merges with or modifies Steam, Epic, GOG, Amazon, Xbox, or other library records. If you own a game on Steam and can also access it through Game Pass, both records can coexist.

> **Screenshot placeholder:** settings page and an example search showing owned and Game Pass records side by side will be added before an add-on database release.

## Current status

- Production provider: Game Pass, with independent plan (PC Game Pass, Essential, Premium, Ultimate, legacy Console, or all catalogs), platform (PC, Xbox console, or both), and Xbox generation (Xbox One, Series X|S, or both) selections
- QOL: last-verified status, one-click refresh/apply with a change preview, active-only quick filter, leaving-soon alerts, conservative free-to-play exclusion, and a per-game availability check
- Default market/language: United States (`US`), English (`en-US`)
- Authentication: none
- Cache lifetime: 24 hours
- Playnite SDK: 6.17.0
- Plugin target: .NET Framework 4.6.2

The live US validation on 2026-09-16 retrieved 905 distinct products across the broad and plan-specific catalogs. All 905 resolved: 600 had verified PC access, 623 had console-catalog access, and 318 shared one product ID across PC and console. Six broad PC-catalog entries lacked Windows evidence, so they are not labeled PC-accessible. The plan-specific lists returned 558 PC Game Pass PC games; Essential 86 PC/93 Xbox; Premium 412 PC/436 Xbox; Ultimate 600 PC/623 Xbox; and legacy Console 501 Xbox. The Ultimate generation-specific collections returned 430 Xbox One and 622 Series X|S IDs. Both Leaving Soon feeds were valid and empty at that check. Counts are expected to change.

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
3. Open **Add-ons > Extension settings > Libraries > Subscription Libraries - Game Pass**.
4. Leave Game Pass enabled. Choose your subscription plan, then independently choose **PC games only**, **Xbox console games only**, or **PC and Xbox console games**. If including Xbox, choose Xbox One, Series X|S, or both. Choose what to do with unavailable entries, then choose a market/language and save. PC Game Pass plus PC-only is the default; choosing PC Game Pass plus Xbox-only correctly yields no games.
5. Run **Update Game Library** and select the **Subscription Libraries - Game Pass** library.

This project is not yet listed in Playnite's add-on browser. Install the `.pext` locally; publication to the Playnite add-on database is intentionally on hold.

If you use another Xbox/Game Pass library plugin, this extension now appears under its distinct **Subscription Libraries - Game Pass** name in Playnite's library list. Disable the other plugin's library updates after confirming this one works. This extension does not delete or modify that other plugin's existing records; Playnite may show duplicates until you remove or hide them yourself.

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
dotnet run --project .\SubscriptionLibraries.CatalogTool -- --region US --language en-US --plan essential --selection both
```

Optional arguments include `--plan pc|console|essential|premium|ultimate|all`, `--selection pc|xbox|both`, `--xbox-generation both|one|series`, `--include-rejected`, `--json <path>`, `--sigl-id <guid>` for the broad PC collection, and `--console-sigl-id <guid>` for the broad console collection. The output reports plan/platform counts, overlap, verified PC products, generation status, leaving-soon count, missing metadata, and samples.

## Release package

Build, stage only the required runtime files, and invoke Playnite's official packer:

```powershell
.\build\pack.ps1 -Configuration Release -ToolboxPath 'C:\Path\To\Playnite\Toolbox.exe'
```

The script writes the extension staging directory and `.pext` package under `artifacts/`. If Playnite is installed in a standard location, `-ToolboxPath` can be omitted. `Playnite.SDK.dll` and the Playnite-hosted JSON assembly are deliberately not included because the host supplies them.

## How synchronization works

1. The provider requests the configured broad **All PC Games** and **All Console Games** collections from `catalog.gamepass.com/sigls/v2`. It also requests Microsoft's plan-specific collections from `sigls/v3` with the plan and PC/console-generation context currently used by the Xbox Game Pass browse page. Separate PC and console **Leaving Soon** collections supply warnings, but no fabricated dates.
2. Stable Microsoft product IDs are deduplicated across all collections. Plan membership is stored separately for PC and Xbox; selecting a plan and platform is a view over this full catalog, so switching views does not imply that a game left Game Pass.
3. Product metadata is resolved from `displaycatalog.mp.microsoft.com/v7.0/products` in batches of at most 200.
4. PC access also requires affirmative Windows PC metadata. Console access is determined by console-collection membership; Windows-capable hardware metadata alone never grants a PC Game Pass label. Product records without a title/ID cannot be imported. Plan-specific lists can be empty in markets where a plan is unavailable; an unrecognized plan membership is never treated as entitlement.
5. Accepted products become Playnite entries with:
   - Library/source: **Game Pass**
   - Platform: **PC (Windows)**, **Xbox One**, and/or **Xbox Series X|S** when a generation-specific collection supplies evidence; generic **Xbox console** only where that detail is unavailable
   - Tags: **Subscription: Game Pass**, **Access: Subscription**, the selected **Game Pass plan: ...** (unless browsing all), and exactly one of **Game Pass: PC only**, **Game Pass: Xbox only**, or **Game Pass: PC + Xbox**, evaluated within the selected plan
   - Microsoft Store link, last-catalog-verification link, cover/background art, description, companies, and release date when supplied
6. A plugin-owned reconciliation pass adds new records and refreshes the extension's status tags and managed PC/Xbox platform fields on existing records. User-added tags and unrelated platforms are preserved.
7. Results are cached in Playnite's plugin data directory.

The stable Microsoft product ID is the library `GameId`; titles are never primary identifiers. Some games have distinct Windows and console product IDs or editions, so they intentionally appear as separate records rather than being merged by similar title.

## Temporary access and duplicates

Subscription access is modeled as `Active`, `LeavingSoon`, or `Removed`. The public **Leaving Soon** collections set a **Leaving Game Pass** tag and can trigger a Playnite notification when a game newly enters them. These feeds are scoped to PC Game Pass on PC and Ultimate on console; other plan/platform combinations are not labeled leaving based on an unrelated feed. Microsoft does not expose dependable leaving dates through those collections, so the extension does not invent dates. An empty collection is valid.

On a successful refresh, a previously cached game that disappears from **both** Game Pass collections is retained in cache history as `Removed` with the detection timestamp. Its plugin-owned Playnite record is retained for auditability, loses the **Access: Subscription** tag, and receives **Access: Removed** plus **Left Game Pass**. If the title returns, those managed tags switch back to active. Unrelated user tags are preserved, manually deleted records remain on Playnite's import-exclusion list, and records from other plugins are never changed.

Changing the plan or PC/Xbox selection does **not** mean a game left Game Pass. Newly discovered out-of-selection products are not imported. Previously imported entries get **Access: Not in selected plan** or **Access: Outside selected platform**, while games no longer in any catalog get **Access: Removed**. The **Unavailable entries** setting then controls what happens to these plugin-owned entries:

- **Keep and mark unavailable** (default): retain them visibly with the non-active status.
- **Hide unavailable entries**: hide them from the normal Playnite view without deleting play history or edits; automatically show them again when they become eligible. Entries the user had already hidden before cleanup stay hidden.
- **Remove unplayed entries; hide played/installed entries**: permanently delete only this extension's unplayed, uninstalled, idle entries outside the selected view. Played, installed, busy, or user-hidden entries are hidden instead. Deletion loses any edits on that Playnite record, but does not add an import exclusion, so the game can be imported again if it later becomes eligible. Records from other library plugins are never touched.

The cleanup mode never deletes based on a stale fallback catalog. A failed deletion falls back to hiding and marking the entry unavailable. **Refresh and Apply...** gets a live catalog, previews changes (including permanent removals), then applies only after confirmation. A normal Playnite library update also applies the configured policy. Playnite intentionally does not remove library entries by default, so cleanup remains opt-in; see [Playnite's explanation](https://github.com/JosefNemec/Playnite/issues/2677).

New additions detected after the first successful baseline receive a detected `DateAdded`; recent entries can receive a temporary **Recently Added to Game Pass** tag. The first catalog is not falsely labeled as newly added.

Matching support reports likely relationships by stable IDs, metadata IDs, exact normalized title, then conservative fuzzy title. It never automatically merges title-only matches.

The Playnite desktop **Subscription Libraries** menu includes **Show active Game Pass games**, a temporary filter for this plugin's entries carrying **Access: Subscription**. Right-click any single game and choose **Check Game Pass availability** to see conservative candidate matches in the selected plan/platform. This does not merge entries or verify your personal subscription.

## Cache and failure behavior

- Default freshness: 24 hours, configurable from 1 to 720 hours.
- A fresh matching market/language cache contains all plan/platform/generation memberships and avoids network traffic when switching views. Changing either Advanced catalog SIGL ID invalidates that cache and requires a new fetch. Caches written before catalog ID identity was introduced also need one new fetch, as do pre-1.3 caches with an older schema.
- Disabling **Refresh during Playnite library update** uses the last cache; an initial fetch still occurs if no cache exists.
- **Refresh and Apply...** forces a network refresh, shows a change preview, and applies the selected view after confirmation.
- If Microsoft fails after a valid cache exists, the plugin logs a warning and returns the cached catalog, even when stale. A stale/fallback catalog marks this plugin's entries **Access: Catalog unverified** and cannot add, hide, restore, or delete them until verification succeeds.
- If an ID remains in a selected collection but its current metadata is missing or unclassifiable, the last verified active record is preserved instead of falsely declaring that the game left the service.
- Cache writes are atomic, and malformed cache files are ignored.
- A suspicious live drop below half of a prior sizable catalog, plan/platform membership, or console-generation membership is rejected to prevent a transient upstream issue from wiping the library view. A previously populated membership that suddenly becomes empty is also rejected when it contained at least five games.

## Data sources and reliability

The Microsoft catalog endpoints are public-facing interfaces used by Microsoft experiences and community implementations, but Microsoft does not document them as a guaranteed third-party API. Their schemas, identifiers, or availability may change without notice. Endpoint definitions, plan collection IDs, and subscription context IDs are isolated in `GamePassConstants`; the broad PC/console SIGL IDs are also available under Advanced settings for recovery. The plan-specific IDs require a code update if Microsoft changes them.

See [docs/RESEARCH.md](docs/RESEARCH.md) for exact request formats, platform evidence, current diagnostics, and source references.

## Privacy and security

V1 sends only the configured market, language, public collection ID, and public Microsoft product IDs. It does not access personal Xbox data, request credentials, persist tokens, bypass authentication, or run a hosted service. Logs omit secrets and full request queries.

## Troubleshooting

- **No games appear:** confirm Game Pass is enabled, select your actual plan and a compatible platform (PC Game Pass does not grant Xbox-console access), save settings, then run Update Game Library with this plugin's library selected.
- **Refresh failed but games still appear:** this is expected stale-cache fallback. Review the last error in settings and Playnite's logs.
- **The cache reflects another country:** change both Region and Language, save, then use Refresh and Apply. Caches are separated by provider/market/language.
- **A console game is missing:** select Xbox console or both and the correct generation in extension settings, then update the library. If it is still missing, inspect the live diagnostic tool; console availability varies by market and plan.
- **An old subscription entry still appears:** choose **Hide** or **Remove unplayed; hide the rest** under **Unavailable entries**, save, and update this extension's library. Played or installed records are deliberately preserved rather than deleted.
- **A current PC title is missing:** run the diagnostic tool with `--include-rejected` and attach its counts/reason (not a full private Playnite database) to an issue.
- **The endpoint changed:** verify it against Microsoft's browse page and the live diagnostic tool. Advanced settings can update the broad PC/console SIGL IDs; plan-specific v3 IDs are isolated in `GamePassConstants` for a code update.
- **Access shows Catalog unverified:** the last attempted refresh failed or automatic refresh is disabled and the cache expired. The displayed catalog is historical, not a current availability guarantee. Use Refresh and Apply when connectivity returns.

## Limitations and roadmap

- This plugin represents regional plan catalog availability, not proof that a particular account has an active subscription. It does not read your account entitlements, family sharing, trials, or purchase history. Selecting a plan is a user-declared filter, not account verification.
- Microsoft's plan collections can include free-to-play titles or titles with plan-specific benefits; a catalog listing does not by itself prove that payment is required to play that title. The optional free-to-play exclusion only acts when the standard public Store purchase SKU reports zero list price and zero MSRP; ambiguous or missing price data is left in view. Perks/trials are not automatically excluded because the source does not reliably classify them.
- Imported records are uninstalled catalog entries with a Store link; V1 does not install or launch games.
- Removed and previously imported out-of-selection records remain visible by default and explicitly labeled; opt-in settings can hide them or remove unplayed records. Hiding preserves history, while removal does not.
- Existing 1.0-1.2 plugin records are kept because the extension ID and Microsoft product IDs are stable; their managed tags update on the next library import. Plugin-owned records still using the old default **PC Game Pass** source are relabeled **Game Pass**; custom sources are preserved. A new full-catalog cache is fetched after upgrading to 1.3, so the first 1.3 sync needs network access.
- Exact subscription start/leaving dates are unavailable for most entries.
- EA Play is not enabled because Microsoft's current EA collection mixes PC and console and cannot be reduced reliably to PC using the available metadata.
- Amazon Luna is not enabled because no stable public unauthenticated Standard/Prime/Premium catalog API was found.
- Amazon/Twitch permanently claimed PC games remain the responsibility of Playnite's existing Amazon library plugin.
- This V1 binary targets the current stable Playnite 10.x SDK. Playnite 11's announced .NET/runtime and package-format changes will require a separate migration and validation pass after that release becomes the supported target.

Future providers remain isolated behind `ISubscriptionProvider`. They will only be enabled when a stable, legal, accurately classifiable source exists.

## License

MIT. See [LICENSE](LICENSE).
