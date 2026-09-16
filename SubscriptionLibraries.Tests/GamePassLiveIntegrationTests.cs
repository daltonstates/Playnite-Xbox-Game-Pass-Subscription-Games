using System.Net.Http.Headers;
using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Services;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class GamePassLiveIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task LivePcAndConsoleCatalogsReturnPlatformMembershipWhenExplicitlyEnabled()
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
        Assert.True(result.Diagnostics.ConsoleCatalogIds > 100);
        Assert.True(result.Diagnostics.ProductsIdentifiedAsPc > 100);
        Assert.True(result.Diagnostics.ProductsIdentifiedAsConsole > 100);
        Assert.All(result.Games, game =>
            Assert.NotEqual(SubscriptionPlatforms.None, game.AccessPlatforms));
        Assert.All(
            result.Games.Where(game =>
                (game.AccessPlatforms & SubscriptionPlatforms.WindowsPc) != 0),
            game => Assert.Contains("Windows PC", game.Platform));
        var essentialCount = result.Games.Count(game =>
            GamePassPlanSelection.Essential.EligiblePlatforms(game) != SubscriptionPlatforms.None);
        var premiumCount = result.Games.Count(game =>
            GamePassPlanSelection.Premium.EligiblePlatforms(game) != SubscriptionPlatforms.None);
        var ultimateCount = result.Games.Count(game =>
            GamePassPlanSelection.Ultimate.EligiblePlatforms(game) != SubscriptionPlatforms.None);
        Assert.InRange(essentialCount, 1, premiumCount);
        Assert.InRange(premiumCount, essentialCount, ultimateCount);
        Assert.Contains(result.Games, game =>
            GamePassPlanSelection.PcGamePass.EligiblePlatforms(game) == SubscriptionPlatforms.WindowsPc);
        Assert.True(result.LeavingSoonStatusKnown);
        Assert.Contains(result.Games, game =>
            (game.XboxGenerations & XboxConsoleGenerations.XboxOne) != 0);
        Assert.Contains(result.Games, game =>
            (game.XboxGenerations & XboxConsoleGenerations.SeriesXorS) != 0);
    }
}
