using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Services;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class GamePassCatalogClientTests
{
    [Fact]
    public async Task ParsesPcSiglAndDeduplicatesStableProductIds()
    {
        var transport = new RecordingTransport(_ => Fixture.Read("gamepass-sigl.json"));
        var client = new GamePassCatalogClient(transport, new GamePassProviderOptions());

        var result = await client.GetProductIdsAsync();

        Assert.Equal(GamePassConstants.PcCatalogSiglId, result.SiglId);
        Assert.Equal("All PC Games", result.Title);
        Assert.Equal(5, result.ProductIds.Count);
        Assert.Equal("PCPACKAGE001", result.ProductIds[0]);
        Assert.Single(transport.Requests);
        Assert.Contains("catalog.gamepass.com/sigls/v2", transport.Requests[0].AbsoluteUri);
        Assert.Contains("market=US", transport.Requests[0].Query);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("[]")]
    public async Task RejectsInvalidOrUnidentifiedSiglResponses(string response)
    {
        var client = new GamePassCatalogClient(
            new RecordingTransport(_ => response),
            new GamePassProviderOptions());

        await Assert.ThrowsAsync<CatalogDataException>(() => client.GetProductIdsAsync());
    }

    [Fact]
    public async Task RejectsJsonBeyondTheConfiguredDepthLimit()
    {
        var response = new string('[', 70) + "{}" + new string(']', 70);
        var client = new GamePassCatalogClient(
            new RecordingTransport(_ => response),
            new GamePassProviderOptions());

        await Assert.ThrowsAsync<CatalogDataException>(() => client.GetProductIdsAsync());
    }
}
