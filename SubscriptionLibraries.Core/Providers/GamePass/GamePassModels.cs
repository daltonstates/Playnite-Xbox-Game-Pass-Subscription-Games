using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SubscriptionLibraries.Core.Models;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public sealed class GamePassSiglEntry
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("siglId")]
    public string? SiglId { get; set; }

    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }
}

public sealed class GamePassCatalogIndex
{
    public string? SiglId { get; set; }

    public string? Title { get; set; }

    public IReadOnlyList<string> ProductIds { get; set; } = Array.Empty<string>();
}

public sealed class MicrosoftStoreProductsResponse
{
    [JsonProperty("Products")]
    public List<MicrosoftStoreProduct>? Products { get; set; }
}

public sealed class MicrosoftStoreProduct
{
    [JsonProperty("ProductId")]
    public string? ProductId { get; set; }

    [JsonProperty("ProductFamily")]
    public string? ProductFamily { get; set; }

    [JsonProperty("ProductKind")]
    public string? ProductKind { get; set; }

    [JsonProperty("LocalizedProperties")]
    public List<StoreLocalizedProperty>? LocalizedProperties { get; set; }

    [JsonProperty("MarketProperties")]
    public List<StoreMarketProperty>? MarketProperties { get; set; }

    [JsonProperty("Properties")]
    public StoreProductProperties? Properties { get; set; }

    [JsonProperty("AlternateIds")]
    public List<StoreAlternateId>? AlternateIds { get; set; }

    [JsonProperty("DisplaySkuAvailabilities")]
    public List<StoreDisplaySkuAvailability>? DisplaySkuAvailabilities { get; set; }
}

public sealed class StoreLocalizedProperty
{
    [JsonProperty("ProductTitle")]
    public string? ProductTitle { get; set; }

    [JsonProperty("ProductDescription")]
    public string? ProductDescription { get; set; }

    [JsonProperty("ShortDescription")]
    public string? ShortDescription { get; set; }

    [JsonProperty("DeveloperName")]
    public string? DeveloperName { get; set; }

    [JsonProperty("PublisherName")]
    public string? PublisherName { get; set; }

    [JsonProperty("Images")]
    public List<StoreImage>? Images { get; set; }
}

public sealed class StoreImage
{
    [JsonProperty("ImagePurpose")]
    public string? ImagePurpose { get; set; }

    [JsonProperty("Uri")]
    public string? Uri { get; set; }

    [JsonProperty("Width")]
    public int? Width { get; set; }

    [JsonProperty("Height")]
    public int? Height { get; set; }
}

public sealed class StoreMarketProperty
{
    [JsonProperty("OriginalReleaseDate")]
    public string? OriginalReleaseDate { get; set; }
}

public sealed class StoreProductProperties
{
    [JsonProperty("PackageFamilyName")]
    public string? PackageFamilyName { get; set; }

    [JsonProperty("PackageIdentityName")]
    public string? PackageIdentityName { get; set; }

    [JsonProperty("Attributes")]
    public List<StoreProductAttribute>? Attributes { get; set; }
}

public sealed class StoreProductAttribute
{
    [JsonProperty("Name")]
    public string? Name { get; set; }

    [JsonProperty("ApplicablePlatforms")]
    public List<string>? ApplicablePlatforms { get; set; }
}

public sealed class StoreAlternateId
{
    [JsonProperty("IdType")]
    public string? IdType { get; set; }

    [JsonProperty("Value")]
    public string? Value { get; set; }
}

public sealed class StoreDisplaySkuAvailability
{
    [JsonProperty("Sku")]
    public StoreSku? Sku { get; set; }

    [JsonProperty("Availabilities")]
    public List<StoreAvailability>? Availabilities { get; set; }
}

public sealed class StoreSku
{
    [JsonProperty("SkuId")]
    public string? SkuId { get; set; }

    [JsonProperty("Properties")]
    public StoreSkuProperties? Properties { get; set; }
}

public sealed class StoreSkuProperties
{
    [JsonProperty("Packages")]
    public List<StorePackage>? Packages { get; set; }

    [JsonProperty("FulfillmentType")]
    public string? FulfillmentType { get; set; }

    [JsonProperty("FulfillmentPluginId")]
    public string? FulfillmentPluginId { get; set; }
}

public sealed class StorePackage
{
    [JsonProperty("PackageFamilyName")]
    public string? PackageFamilyName { get; set; }

    [JsonProperty("PackageFormat")]
    public string? PackageFormat { get; set; }

    [JsonProperty("Architectures")]
    public List<string>? Architectures { get; set; }

    [JsonProperty("PlatformDependencies")]
    public List<StorePlatformDependency>? PlatformDependencies { get; set; }
}

public sealed class StorePlatformDependency
{
    [JsonProperty("PlatformName")]
    public string? PlatformName { get; set; }
}

public sealed class StoreAvailability
{
    [JsonProperty("AvailabilityId")]
    public string? AvailabilityId { get; set; }

    [JsonProperty("Actions")]
    public List<string>? Actions { get; set; }

    [JsonProperty("OrderManagementData")]
    public StoreOrderManagementData? OrderManagementData { get; set; }

    [JsonProperty("Conditions")]
    public StoreAvailabilityConditions? Conditions { get; set; }
}

public sealed class StoreOrderManagementData
{
    [JsonProperty("Price")]
    public StorePrice? Price { get; set; }
}

public sealed class StorePrice
{
    [JsonProperty("MSRP")]
    public decimal? Msrp { get; set; }

    [JsonProperty("ListPrice")]
    public decimal? ListPrice { get; set; }
}

public sealed class StoreAvailabilityConditions
{
    [JsonProperty("ClientConditions")]
    public StoreClientConditions? ClientConditions { get; set; }

    [JsonProperty("StartDate")]
    public string? StartDate { get; set; }

    [JsonProperty("EndDate")]
    public string? EndDate { get; set; }
}

public sealed class StoreClientConditions
{
    [JsonProperty("AllowedPlatforms")]
    public List<StoreAllowedPlatform>? AllowedPlatforms { get; set; }
}

public sealed class StoreAllowedPlatform
{
    [JsonProperty("PlatformName")]
    public string? PlatformName { get; set; }
}

public enum ProductPlatformClassification
{
    WindowsPc,
    NonPc,
    Unknown
}

public sealed class ProductPlatformClassificationResult
{
    public ProductPlatformClassification Classification { get; set; }

    public string Evidence { get; set; } = string.Empty;
}

public sealed class RejectedCatalogProduct
{
    public string? ProductId { get; set; }

    public string? Name { get; set; }

    public ProductPlatformClassification Classification { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class CatalogDiagnostics
{
    public int TotalCatalogIds { get; set; }

    public int PcCatalogIds { get; set; }

    public int ConsoleCatalogIds { get; set; }

    public int ProductsSuccessfullyResolved { get; set; }

    public int ProductsIdentifiedAsPc { get; set; }

    public int ProductsIdentifiedAsConsole { get; set; }

    public int ProductsInBothCatalogs { get; set; }

    public int ProductsRejectedAsNonPc { get; set; }

    public int ProductsThatCouldNotBeClassified { get; set; }

    public int ProductIdsWithoutMetadata { get; set; }
}

public sealed class GamePassCatalogResult
{
    public bool LeavingSoonStatusKnown { get; set; }
    public IReadOnlyList<string> PcProductIds { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ConsoleProductIds { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ProductIds { get; set; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, Dictionary<string, SubscriptionPlatforms>>
        LeavingSoonPlanPlatformsByProductId { get; set; } =
        new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>(
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Dictionary<string, SubscriptionPlatforms>>
        DeclaredPlanPlatformsByProductId { get; set; } =
        new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>(
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Dictionary<string, XboxConsoleGenerations>>
        DeclaredPlanGenerationsByProductId { get; set; } =
        new Dictionary<string, Dictionary<string, XboxConsoleGenerations>>(
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> IncompleteProductIds { get; set; } = Array.Empty<string>();

    public IReadOnlyList<SubscriptionGame> Games { get; set; } = Array.Empty<SubscriptionGame>();

    public IReadOnlyList<RejectedCatalogProduct> RejectedProducts { get; set; } =
        Array.Empty<RejectedCatalogProduct>();

    public CatalogDiagnostics Diagnostics { get; set; } = new();
}
