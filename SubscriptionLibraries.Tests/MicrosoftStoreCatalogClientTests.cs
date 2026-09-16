using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Services;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class MicrosoftStoreCatalogClientTests
{
    [Fact]
    public async Task ParsesRepresentativeProductsIncludingNullMetadata()
    {
        var transport = new RecordingTransport(_ => Fixture.Read("displaycatalog-products.json"));
        var client = new MicrosoftStoreCatalogClient(
            transport,
            new GamePassProviderOptions());

        var products = await client.GetProductsAsync(new[] { "PCPACKAGE001" });

        Assert.Equal(7, products.Count);
        var desktop = Assert.Single(products, product => product.ProductId == "PCPACKAGE001" &&
            product.LocalizedProperties?[0].ProductTitle == "Desktop Package Game");
        Assert.Equal("Example Developer", desktop.LocalizedProperties?[0].DeveloperName);
        var missing = Assert.Single(products, product => product.ProductId == "NULLMETA0001");
        Assert.Null(missing.LocalizedProperties?[0].ProductTitle);
    }

    [Fact]
    public async Task BatchesIdsAndUsesDocumentedQueryNames()
    {
        var transport = new RecordingTransport(_ => "{\"Products\":[]}");
        var client = new MicrosoftStoreCatalogClient(
            transport,
            new GamePassProviderOptions { ProductBatchSize = 2 });

        await client.GetProductsAsync(new[] { "ONE", "TWO", "THREE", "FOUR", "FIVE", "ONE" });

        Assert.Equal(3, transport.Requests.Count);
        Assert.All(transport.Requests, request =>
        {
            Assert.Contains("bigIds=", request.Query);
            Assert.Contains("market=US", request.Query);
            Assert.Contains("languages=en-US", request.Query);
        });
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Products\":{}}")]
    public async Task RejectsUnexpectedProductResponseShapes(string response)
    {
        var client = new MicrosoftStoreCatalogClient(
            new RecordingTransport(_ => response),
            new GamePassProviderOptions());

        await Assert.ThrowsAsync<CatalogDataException>(() =>
            client.GetProductsAsync(new[] { "PRODUCT" }));
    }
}
