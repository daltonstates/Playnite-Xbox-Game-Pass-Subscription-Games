# Architecture

## Project boundaries

- `SubscriptionLibraries.Core` contains normalized models, provider contracts, Microsoft clients, filtering, matching, retries, caching, and synchronization. It has no Playnite dependency and targets both `net462` and `net8.0`.
- `SubscriptionLibraries` is the thin Playnite `LibraryPlugin`. It maps normalized games into Playnite metadata and owns the settings UI.
- Its Playnite library name is **Subscription Libraries - Game Pass**, distinct from the older third-party **Game Pass** library plugin; imported records use the simpler **Game Pass** source/tag label.
- `SubscriptionLibraries.CatalogTool` exercises the live catalog independently from Playnite.
- `SubscriptionLibraries.Tests` validates the core with sanitized fixtures. Its live test is opt-in.

The `net462` core compiles against the Newtonsoft.Json 10.0.3 assembly hosted by Playnite 10.x; Playnite's official packer intentionally excludes private copies of that host assembly. Standalone `net8.0` tooling/tests use Newtonsoft.Json 13.0.4. All external and cache JSON reads enforce a depth limit of 64 before materialization, including on the host version.

## Data flow

1. `GamePassCatalogClient` fetches and validates the configured broad PC/Xbox SIGL v2 collections plus nine plan/platform/generation-specific SIGL v3 collections and two optional Leaving Soon feeds. Stable Microsoft product IDs are deduplicated across all collections; each ID retains its per-plan, per-platform and console-generation memberships.
2. `MicrosoftStoreCatalogClient` resolves the union of those IDs in bounded batches.
3. `GamePassPlatformClassifier` requires affirmative Windows-PC evidence before labeling PC access. Xbox console access comes from the console collection's membership, not an inferred console hardware flag.
4. `GamePassProvider` normalizes accepted products into `SubscriptionGame` records keyed by Microsoft product ID, with broad PC/Xbox flags, a plan-to-platform map, and a plan-to-console-generation map. A selected plan projects the game into only the platforms and generations supported through that plan. A zero MSRP on the standard public Store purchase offer is the only free-to-play exclusion evidence.
5. `SubscriptionSyncService` applies cache policy, preserves detected `DateAdded` values and Leaving Soon transitions, records departed games as `Removed` history, and falls back to the last structurally valid cache after a network failure. Full source membership is tracked separately from normalized games, so a still-listed product with temporarily incomplete metadata retains its last verified active record.
6. `PlayniteGameMapper` creates separate, uninstalled `GameMetadata` records owned only by this plugin. Managed tags express plan, platform/generation, leaving-soon and verification state. A managed link records the last catalog verification time without creating date-specific tags.
7. `PlayniteLibraryReconciler` imports records in the selected plan/platform view and updates managed access-status tags and managed PC/Xbox platforms only on records whose `PluginId` equals this extension's ID. Previously imported records outside the selection receive `Access: Not in selected plan` or `Access: Outside selected platform`, not a false removal status. An opt-in policy can hide unavailable entries, or remove unplayed/uninstalled/idle ones and hide the rest. A managed marker distinguishes plugin-hidden from user-hidden entries, allowing restoration when access returns without revealing games the user hid. It also migrates plugin-owned records still using the 1.0 default source name from **PC Game Pass** to **Game Pass**, without changing user-customized sources.

The plugin never edits, merges, or removes records owned by Steam, Epic, GOG, Amazon, Xbox, or another integration. It also respects Playnite's import-exclusion list so a user-deleted subscription record is not recreated automatically. When a catalog is stale or served as a network-failure fallback, reconciliation only marks its own entries as unverified; it does not import, hide, restore, or delete anything.

## Matching

`GameMatchingService` reports likely relationships in this order:

1. stable source/cross-store identifiers;
2. metadata identifiers;
3. exact normalized title;
4. conservative fuzzy title.

Only identifier matches are marked safe as relationships. The plugin performs no automatic merges, including for exact or fuzzy title matches.

## Cache and removal history

Each provider/market/language combination has a separate JSON envelope in Playnite's plugin data directory. It contains the configured catalog ID identity, fetch timestamp, product IDs, normalized active games with all plan/platform/generation memberships, removed-game history, and diagnostics. Changing a broad PC or console SIGL ID invalidates the matching envelope. Envelopes written before catalog ID identity was added also require one new fetch. The v1.3 schema upgrade invalidates older envelopes for one full refresh. Switching plan, platform, or generation then reuses the same cache.

Writes use a same-directory temporary file followed by an atomic replace. An expired cache triggers a refresh, but remains eligible as a failure fallback. A live result that suddenly drops below half of a prior catalog of at least 20 games is rejected and the older cache is retained. The same guard checks each previously populated plan/platform and console-generation membership; a drop to zero from at least five games is also rejected. A rejected result falls back to the previous cache without applying library cleanup.

Removed records remain in cache history with `SubscriptionAvailability.Removed` and a detected leaving timestamp. Their Playnite entries remain as historical subscription records by default, but the reconciler replaces the active access tag with `Access: Removed` and `Left Game Pass`. Reappearing products are reactivated. Changing plan or platform never counts as a catalog departure. The extension replaces only its known status tags and managed PC/Xbox platform IDs; unrelated tags, platforms, and other metadata remain untouched unless the user explicitly selects destructive cleanup. Cleanup is never performed on a stale cache fallback and never crosses the plugin ownership boundary.
