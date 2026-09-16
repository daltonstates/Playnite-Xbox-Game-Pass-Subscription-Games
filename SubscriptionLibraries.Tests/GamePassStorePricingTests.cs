using Newtonsoft.Json;
using SubscriptionLibraries.Core.Providers.GamePass;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class GamePassStorePricingTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 4.99, false)]
    [InlineData(4.99, 4.99, false)]
    public void RequiresZeroMsrpAndListPriceOnPublicPurchaseOffer(
        decimal listPrice, decimal msrp, bool expected)
    {
        var product = new MicrosoftStoreProduct
        {
            DisplaySkuAvailabilities = new()
            {
                new StoreDisplaySkuAvailability
                {
                    Sku = new StoreSku { SkuId = "0010" },
                    Availabilities = new()
                    {
                        new StoreAvailability
                        {
                            Actions = new() { "Purchase", "Fulfill" },
                            OrderManagementData = new StoreOrderManagementData
                            {
                                Price = new StorePrice { ListPrice = listPrice, Msrp = msrp }
                            }
                        },
                        new StoreAvailability
                        {
                            Actions = new() { "License" },
                            OrderManagementData = new StoreOrderManagementData
                            {
                                Price = new StorePrice { ListPrice = 0, Msrp = 0 }
                            }
                        }
                    }
                }
            }
        };

        Assert.Equal(expected, GamePassStorePricing.IsConfirmedFreeToPlay(product));
    }

    [Fact]
    public void MissingPurchaseOfferDoesNotClaimFreeToPlay()
    {
        var product = JsonConvert.DeserializeObject<MicrosoftStoreProduct>(
            "{\"DisplaySkuAvailabilities\":[{\"Sku\":{\"SkuId\":\"0010\"}," +
            "\"Availabilities\":[{\"Actions\":[\"License\"]," +
            "\"OrderManagementData\":{\"Price\":{\"ListPrice\":0,\"MSRP\":0}}}]}]}")!;

        Assert.False(GamePassStorePricing.IsConfirmedFreeToPlay(product));
    }
}
