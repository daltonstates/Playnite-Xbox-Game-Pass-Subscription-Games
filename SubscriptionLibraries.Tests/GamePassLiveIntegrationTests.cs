using System.Net.Http.Headers;
using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Services;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class GamePassLiveIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task LivePcCatalogReturnsWindowsGamesWhenExplicitlyEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SUBSCRIPTIONLIBRARIES_RUN_LIVE_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SubscriptionLibraries.Tests", "1.0"));
        var transport = new HttpClientService(httpClient);
        var options = new GamePassProviderOptions();
        var provider = new GamePassProvider(
            new GamePassCatalogClient(transport, options),
            new MicrosoftStoreCatalogClient(transport, options),
            new GamePassPlatformClassifier(),
            options);

        var result = await provider.GetCatalogAsync();

        Assert.True(result.Diagnostics.TotalCatalogIds > 100);
        Assert.True(result.Diagnostics.ProductsIdentifiedAsPc > 100);
        Assert.Equal(0, result.Diagnostics.ProductsThatCouldNotBeClassified);
        Assert.Equal(0, result.Diagnostics.ProductIdsWithoutMetadata);
        Assert.All(result.Games, game => Assert.Equal("Windows PC", game.Platform));
    }
}
