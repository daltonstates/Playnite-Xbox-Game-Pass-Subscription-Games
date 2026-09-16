using Newtonsoft.Json;
using SubscriptionLibraries.Core.Providers.GamePass;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class GamePassPlatformClassifierTests
{
    private readonly IReadOnlyDictionary<string, MicrosoftStoreProduct> products;
    private readonly GamePassPlatformClassifier classifier = new();

    public GamePassPlatformClassifierTests()
    {
        var response = JsonConvert.DeserializeObject<MicrosoftStoreProductsResponse>(
            Fixture.Read("displaycatalog-products.json"));
        products = response!.Products!
            .GroupBy(product => product.ProductId!)
            .ToDictionary(group => group.Key, group => group.First());
    }

    [Fact]
    public void AcceptsExplicitWindowsDesktopPackage()
    {
        var result = classifier.Classify(products["PCPACKAGE001"]);

        Assert.Equal(ProductPlatformClassification.WindowsPc, result.Classification);
        Assert.Contains("Windows.Desktop", result.Evidence);
    }

    [Fact]
    public void AcceptsActionableDesktopAvailabilityForLauncherDeliveredGame()
    {
        var result = classifier.Classify(products["PCAVAIL0001"]);

        Assert.Equal(ProductPlatformClassification.WindowsPc, result.Classification);
        Assert.Contains("availability", result.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsConsolePackageAndIgnoresSentinelDesktopAvailability()
    {
        var result = classifier.Classify(products["CONSOLE0001"]);

        Assert.Equal(ProductPlatformClassification.NonPc, result.Classification);
        Assert.Contains("Windows.Xbox", result.Evidence);
        Assert.DoesNotContain("Windows.Desktop", result.Evidence);
    }

    [Fact]
    public void LeavesMissingPlatformMetadataUnclassified()
    {
        var result = classifier.Classify(products["UNKNOWN0001"]);

        Assert.Equal(ProductPlatformClassification.Unknown, result.Classification);
    }

    [Fact]
    public void AcceptsLegacyWindowsDesktopPackage()
    {
        var product = new MicrosoftStoreProduct
        {
            DisplaySkuAvailabilities = new List<StoreDisplaySkuAvailability>
            {
                new()
                {
                    Sku = new StoreSku
                    {
                        Properties = new StoreSkuProperties
                        {
                            Packages = new List<StorePackage>
                            {
                                new()
                                {
                                    PlatformDependencies = new List<StorePlatformDependency>
                                    {
                                        new() { PlatformName = GamePassConstants.LegacyWindowsDesktopPlatform }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var result = classifier.Classify(product);

        Assert.Equal(ProductPlatformClassification.WindowsPc, result.Classification);
    }
}
