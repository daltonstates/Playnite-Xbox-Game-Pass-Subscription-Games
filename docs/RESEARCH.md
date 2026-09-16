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

## PC and Xbox console Game Pass catalogs

The configured PC and console collections are:

- PC: `fdd9e2a7-0fee-49f6-ad69-4354098401ff`
- Xbox console: `f6f1f99f-9b49-4ccd-b3bf-4d9767a77f5e`

The live collection request is:

`GET https://catalog.gamepass.com/sigls/v2?id={siglId}&language={language}&market={market}`

The response is an array. Its first record describes the collection and subsequent records contain Microsoft product IDs. On 2026-09-16, live US responses identified themselves as **All PC Games** and **All Console Games**, with 519 and 623 unique IDs respectively. The console identifier is also used in the maintained [NikkelM/Game-Pass-API integration tests](https://github.com/NikkelM/Game-Pass-API/blob/main/test/api.test.mjs).

### Plan-specific collections

Microsoft's live [Xbox Game Pass browse page](https://www.xbox.com/en-US/xbox-game-pass/games) exposes independent **Subscriptions** (PC Game Pass, legacy Console, Essential, Premium, Ultimate) and **Play on** filters. Its [catalog population script](https://www.xbox.com/en-us/xbox-game-pass/games/js/xgpcatPopulate-2025.js), inspected on 2026-09-16, requests:

`GET https://catalog.gamepass.com/sigls/v3?id={collectionId}&language={language}&market={market}&platformContext={pc|ConsoleGen8|ConsoleGen9}&subscriptionContext={subscriptionProductId}`

The script supplies distinct collection IDs for Ultimate (`97c6c862-d28a-4907-a3d5-c401f2296a53`), Premium (`09a72c0d-c466-426a-9580-b78955d8173a`), and Essential (`34031711-5a70-4196-bab7-45757dc2294e`). For PC Game Pass it uses PC collection `609d944c-d395-4c0a-9ea4-e9f39b52c1ad`; for retired Console it uses console collection `f6f1f99f-9b49-4ccd-b3bf-4d9767a77f5e`. The `subscriptionContext` values are Microsoft's subscription **product IDs**, corroborated by the [Microsoft Game Development Kit tier table](https://learn.microsoft.com/en-us/xbox/gdk/docs/store/commerce/service-to-service/xstore-detecting-game-pass). Using them as public catalog context does not query a user's entitlement; Microsoft's `publisherQuery` entitlement API requires authorization and is not used.

We call nine plan/platform/generation combinations: PC Game Pass on PC; legacy Console separately for Xbox One and Series X|S; and Essential/Premium/Ultimate on PC, Xbox One, and Series X|S. The union of each plan's two console-generation lists defines its console membership. We do not infer PC access from console membership, or Ultimate membership from a lower plan's membership. The unsupported combinations (PC Game Pass on Xbox and legacy Console on PC) returned a header but zero products in the live US response. Empty plan lists are allowed, but all nine empty is rejected as an upstream failure.

The same Xbox browse script uses `cc7fc951-d00f-410e-9e02-5e4628e04163` for PC Leaving Soon (`platformContext=pc`, PC Game Pass context) and `393f05bf-e596-4ef6-9487-6d4fa0eab987` for console Leaving Soon (`platformContext=ConsoleGen8;ConsoleGen9`, Ultimate context). Both feeds returned their expected headers and **zero IDs** in the live US response on 2026-09-16. Empty is not an error. A failure of either optional feed leaves prior Leaving Soon warnings in place; it does not fail the main catalog or manufacture dates.

Live US plan results on 2026-09-16, after Windows evidence checks:

| Plan | PC IDs | Xbox IDs |
|---|---:|---:|
| PC Game Pass | 558 | 0 |
| Legacy Xbox Game Pass for Console | 0 | 501 |
| Essential | 86 | 93 |
| Premium | 412 | 436 |
| Ultimate | 600 | 623 |

The Ultimate console-generation responses contained 430 Xbox One and 622 Series X|S IDs before union. A product can be in both. Generation-specific views only include a console game when the selected plan's generation collection actually contains its ID; unknown-generation entries fail closed for that view.

These are product IDs, not unique title counts. Some PC and console editions have different IDs; some products appear in both plan/platform lists. Tier lists can change or vary by region. Microsoft does not document SIGL v3 as a supported third-party interface, so the IDs and semantics are isolated and tested live before release updates.

Product metadata is resolved in batches of at most 200 with:

`GET https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={comma-separated IDs}&market={market}&languages={language}`

This exact request shape was verified against live responses and the maintained open-source [NikkelM/Game-Pass-API](https://github.com/NikkelM/Game-Pass-API) implementation. It requires no API key or Microsoft account authentication.

### Windows-PC classification

SIGL membership alone is not used as the only platform check. A product is accepted only when the display catalog also supplies one of these high-confidence signals:

1. a package dependency on `Windows.Desktop`;
2. a legacy PC package dependency on `Windows.Windows8x`; or
3. a non-sentinel, actionable store availability explicitly allowing `Windows.Desktop` (needed for launcher-delivered and package-less entries).

Generic storefront availabilities dated `1753-01-01` are ignored because console products can carry those entries. Products with explicit Xbox-only evidence are not labeled PC-accessible; products with no usable Windows evidence are left unclassified for PC access. If a product is also in the console collection, it remains importable as an Xbox console game. Conversely, a console-only collection member with `Windows.Desktop` storefront metadata is **not** labeled PC Game Pass without PC collection membership. Cloud availability is not inferred or imported as a separate platform.

### Conservative free-to-play evidence

The live Display Catalog responses for `9PDC9X7BKZR3` (Albion Online) and `9NG07QJNK38J` (Among Us) were inspected on 2026-09-16. Both contain zero-price license or secondary purchase offers, so testing for *any* zero price would wrongly classify paid Among Us as free. The optional exclusion only marks a product confirmed free-to-play when the standard public Store SKU (`SkuId=0010`) has an actionable `Purchase` + `Fulfill` availability with both `OrderManagementData.Price.ListPrice` and `MSRP` at zero and no qualifying paid offer. A promotion with a zero list price but positive MSRP is not enough. Missing or ambiguous pricing is kept in view. Perks and trials lack equally trustworthy catalog classification and are not automatically excluded.

Live combined diagnostics on 2026-09-16 (v2 broad lists plus v3 plan lists):

| Diagnostic | Count |
|---|---:|
| PC catalog IDs | 606 |
| Xbox console catalog IDs | 623 |
| Distinct IDs across both catalogs | 905 |
| Products resolved | 905 |
| Products identified as Windows PC | 600 |
| Products identified as Xbox console | 623 |
| Products sharing one ID in both | 318 |
| PC-catalog entries without Windows evidence | 6 |
| Products unclassified | 0 |
| IDs without metadata | 0 |

The six PC entries without Windows evidence exposed only `Windows.Xbox` package/action evidence and are represented as console access where console membership exists. The overlap counts compare **product IDs**, not titles. Separate PC and console editions with distinct product IDs are not merged by title; for example, the live catalog contains separate product IDs for `A Plague Tale: Requiem` and `A Plague Tale: Requiem - Windows`. These counts will change as Microsoft rotates the catalog.

The public SIGL collections indicate plan catalog inclusion, not an individual account's active entitlement. The selected plan is user-declared. No credentials, personal account data, or unauthorized entitlement query is used.

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
