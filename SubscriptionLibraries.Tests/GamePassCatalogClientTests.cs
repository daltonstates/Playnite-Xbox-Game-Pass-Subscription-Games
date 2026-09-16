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

    [Fact]
    public async Task ParsesConsoleSiglAndRejectsWrongHeader()
    {
        var transport = new RecordingTransport(_ => Fixture.Read("gamepass-console-sigl.json"));
        var client = new GamePassCatalogClient(transport, new GamePassProviderOptions());

        var result = await client.GetProductIdsAsync(GamePassConstants.ConsoleCatalogSiglId);

        Assert.Equal(GamePassConstants.ConsoleCatalogSiglId, result.SiglId);
        Assert.Equal("All Console Games", result.Title);
        Assert.Equal(3, result.ProductIds.Count);
        Assert.Contains(GamePassConstants.ConsoleCatalogSiglId, transport.Requests[0].Query);
        await Assert.ThrowsAsync<CatalogDataException>(() =>
            client.GetProductIdsAsync(GamePassConstants.PcCatalogSiglId));
    }

    [Fact]
    public async Task PlanCatalogUsesVerifiedV3ContextParameters()
    {
        var catalog = GamePassConstants.PlanCatalogs.Single(item =>
            item.Plan == GamePassPlanSelection.Premium &&
            item.ConsoleGeneration == SubscriptionLibraries.Core.Models.XboxConsoleGenerations.XboxOne);
        var response = "[{\"siglId\":\"" + catalog.SiglId +
            "\",\"title\":\"Premium\"},{\"id\":\"9TEST\"}]";
        var transport = new RecordingTransport(_ => response);
        var client = new GamePassCatalogClient(transport, new GamePassProviderOptions());

        var result = await client.GetPlanProductIdsAsync(catalog);

        Assert.Single(result.ProductIds);
        var uri = Assert.Single(transport.Requests);
        Assert.EndsWith("/sigls/v3", uri.AbsolutePath);
        Assert.Contains("platformContext=ConsoleGen8", uri.Query);
        Assert.Contains("subscriptionContext=cfq7ttc0p85b", uri.Query);
    }

    [Fact]
    public async Task EmptyLeavingSoonCollectionIsValidWhenHeaderMatches()
    {
        var catalog = GamePassConstants.LeavingSoonPcCatalog;
        var response = "[{\"siglId\":\"" + catalog.SiglId +
            "\",\"title\":\"Leaving soon\"}]";
        var client = new GamePassCatalogClient(
            new RecordingTransport(_ => response), new GamePassProviderOptions());

        var result = await client.GetPlanProductIdsAsync(catalog);

        Assert.Empty(result.ProductIds);
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
