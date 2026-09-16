# Architecture

## Project boundaries

- `SubscriptionLibraries.Core` contains normalized models, provider contracts, Microsoft clients, filtering, matching, retries, caching, and synchronization. It has no Playnite dependency and targets both `net462` and `net8.0`.
- `SubscriptionLibraries` is the thin Playnite `LibraryPlugin`. It maps normalized games into Playnite metadata and owns the settings UI.
- `SubscriptionLibraries.CatalogTool` exercises the live catalog independently from Playnite.
- `SubscriptionLibraries.Tests` validates the core with sanitized fixtures. Its live test is opt-in.

The `net462` core compiles against the Newtonsoft.Json 10.0.3 assembly hosted by Playnite 10.x; Playnite's official packer intentionally excludes private copies of that host assembly. Standalone `net8.0` tooling/tests use Newtonsoft.Json 13.0.4. All external and cache JSON reads enforce a depth limit of 64 before materialization, including on the host version.

## Data flow

1. `GamePassCatalogClient` fetches and validates the configured PC SIGL, then deduplicates stable Microsoft product IDs.
2. `MicrosoftStoreCatalogClient` resolves those IDs in bounded batches.
3. `GamePassPlatformClassifier` rejects products without affirmative Windows-PC evidence.
4. `GamePassProvider` normalizes accepted products into `SubscriptionGame` records keyed by Microsoft product ID.
5. `SubscriptionSyncService` applies cache policy, preserves detected `DateAdded` values, records departed games as `Removed` history, and falls back to the last structurally valid cache after a network failure. Full source membership is tracked separately from normalized games, so a still-listed product with temporarily incomplete metadata retains its last verified active record.
6. `PlayniteGameMapper` creates separate, uninstalled `GameMetadata` records owned only by this plugin.
7. `PlayniteLibraryReconciler` imports missing records and updates only managed access-status tags on records whose `PluginId` equals this extension's ID.

The plugin never edits, merges, or removes records owned by Steam, Epic, GOG, Amazon, Xbox, or another integration. It also respects Playnite's import-exclusion list so a user-deleted subscription record is not recreated automatically.

## Matching

`GameMatchingService` reports likely relationships in this order:

1. stable source/cross-store identifiers;
2. metadata identifiers;
3. exact normalized title;
4. conservative fuzzy title.

Only identifier matches are marked safe as relationships. The plugin performs no automatic merges, including for exact or fuzzy title matches.

## Cache and removal history

Each provider/market/language combination has a separate JSON envelope in Playnite's plugin data directory. It contains the fetch timestamp, product IDs, normalized active games, removed-game history, and diagnostics.

Writes use a same-directory temporary file followed by an atomic replace. An expired cache triggers a refresh, but remains eligible as a failure fallback. A live result that suddenly drops below half of a prior catalog of at least 20 games is rejected and the older cache is retained.

Removed records remain in cache history with `SubscriptionAvailability.Removed` and a detected leaving timestamp. Their Playnite entries remain as historical subscription records, but the reconciler replaces the active access tag with `Access: Removed` and `Left PC Game Pass`. Reappearing products are reactivated. Only the extension's known status tags are replaced; unrelated tags and metadata remain untouched.
