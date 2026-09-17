using Newtonsoft.Json.Linq;
using SubscriptionLibraries.Core.Providers.UbisoftPlus;
using SubscriptionLibraries.Core.Services;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class UbisoftPlusProviderTests
{
    private const string Page = "<mooncake-data algolia-api-key=\"dc39136c7548697cbee2fbc171d04133\" " +
        "algolia-app-id=\"XELY3U4LOD\"></mooncake-data>" +
        "<div id=\"ubisoftplus-games-redesign\" locale=\"en_US\"></div>" +
        "production__us_ubisoft__products__en_US__release_date";

    [Fact]
    public async Task ImportsOnlyCurrentPcGamesWithExplicitPlansAndStableIds()
    {
        var response = Response();
        var provider = Provider(response, out var transport);

        var snapshot = await provider.GetCatalogSnapshotAsync();

        Assert.Equal(20, snapshot.Games.Count);
        Assert.Single(snapshot.IncompleteProductIds);
        Assert.Equal(21, snapshot.ProductIds.Count);
        Assert.False(snapshot.LeavingSoonStatusKnown);
        var first = snapshot.Games.First();
        Assert.StartsWith(UbisoftPlusProvider.GameIdPrefix, first.ProviderGameId);
        Assert.Equal("Ubisoft+", first.ProviderName);
        Assert.Equal("store.ubisoft.com", first.StoreUri?.Host);
        Assert.True(UbisoftPlusProvider.Includes(UbisoftPlusPlanSelection.Classics, first));
        Assert.False(UbisoftPlusProvider.Includes(UbisoftPlusPlanSelection.Premium, first));
        Assert.Equal(2, transport.Requests.Count);
        Assert.Contains("partOfUbisoftPlus", Uri.UnescapeDataString(transport.Requests[1].Query));
    }

    [Fact]
    public async Task RejectsIncompletePaginationInsteadOfUsingPartialCatalog()
    {
        var response = Response();
        response["nbPages"] = 2;
        var provider = Provider(response, out _);

        await Assert.ThrowsAsync<CatalogDataException>(() => provider.GetCatalogSnapshotAsync());
    }

    [Fact]
    public async Task RejectsUnknownPlanLabelInsteadOfMisstatingMembership()
    {
        var response = Response();
        ((JObject)((JArray)response["hits"]!)[0]!)!["partofSubscriptionOffer"] =
            new JArray("Ubisoft+ Unlimited");
        var provider = Provider(response, out _);

        await Assert.ThrowsAsync<CatalogDataException>(() => provider.GetCatalogSnapshotAsync());
    }

    [Fact]
    public async Task RejectsProductLinkOutsideSelectedRegion()
    {
        var response = Response();
        ((JObject)((JArray)response["hits"]!)[0]!)!["link"] =
            "https://store.ubisoft.com/ca/game/000000000000000000000000.html";
        var provider = Provider(response, out _);

        await Assert.ThrowsAsync<CatalogDataException>(() => provider.GetCatalogSnapshotAsync());
    }

    [Theory]
    [InlineData("Platform")]
    [InlineData("availability")]
    [InlineData("comingSoon")]
    [InlineData("product_type")]
    [InlineData("preorder")]
    public async Task RejectsMissingMembershipFieldsInsteadOfRemovingOneGame(string field)
    {
        var response = Response();
        ((JObject)((JArray)response["hits"]!)[0]!)!.Remove(field);
        var provider = Provider(response, out _);

        await Assert.ThrowsAsync<CatalogDataException>(() => provider.GetCatalogSnapshotAsync());
    }

    [Fact]
    public async Task KeepsDistinctEditionsWithTheSameTitle()
    {
        var response = Response();
        var hits = (JArray)response["hits"]!;
        ((JObject)hits[1]!)["title"] = ((JObject)hits[0]!)["title"];
        var provider = Provider(response, out _);

        var snapshot = await provider.GetCatalogSnapshotAsync();
        var games = snapshot.Games.ToList();

        Assert.Equal(games[0].Name, games[1].Name);
        Assert.NotEqual(games[0].ProviderGameId, games[1].ProviderGameId);
    }

    [Fact]
    public async Task RejectsUnverifiedTrialProductType()
    {
        var response = Response();
        ((JObject)((JArray)response["hits"]!)[0]!)!["product_type"] = "Trials";
        var provider = Provider(response, out _);

        await Assert.ThrowsAsync<CatalogDataException>(() => provider.GetCatalogSnapshotAsync());
    }

    [Fact]
    public void DoesNotSubstituteUsCatalogForOtherRegions()
    {
        Assert.True(UbisoftPlusProvider.Supports("US", "en-US"));
        Assert.False(UbisoftPlusProvider.Supports("CA", "en-US"));
        Assert.False(UbisoftPlusProvider.Supports("US", "fr-FR"));
    }

    private static UbisoftPlusProvider Provider(JObject response, out RecordingTransport transport)
    {
        transport = new RecordingTransport(uri =>
            uri.Host == "store.ubisoft.com" ? Page : response.ToString());
        return new UbisoftPlusProvider(transport);
    }

    private static JObject Response()
    {
        var hits = new JArray();
        for (var index = 0; index < 20; index++)
        {
            hits.Add(Product(index, index % 2 == 0
                ? UbisoftPlusProvider.ClassicsPlan : UbisoftPlusProvider.PremiumPlan));
        }
        var ambiguous = Product(20, null);
        hits.Add(ambiguous);
        var preorder = Product(21, UbisoftPlusProvider.PremiumPlan);
        preorder["preorder"] = true;
        hits.Add(preorder);
        var dlc = Product(22, UbisoftPlusProvider.PremiumPlan);
        dlc["product_type"] = "DLCs";
        hits.Add(dlc);
        var unavailable = Product(23, UbisoftPlusProvider.PremiumPlan);
        unavailable["availability"] = 0;
        hits.Add(unavailable);
        return new JObject
        {
            ["hits"] = hits,
            ["nbHits"] = hits.Count,
            ["nbPages"] = 1,
            ["page"] = 0
        };
    }

    private static JObject Product(int index, string? plan)
    {
        var id = index.ToString("x24");
        return new JObject
        {
            ["id"] = id,
            ["MasterID"] = id,
            ["title"] = "Game " + index,
            ["link"] = $"https://store.ubisoft.com/us/game-{index}/{id}.html?lang=en_US",
            ["image_link"] = "https://example.com/game.jpg",
            ["partOfUbisoftPlus"] = true,
            ["partofSubscriptionOffer"] = plan is null ? new JArray() : new JArray(plan),
            ["preorder"] = false,
            ["product_type"] = "Games",
            ["availability"] = 1,
            ["Platform"] = "PC (Digital)",
            ["comingSoon"] = "No"
        };
    }
}
