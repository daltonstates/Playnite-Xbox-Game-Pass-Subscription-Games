# Subscription Libraries for Playnite

**Browse Game Pass games beside the games you own in Playnite.** Choose a Game Pass plan and a PC or Xbox view, then update your library. The extension adds separate, clearly labeled catalog entries so you can search, filter, and discover games available through that subscription.

These entries represent **regional catalog availability**, not games you own or proof that your Microsoft account can play them. The extension does not sign in to Microsoft, download or track installed games, or change records from your other Playnite libraries.

For example, if you own a game on Steam and it also appears in Game Pass, Playnite can show both entries. If it later leaves Game Pass, only this extension's Game Pass entry changes; your Steam entry stays as it was.

## What it does

- Imports PC and Xbox console Game Pass catalog games as separate, uninstalled entries with a **Game Pass** source, subscription tags, a Microsoft Store link, and available artwork and metadata.
- Lets you choose a plan, PC or Xbox platforms, Xbox One or Series X|S generations, and a region. The default is **PC Game Pass / PC games / United States**.
- Marks games that are leaving soon, have left the catalog, or no longer match your selected view. Newly detected additions can receive a temporary **Recently Added to Game Pass** tag.
- Offers a **Show active Game Pass games** menu filter, a per-game **Check Game Pass availability** action, and a **Refresh and Apply** action that previews library changes.
- Opens an eligible PC game's product page from its Playnite right-click menu. Choose the Xbox app (default) or Microsoft Store once in settings, then choose **Install** in that app.

Game Pass is the only enabled provider. Selecting **All catalogs** is useful for discovery, but it ignores plan membership and does not describe what your own subscription grants.

## Get started

This project is not yet in Playnite's add-on browser. Build a `.pext` package from source using [Build and package from source](#build-and-package-from-source), or use a package you already have.

1. Open the `.pext` file with Playnite, confirm installation, and restart Playnite.
2. Open **Add-ons > Extension settings > Libraries > Subscription Libraries - Game Pass**.
3. Leave Game Pass enabled. Choose your plan, the PC/Xbox games to show, and an Xbox generation if your view includes console games. Choose a region and language if the defaults do not fit you, then save.
4. Run **Update Game Library** and select **Subscription Libraries - Game Pass**.

Your plan and platform choices are independent. **PC Game Pass + Xbox-only** and **legacy Xbox Game Pass for Console + PC-only** produce empty views. The settings page points out these combinations and disables the Xbox generation control when it cannot affect the view.

The extension does not remove entries created by another Xbox or Game Pass plugin. If you use one, you may see duplicate titles until you disable that plugin's updates or manage its records yourself.

## Use it day to day

**Update Game Library** uses your saved settings and normally checks the catalog when its cache expires. The default cache lifetime is 24 hours. Changing only the plan or platform can reuse a fresh catalog; changing the region, language, or advanced catalog IDs requires a new one.

Use **Refresh and Apply...** in the extension settings or the Playnite **Subscription Libraries** menu when you want a fresh catalog now. It previews additions, hiding, and permanent removals before you confirm. When run from the settings dialog, confirming also saves the settings being applied, even if you later cancel that dialog. A normal library update applies your chosen cleanup policy without that preview.

The settings page shows the last checked game count with the plan, platform, region, language, and catalog verification time used to calculate it. After changing settings, refresh or update the library to get a count for the new view.

The **Show active Game Pass games** menu item temporarily filters Playnite to active entries from this extension. Right-click a single game and choose **Check Game Pass availability** for likely matches in your selected view. Title matches are suggestions; the extension does not merge records.

Under **Preferred install app** in extension settings, choose **Xbox app** (default) or **Microsoft Store**. For an active Game Pass entry with PC access, right-click and use the single install-page action, which names your selected app. The app opens the game's product page; you sign in and start the installation there. If that app is unavailable, change the preference. Console-only and unavailable entries do not offer this action. The extension does not start the download, detect when it finishes, or change the catalog entry's installed status. Your installed game may appear separately through Playnite's Xbox integration.

## Understand availability

| What you see | Meaning |
| --- | --- |
| **Access: Subscription** | The last verified catalog places the game in your selected plan and platform view. |
| **Leaving Game Pass** | A relevant public Leaving Soon collection includes it. The collection does not provide a dependable departure date. |
| **Access: Not in selected plan** or **Access: Outside selected platform** | You previously imported the game, but it is outside your current view. This does not mean it left Game Pass. |
| **Access: Excluded free-to-play** | Your optional free-to-play filter excluded a previously imported entry using clear Store pricing evidence. |
| **Access: Removed** | A verified refresh found that a previously cataloged game is no longer in the Game Pass collections. |
| **Access: Catalog unverified** | The extension is showing an older catalog after a failed or overdue verification. Treat availability as historical until refresh succeeds. |

Leaving Soon warnings come from the PC Game Pass feed for PC games and the Ultimate feed for console games. They appear only when that platform is in your selected view; the extension does not infer warnings for other plans or invent leaving dates. A game's first appearance after the initial catalog baseline can be marked as recently added; the first import is not treated as a batch of new releases.

### Choose what happens to unavailable entries

This setting applies only to entries owned by this extension when a game leaves Game Pass or falls outside your selected view:

| Setting | Result |
| --- | --- |
| **Keep and mark unavailable** (default) | Leave the entry visible with its new status. |
| **Hide unavailable entries** | Hide it without deleting your play history or edits; show it again if it becomes eligible. Entries you hid yourself stay hidden. |
| **Remove unplayed entries; hide played/installed entries** | Permanently remove eligible unplayed, uninstalled, idle entries. Hide played, installed, busy, or user-hidden entries instead. Removal loses edits to that Game Pass entry. |

The extension does not delete entries based on an unverified catalog. It also leaves records from Steam, Xbox, and other libraries untouched. If you manually delete one of its entries, Playnite's import exclusion prevents the extension from recreating it automatically.

## What the catalog can and cannot tell you

The extension reads Microsoft's public regional Game Pass collections and Store product metadata. It uses Microsoft product IDs to track entries and requires Windows evidence before labeling a game as available on PC. PC and console editions can have different IDs, so similar titles may appear as separate entries.

It does **not** check your account, subscription payment, family sharing, trials, purchases, or personal entitlements. A catalog listing can include a free-to-play game or a game with plan-specific benefits; it does not prove that payment is required to play it. The optional free-to-play filter excludes only titles with clear zero-price evidence and leaves ambiguous ones visible. The install-page links are a handoff to Microsoft apps, not a Playnite-managed installation or game launch action.

The catalog endpoints are public-facing but are not guaranteed as a third-party API. If an endpoint fails, a previous cache can remain visible with **Catalog unverified** status; that fallback cannot add, hide, restore, or delete entries. The extension also rejects unexpectedly large catalog drops so an incomplete upstream response cannot trigger cleanup. See [architecture](docs/ARCHITECTURE.md) and the dated [research and live diagnostics](docs/RESEARCH.md) for source details and point-in-time counts.

Catalog requests send the selected market, language, public collection IDs, and public product IDs. The extension does not request credentials or store tokens.

## Troubleshooting

- **No games appear:** Confirm Game Pass is enabled, choose a compatible plan and platform, save, then update this extension's library. PC Game Pass does not include an Xbox console catalog.
- **An older entry is still visible:** The default policy keeps unavailable entries with a status tag. Choose **Hide** or **Remove unplayed entries; hide played/installed entries**, save, and update the library if you want cleanup.
- **Catalog unverified or refresh failed:** Check the last error in settings and Playnite's logs. The old catalog remains visible, but library changes wait for a successful verification. Use **Refresh and Apply...** when connectivity returns.
- **A game is missing from your view:** Check the region, plan, platform, and Xbox generation. You can use the [catalog diagnostic tool](#catalog-diagnostic-tool) to inspect current source data.
- **The wrong country's catalog appears:** Set both Region and Language, save, then use **Refresh and Apply...**. Caches are separate by market and language.

## Build and package from source

The plugin targets Playnite 10.x with PlayniteSDK 6.17.0 and .NET Framework 4.6.2. It was validated against Playnite 10.60 at the 2026-09-16 research checkpoint. Building requires Windows and either Visual Studio 2022 with the .NET desktop workload and .NET Framework 4.6.2 targeting pack, or a .NET SDK that can build `net462` and `net8.0`.

From the repository root:

```powershell
dotnet restore .\SubscriptionLibraries.sln
dotnet build .\SubscriptionLibraries.sln -c Debug
dotnet test .\SubscriptionLibraries.Tests\SubscriptionLibraries.Tests.csproj -c Debug
```

The normal tests use local fixtures and do not contact Microsoft. To run the live integration test:

```powershell
$env:SUBSCRIPTIONLIBRARIES_RUN_LIVE_TESTS = '1'
dotnet test .\SubscriptionLibraries.Tests\SubscriptionLibraries.Tests.csproj -c Release --filter Category=Integration
```

To create an installable `.pext`, use PowerShell and Playnite's `Toolbox.exe`:

```powershell
.\build\pack.ps1 -Configuration Release -ToolboxPath 'C:\Path\To\Playnite\Toolbox.exe'
```

The package is written under `artifacts/`. You can omit `-ToolboxPath` if Playnite is installed in a standard location. For development, build Debug, add `SubscriptionLibraries/bin/Debug/net462` in Playnite's **Settings > For developers**, and restart Playnite after rebuilding the plugin.

### Catalog diagnostic tool

The standalone tool inspects the live catalog without Playnite:

```powershell
dotnet run --project .\SubscriptionLibraries.CatalogTool -- --region US --language en-US --plan essential --selection both
```

Run it with `--help` for plan, platform, Xbox generation, rejected-product, JSON output, and advanced catalog ID options. The output includes selected-view counts, platform evidence, and missing metadata; live numbers change over time.

## License

MIT. See [LICENSE](LICENSE).
