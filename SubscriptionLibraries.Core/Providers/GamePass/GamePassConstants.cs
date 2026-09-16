using System;
using System.Collections.Generic;
using SubscriptionLibraries.Core.Models;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public static class GamePassConstants
{
    public const string ProviderId = "game-pass";
    public const string ProviderName = "Game Pass";
    public const string SubscriptionTier = "Game Pass catalog";

    // Microsoft's collection identifiers are isolated here because the public-facing
    // catalog interfaces are not a versioned, supported developer API.
    public const string PcCatalogSiglId = "fdd9e2a7-0fee-49f6-ad69-4354098401ff";
    public const string ConsoleCatalogSiglId = "f6f1f99f-9b49-4ccd-b3bf-4d9767a77f5e";

    public static readonly Uri CatalogEndpoint = new("https://catalog.gamepass.com/sigls/v2");

    // Xbox's current browse page uses SIGL v3 with both a platform and a plan
    // context. The GUIDs are collection IDs; the CFQ IDs are subscription
    // product IDs used here only as public catalog query context.
    public static readonly Uri PlanCatalogEndpoint = new("https://catalog.gamepass.com/sigls/v3");

    // Xbox's public browse page currently uses these separate collections.
    // They are optional enrichment: an unavailable feed must not erase access.
    public static readonly GamePassPlanCatalog LeavingSoonPcCatalog = new(
        GamePassPlanSelection.PcGamePass, SubscriptionPlatforms.WindowsPc,
        "cc7fc951-d00f-410e-9e02-5e4628e04163", "pc", "cfq7ttc0kgq8");
    public static readonly GamePassPlanCatalog LeavingSoonConsoleCatalog = new(
        GamePassPlanSelection.Ultimate, SubscriptionPlatforms.XboxConsole,
        "393f05bf-e596-4ef6-9487-6d4fa0eab987",
        "ConsoleGen8;ConsoleGen9", "cfq7ttc0khs0");

    public static readonly IReadOnlyList<GamePassPlanCatalog> PlanCatalogs = new[]
    {
        new GamePassPlanCatalog(GamePassPlanSelection.PcGamePass,
            SubscriptionPlatforms.WindowsPc, "609d944c-d395-4c0a-9ea4-e9f39b52c1ad",
            "pc", "cfq7ttc0kgq8"),
        new GamePassPlanCatalog(GamePassPlanSelection.XboxGamePassConsole,
            SubscriptionPlatforms.XboxConsole, ConsoleCatalogSiglId,
            "ConsoleGen8", "cfq7ttc0k6l8"),
        new GamePassPlanCatalog(GamePassPlanSelection.XboxGamePassConsole,
            SubscriptionPlatforms.XboxConsole, ConsoleCatalogSiglId,
            "ConsoleGen9", "cfq7ttc0k6l8"),
        new GamePassPlanCatalog(GamePassPlanSelection.Essential,
            SubscriptionPlatforms.WindowsPc, "34031711-5a70-4196-bab7-45757dc2294e",
            "pc", "cfq7ttc0k5dj"),
        new GamePassPlanCatalog(GamePassPlanSelection.Essential,
            SubscriptionPlatforms.XboxConsole, "34031711-5a70-4196-bab7-45757dc2294e",
            "ConsoleGen8", "cfq7ttc0k5dj"),
        new GamePassPlanCatalog(GamePassPlanSelection.Essential,
            SubscriptionPlatforms.XboxConsole, "34031711-5a70-4196-bab7-45757dc2294e",
            "ConsoleGen9", "cfq7ttc0k5dj"),
        new GamePassPlanCatalog(GamePassPlanSelection.Premium,
            SubscriptionPlatforms.WindowsPc, "09a72c0d-c466-426a-9580-b78955d8173a",
            "pc", "cfq7ttc0p85b"),
        new GamePassPlanCatalog(GamePassPlanSelection.Premium,
            SubscriptionPlatforms.XboxConsole, "09a72c0d-c466-426a-9580-b78955d8173a",
            "ConsoleGen8", "cfq7ttc0p85b"),
        new GamePassPlanCatalog(GamePassPlanSelection.Premium,
            SubscriptionPlatforms.XboxConsole, "09a72c0d-c466-426a-9580-b78955d8173a",
            "ConsoleGen9", "cfq7ttc0p85b"),
        new GamePassPlanCatalog(GamePassPlanSelection.Ultimate,
            SubscriptionPlatforms.WindowsPc, "97c6c862-d28a-4907-a3d5-c401f2296a53",
            "pc", "cfq7ttc0khs0"),
        new GamePassPlanCatalog(GamePassPlanSelection.Ultimate,
            SubscriptionPlatforms.XboxConsole, "97c6c862-d28a-4907-a3d5-c401f2296a53",
            "ConsoleGen8", "cfq7ttc0khs0"),
        new GamePassPlanCatalog(GamePassPlanSelection.Ultimate,
            SubscriptionPlatforms.XboxConsole, "97c6c862-d28a-4907-a3d5-c401f2296a53",
            "ConsoleGen9", "cfq7ttc0khs0")
    };

    public static readonly Uri DisplayCatalogEndpoint =
        new("https://displaycatalog.mp.microsoft.com/v7.0/products");

    public const int ProductBatchSize = 200;
    public const string WindowsDesktopPlatform = "Windows.Desktop";
    public const string LegacyWindowsDesktopPlatform = "Windows.Windows8x";
    public const string XboxPlatform = "Windows.Xbox";
}
