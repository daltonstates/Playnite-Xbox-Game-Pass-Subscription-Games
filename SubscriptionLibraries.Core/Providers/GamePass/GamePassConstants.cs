using System;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public static class GamePassConstants
{
    public const string ProviderId = "pc-game-pass";
    public const string ProviderName = "PC Game Pass";
    public const string SubscriptionTier = "PC Game Pass";

    // Microsoft's collection identifiers are isolated here because the public-facing
    // catalog interfaces are not a versioned, supported developer API.
    public const string PcCatalogSiglId = "fdd9e2a7-0fee-49f6-ad69-4354098401ff";

    public static readonly Uri CatalogEndpoint = new("https://catalog.gamepass.com/sigls/v2");

    public static readonly Uri DisplayCatalogEndpoint =
        new("https://displaycatalog.mp.microsoft.com/v7.0/products");

    public const int ProductBatchSize = 200;
    public const string WindowsDesktopPlatform = "Windows.Desktop";
    public const string LegacyWindowsDesktopPlatform = "Windows.Windows8x";
    public const string XboxPlatform = "Windows.Xbox";
}
