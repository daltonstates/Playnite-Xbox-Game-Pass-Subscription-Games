using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Models;
using Newtonsoft.Json;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class GamePassProviderTests
{
    [Fact]
    public async Task NormalizesPcProductsAndReportsRejectedAndUnknownProducts()
    {
        var transport = new RecordingTransport(uri =>
            uri.Host.Equals("catalog.gamepass.com", StringComparison.OrdinalIgnoreCase)
                ? uri.AbsolutePath.Contains("/v3", StringComparison.OrdinalIgnoreCase)
                    ? PlanResponse(uri)
                    : uri.Query.Contains(GamePassConstants.ConsoleCatalogSiglId, StringComparison.OrdinalIgnoreCase)
                        ? Fixture.Read("gamepass-console-sigl.json")
                        : Fixture.Read("gamepass-sigl.json")
                : Fixture.Read("displaycatalog-products.json"));
        var options = new GamePassProviderOptions();
        var provider = new GamePassProvider(
            new GamePassCatalogClient(transport, options),
            new MicrosoftStoreCatalogClient(transport, options),
            new GamePassPlatformClassifier(),
            options);

        var result = await provider.GetCatalogAsync();

        Assert.Equal(6, result.Diagnostics.TotalCatalogIds);
        Assert.Equal(5, result.Diagnostics.PcCatalogIds);
        Assert.Equal(3, result.Diagnostics.ConsoleCatalogIds);
        Assert.Equal(6, result.Diagnostics.ProductsSuccessfullyResolved);
        Assert.Equal(2, result.Diagnostics.ProductsIdentifiedAsPc);
        Assert.Equal(3, result.Diagnostics.ProductsIdentifiedAsConsole);
        Assert.Equal(1, result.Diagnostics.ProductsInBothCatalogs);
        Assert.Equal(1, result.Diagnostics.ProductsRejectedAsNonPc);
        Assert.Equal(2, result.Diagnostics.ProductsThatCouldNotBeClassified);
        Assert.Equal(0, result.Diagnostics.ProductIdsWithoutMetadata);
        Assert.Equal(4, result.Games.Count);
        Assert.Equal(6, result.ProductIds.Count);
        Assert.Equal(
            new[] { "NULLMETA0001", "UNKNOWN0001" },
            result.IncompleteProductIds.OrderBy(id => id).ToArray());

        var desktop = Assert.Single(result.Games, game => game.ProviderGameId == "PCPACKAGE001");
        Assert.Equal("Desktop Package Game", desktop.Name);
        Assert.Equal("Windows PC + Xbox console", desktop.Platform);
        Assert.Equal(
            SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole,
            desktop.AccessPlatforms);
        Assert.Equal("Game Pass catalog", desktop.SubscriptionTier);
        Assert.Equal(SubscriptionAvailability.LeavingSoon, desktop.Availability);
        Assert.Equal(SubscriptionAvailability.LeavingSoon,
            GamePassPlanSelection.PcGamePass.Project(desktop).Availability);
        Assert.Equal(SubscriptionAvailability.Active,
            GamePassPlanSelection.Ultimate.Project(desktop).Availability);
        Assert.Equal(XboxConsoleGenerations.Both, desktop.XboxGenerations);
        Assert.Equal("https", desktop.ImageUri?.Scheme);
        Assert.Equal(new DateTimeOffset(2025, 2, 18, 0, 0, 0, TimeSpan.Zero), desktop.ReleaseDate);
        Assert.Equal("123456789", desktop.RawSourceIdentifiers["XboxTitleId"]);

        var consoleOnly = Assert.Single(result.Games, game => game.ProviderGameId == "CONSOLEONLY001");
        Assert.Equal(SubscriptionPlatforms.XboxConsole, consoleOnly.AccessPlatforms);
        Assert.Equal("Xbox console", consoleOnly.Platform);
        Assert.Equal(SubscriptionPlatforms.XboxConsole,
            Assert.Single(result.Games, game => game.ProviderGameId == "CONSOLE0001").AccessPlatforms);
        Assert.Equal(SubscriptionPlatforms.WindowsPc,
            Assert.Single(result.Games, game => game.ProviderGameId == "PCAVAIL0001").AccessPlatforms);
        Assert.Equal(SubscriptionPlatforms.WindowsPc,
            desktop.PlanPlatforms[GamePassPlanSelection.PcGamePass.Key()]);
        Assert.Equal(SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole,
            desktop.PlanPlatforms[GamePassPlanSelection.Ultimate.Key()]);
        Assert.Equal(SubscriptionPlatforms.None,
            GamePassPlanSelection.Essential.EligiblePlatforms(desktop));

        var snapshot = await provider.GetCatalogSnapshotAsync();
        Assert.Equal(
            SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole,
            snapshot.DeclaredPlatformsByProductId["PCPACKAGE001"]);
        Assert.Equal(SubscriptionPlatforms.XboxConsole,
            snapshot.DeclaredPlatformsByProductId["CONSOLEONLY001"]);
        Assert.Equal(SubscriptionPlatforms.WindowsPc,
            snapshot.DeclaredPlanPlatformsByProductId["PCAVAIL0001"][
                GamePassPlanSelection.Essential.Key()]);
        Assert.True(snapshot.LeavingSoonStatusKnown);
        Assert.Equal(XboxConsoleGenerations.Both,
            snapshot.DeclaredPlanGenerationsByProductId["PCPACKAGE001"][
                GamePassPlanSelection.Ultimate.Key()]);
    }

    private static string PlanResponse(Uri uri)
    {
        var query = Uri.UnescapeDataString(uri.Query);
        if (query.Contains(GamePassConstants.LeavingSoonPcCatalog.SiglId,
                StringComparison.OrdinalIgnoreCase))
        {
            return JsonConvert.SerializeObject(new object[]
            {
                new { siglId = GamePassConstants.LeavingSoonPcCatalog.SiglId,
                    title = "Leaving soon" },
                new { id = "PCPACKAGE001" }
            });
        }
        if (query.Contains(GamePassConstants.LeavingSoonConsoleCatalog.SiglId,
                StringComparison.OrdinalIgnoreCase))
        {
            return JsonConvert.SerializeObject(new object[]
            {
                new { siglId = GamePassConstants.LeavingSoonConsoleCatalog.SiglId,
                    title = "Leaving soon" }
            });
        }
        var catalog = GamePassConstants.PlanCatalogs.Single(item =>
            query.Contains("id=" + item.SiglId, StringComparison.OrdinalIgnoreCase) &&
            query.Contains("platformContext=" + item.PlatformContext,
                StringComparison.OrdinalIgnoreCase) &&
            query.Contains("subscriptionContext=" + item.SubscriptionContext,
                StringComparison.OrdinalIgnoreCase));
        var ids = (catalog.Plan, catalog.Platform) switch
        {
            (GamePassPlanSelection.PcGamePass, _) => new[] { "PCPACKAGE001", "PCAVAIL0001" },
            (GamePassPlanSelection.XboxGamePassConsole, _) => new[] { "CONSOLE0001", "CONSOLEONLY001" },
            (GamePassPlanSelection.Essential, SubscriptionPlatforms.WindowsPc) =>
                new[] { "PCAVAIL0001" },
            (GamePassPlanSelection.Essential, _) => new[] { "CONSOLE0001" },
            (GamePassPlanSelection.Premium, SubscriptionPlatforms.WindowsPc) =>
                new[] { "PCPACKAGE001", "PCAVAIL0001" },
            (GamePassPlanSelection.Premium, _) => new[] { "PCPACKAGE001", "CONSOLE0001" },
            (GamePassPlanSelection.Ultimate, SubscriptionPlatforms.WindowsPc) =>
                new[] { "PCPACKAGE001", "PCAVAIL0001", "UNKNOWN0001" },
            _ => new[] { "PCPACKAGE001", "CONSOLE0001", "CONSOLEONLY001" }
        };
        return JsonConvert.SerializeObject(
            new object[] { new { siglId = catalog.SiglId, title = catalog.Plan.DisplayName() } }
                .Concat(ids.Select(id => (object)new { id })));
    }
}
