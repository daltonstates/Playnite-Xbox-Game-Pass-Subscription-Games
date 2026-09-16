using SubscriptionLibraries.Core.Providers.GamePass;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class GamePassProviderTests
{
    [Fact]
    public async Task NormalizesPcProductsAndReportsRejectedAndUnknownProducts()
    {
        var transport = new RecordingTransport(uri =>
            uri.Host.Equals("catalog.gamepass.com", StringComparison.OrdinalIgnoreCase)
                ? Fixture.Read("gamepass-sigl.json")
                : Fixture.Read("displaycatalog-products.json"));
        var options = new GamePassProviderOptions();
        var provider = new GamePassProvider(
            new GamePassCatalogClient(transport, options),
            new MicrosoftStoreCatalogClient(transport, options),
            new GamePassPlatformClassifier(),
            options);

        var result = await provider.GetCatalogAsync();

        Assert.Equal(5, result.Diagnostics.TotalCatalogIds);
        Assert.Equal(5, result.Diagnostics.ProductsSuccessfullyResolved);
        Assert.Equal(2, result.Diagnostics.ProductsIdentifiedAsPc);
        Assert.Equal(1, result.Diagnostics.ProductsRejectedAsNonPc);
        Assert.Equal(2, result.Diagnostics.ProductsThatCouldNotBeClassified);
        Assert.Equal(0, result.Diagnostics.ProductIdsWithoutMetadata);
        Assert.Equal(2, result.Games.Count);
        Assert.Equal(5, result.ProductIds.Count);
        Assert.Equal(
            new[] { "NULLMETA0001", "UNKNOWN0001" },
            result.IncompleteProductIds.OrderBy(id => id).ToArray());

        var desktop = Assert.Single(result.Games, game => game.ProviderGameId == "PCPACKAGE001");
        Assert.Equal("Desktop Package Game", desktop.Name);
        Assert.Equal("Windows PC", desktop.Platform);
        Assert.Equal("PC Game Pass", desktop.SubscriptionTier);
        Assert.Equal("https", desktop.ImageUri?.Scheme);
        Assert.Equal(new DateTimeOffset(2025, 2, 18, 0, 0, 0, TimeSpan.Zero), desktop.ReleaseDate);
        Assert.Equal("123456789", desktop.RawSourceIdentifiers["XboxTitleId"]);
    }
}
