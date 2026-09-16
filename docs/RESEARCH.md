# Research record

Research was performed on 2026-09-16 before implementation. Live catalog counts are a point-in-time observation, not a permanent expectation.

## Playnite extension requirements

- The current official plugin tutorial still requires **.NET Framework 4.6.2** for managed plugins.
- The latest stable Playnite package published at the time of verification was **10.60** (2026-09-11).
- The current official Toolbox template is SDK-style, targets `net462`, and references **PlayniteSDK 6.17.0**.
- A library integration derives from `Playnite.SDK.Plugins.LibraryPlugin`. The normal path returns `GameMetadata` records from `GetGames(LibraryGetGamesArgs)`; this extension enables `HasCustomizedGameImport` and implements `ImportGames(LibraryImportGamesArgs)` because Playnite's normal importer does not reconcile departed catalog records or refresh metadata tags.
- Settings use `ISettings`, `GetSettings`, `GetSettingsView`, and Playnite's plugin settings serialization helpers.
- `extension.yaml` is mandatory. A library manifest uses `Type: GameLibrary`, the plugin assembly name in `Module`, and a stable ID.
- Distribution packages are `.pext` files created with `Toolbox.exe pack <extension-folder> <target-folder>`.

Playnite 11 migration documentation already exists, but 10.60 is still the latest stable release at this checkpoint. Playnite 11 changes the runtime and package format, so it will require a deliberate future migration rather than silently shipping an untestable preview-targeted binary in this V1 package.

Primary references:

- [Playnite plugin introduction](https://api.playnite.link/docs/tutorials/extensions/plugins.html)
- [Library plugin documentation](https://api.playnite.link/docs/tutorials/extensions/libraryPlugins.html)
- [Extension manifest documentation](https://api.playnite.link/docs/tutorials/extensions/extensionsManifest.html)
- [Toolbox packaging documentation](https://api.playnite.link/docs/tutorials/toolbox.html)
- [Playnite source and current templates](https://github.com/JosefNemec/Playnite)
- [PlayniteSDK on NuGet](https://www.nuget.org/packages/PlayniteSDK)
- [Playnite 10.60 stable release](https://github.com/JosefNemec/Playnite/releases/tag/10.60)
- [Playnite 11 migration documentation](https://api.playnite.link/docs/wpfdevel11/manual/plugins.html)

## PC Game Pass catalog

The configured PC collection is:

`fdd9e2a7-0fee-49f6-ad69-4354098401ff`

The live collection request is:

`GET https://catalog.gamepass.com/sigls/v2?id={siglId}&language={language}&market={market}`

The response is an array. Its first record describes the collection and subsequent records contain Microsoft product IDs. On 2026-09-16, the US response identified itself as **All PC Games** and returned 528 unique product IDs.

Product metadata is resolved in batches of at most 200 with:

`GET https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={comma-separated IDs}&market={market}&languages={language}`

This exact request shape was verified against live responses and the maintained open-source [NikkelM/Game-Pass-API](https://github.com/NikkelM/Game-Pass-API) implementation. It requires no API key or Microsoft account authentication.

### Windows-PC classification

SIGL membership alone is not used as the only platform check. A product is accepted only when the display catalog also supplies one of these high-confidence signals:

1. a package dependency on `Windows.Desktop`;
2. a legacy PC package dependency on `Windows.Windows8x`; or
3. a non-sentinel, actionable store availability explicitly allowing `Windows.Desktop` (needed for launcher-delivered and package-less entries).

Generic storefront availabilities dated `1753-01-01` are ignored because console products can carry those entries. Products with explicit Xbox-only evidence are rejected; products with no usable evidence are left unclassified and are not imported.

Live Phase 4 diagnostics on 2026-09-16:

| Diagnostic | Count |
|---|---:|
| Total PC catalog IDs | 528 |
| Products resolved | 528 |
| Products identified as Windows PC | 522 |
| Products rejected as non-PC | 6 |
| Products unclassified | 0 |
| IDs without metadata | 0 |

The six rejected records exposed only `Windows.Xbox` package/action evidence. These counts will change as Microsoft rotates the catalog.

## EA Play investigation

Microsoft currently exposes collection `b8900d09-a491-44cc-916e-32b5acae621b`, whose live header says **EA Play Games**. It is not identified as a PC-only collection. The 2026-09-16 US response contained 95 IDs:

- 66 had explicit Xbox-only evidence;
- 29 passed the same basic desktop signal test, but that set included obvious Xbox-specific SKUs such as names ending in `Xbox One` and `Xbox Series X|S`.

The display catalog's storefront availability data is therefore not strong enough to derive a reliable EA Play **PC** subset from this mixed collection. No `EAPlayProvider` is shipped. Adding one would create false subscription records, which is worse than leaving the provider unavailable.

## Amazon Luna investigation

Amazon's current public pages and Luna terms confirm that Prime/Luna Standard provides a rotating subscription catalog and that Luna Premium has an expanded catalog. No stable, documented, unauthenticated catalog API was found that distinguishes the current Standard/Prime and Premium entitlements by region.

The interactive Luna experience is personalized and may require Amazon session state. Reusing private browser tokens or scraping its rendered HTML would violate this project's reliability and privacy constraints. No Luna provider is shipped until Amazon offers a suitable structured public source.

Relevant official references:

- [Games at Amazon](https://games.amazon.com/en-us)
- [Amazon Luna terms](https://digprjsurvey.amazon.com/csad/help/node/G5FYRVVJK7KFGQQN)
