using System;
using System.Linq;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public static class GamePassStorePricing
{
    public static bool IsConfirmedFreeToPlay(MicrosoftStoreProduct product)
    {
        if (product is null)
        {
            throw new ArgumentNullException(nameof(product));
        }

        // Observed Display Catalog SKU 0010 is the public Store purchase SKU.
        // Subscription licenses and promotional offers can also have a zero
        // price, so zero by itself is not free-to-play evidence.
        var primarySku = product.DisplaySkuAvailabilities?
            .FirstOrDefault(value => string.Equals(value.Sku?.SkuId, "0010",
                StringComparison.OrdinalIgnoreCase));
        var publicOffers = primarySku?.Availabilities?
            .Where(value => value.Actions?.Contains("Purchase",
                    StringComparer.OrdinalIgnoreCase) == true &&
                value.Actions.Contains("Fulfill", StringComparer.OrdinalIgnoreCase))
            .ToList();
        return publicOffers?.Count > 0 && publicOffers.All(value =>
            value.OrderManagementData?.Price?.Msrp == 0m &&
            value.OrderManagementData.Price.ListPrice == 0m);
    }
}
