using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Services;
using SubscriptionLibraries.Core.Utilities;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public sealed class GamePassProvider : ISubscriptionCatalogProvider
{
    private static readonly string[] CoverImagePurposes =
    {
        "Poster",
        "BoxArt",
        "BrandedKeyArt",
        "FeaturePromotionalSquareArt"
    };

    private static readonly string[] BackgroundImagePurposes =
    {
        "SuperHeroArt",
        "TitledHeroArt",
        "Hero"
    };

    private readonly GamePassCatalogClient catalogClient;
    private readonly MicrosoftStoreCatalogClient storeClient;
    private readonly GamePassPlatformClassifier platformClassifier;
    private readonly GamePassProviderOptions options;
    private readonly ISubscriptionLogger logger;

    public GamePassProvider(
        GamePassCatalogClient catalogClient,
        MicrosoftStoreCatalogClient storeClient,
        GamePassPlatformClassifier platformClassifier,
        GamePassProviderOptions options,
        ISubscriptionLogger? logger = null)
    {
        this.catalogClient = catalogClient ?? throw new ArgumentNullException(nameof(catalogClient));
        this.storeClient = storeClient ?? throw new ArgumentNullException(nameof(storeClient));
        this.platformClassifier = platformClassifier ??
            throw new ArgumentNullException(nameof(platformClassifier));
        this.options = (options ?? throw new ArgumentNullException(nameof(options))).NormalizeAndValidate();
        this.logger = logger ?? NullSubscriptionLogger.Instance;
    }

    public string Id => GamePassConstants.ProviderId;

    public string Name => GamePassConstants.ProviderName;

    public CatalogDiagnostics? LastDiagnostics { get; private set; }

    public async Task<IReadOnlyCollection<SubscriptionGame>> GetGamesAsync(
        CancellationToken cancellationToken = default) =>
        (await GetCatalogAsync(cancellationToken).ConfigureAwait(false)).Games;

    public async Task<SubscriptionCatalogSnapshot> GetCatalogSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return new SubscriptionCatalogSnapshot
        {
            Games = result.Games,
            ProductIds = result.ProductIds,
            IncompleteProductIds = result.IncompleteProductIds
        };
    }

    public async Task<GamePassCatalogResult> GetCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        logger.Info("Starting PC Game Pass catalog synchronization.");
        var index = await catalogClient.GetProductIdsAsync(cancellationToken).ConfigureAwait(false);
        logger.Info($"Retrieved {index.ProductIds.Count} Game Pass product IDs.");

        var products = await storeClient
            .GetProductsAsync(index.ProductIds, cancellationToken)
            .ConfigureAwait(false);
        logger.Info($"Resolved {products.Count} Microsoft Store products.");

        if (products.Count == 0)
        {
            throw new CatalogDataException(
                "Microsoft returned no product metadata for the non-empty PC Game Pass catalog.");
        }

        var requestedIds = new HashSet<string>(index.ProductIds, StringComparer.OrdinalIgnoreCase);
        var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var incompleteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var games = new List<SubscriptionGame>();
        var rejected = new List<RejectedCatalogProduct>();
        var nonPcCount = 0;
        var unknownCount = 0;

        foreach (var product in products)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var productId = product.ProductId?.Trim();
            if (string.IsNullOrWhiteSpace(productId))
            {
                unknownCount++;
                rejected.Add(Reject(product, ProductPlatformClassification.Unknown, "Product metadata had no ProductId."));
                continue;
            }

            if (!requestedIds.Contains(productId!) || !resolvedIds.Add(productId!))
            {
                continue;
            }

            var classification = platformClassifier.Classify(product);
            if (classification.Classification == ProductPlatformClassification.NonPc)
            {
                nonPcCount++;
                rejected.Add(Reject(product, classification.Classification, classification.Evidence));
                continue;
            }

            if (classification.Classification == ProductPlatformClassification.Unknown)
            {
                unknownCount++;
                incompleteIds.Add(productId!);
                rejected.Add(Reject(product, classification.Classification, classification.Evidence));
                continue;
            }

            var game = Normalize(product);
            if (game is null)
            {
                unknownCount++;
                incompleteIds.Add(productId!);
                rejected.Add(Reject(
                    product,
                    ProductPlatformClassification.Unknown,
                    "Required normalized metadata (ProductId or title) was missing."));
                continue;
            }

            if (importedIds.Add(game.ProviderGameId))
            {
                games.Add(game);
            }
        }

        incompleteIds.UnionWith(requestedIds.Where(id => !resolvedIds.Contains(id)));

        var diagnostics = new CatalogDiagnostics
        {
            TotalCatalogIds = index.ProductIds.Count,
            ProductsSuccessfullyResolved = resolvedIds.Count,
            ProductsIdentifiedAsPc = games.Count,
            ProductsRejectedAsNonPc = nonPcCount,
            ProductsThatCouldNotBeClassified = unknownCount,
            ProductIdsWithoutMetadata = Math.Max(0, index.ProductIds.Count - resolvedIds.Count)
        };
        LastDiagnostics = diagnostics;

        logger.Info($"Filtered {nonPcCount} products that are not Windows PC titles.");
        if (unknownCount > 0 || diagnostics.ProductIdsWithoutMetadata > 0)
        {
            logger.Warn(
                $"Could not classify {unknownCount} resolved products; " +
                $"{diagnostics.ProductIdsWithoutMetadata} product IDs had no metadata.");
        }

        logger.Info($"Imported {games.Count} PC Game Pass games.");
        if (games.Count == 0)
        {
            throw new CatalogDataException(
                "The catalog contained no products with reliable Windows PC platform evidence.");
        }

        return new GamePassCatalogResult
        {
            ProductIds = index.ProductIds,
            IncompleteProductIds = incompleteIds.ToList(),
            Games = games,
            RejectedProducts = rejected,
            Diagnostics = diagnostics
        };
    }

    private SubscriptionGame? Normalize(MicrosoftStoreProduct product)
    {
        var productId = product.ProductId?.Trim();
        var localized = product.LocalizedProperties?
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value.ProductTitle)) ??
            product.LocalizedProperties?.FirstOrDefault();
        var title = localized?.ProductTitle?.Trim();
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var slug = NormalizationHelper.CreateStoreSlug(title);
        var languagePath = Uri.EscapeDataString(options.Language);
        var productPath = Uri.EscapeDataString(productId);
        var game = new SubscriptionGame
        {
            ProviderId = Id,
            ProviderName = Name,
            ProviderGameId = productId!,
            MicrosoftProductId = productId,
            Name = title!,
            Platform = "Windows PC",
            SubscriptionTier = GamePassConstants.SubscriptionTier,
            Availability = SubscriptionAvailability.Active,
            StoreUri = new Uri(
                $"https://www.xbox.com/{languagePath}/games/store/{slug}/{productPath}"),
            Description = FirstNonEmpty(localized?.ShortDescription, localized?.ProductDescription),
            Publisher = NullIfEmpty(localized?.PublisherName),
            Developer = NullIfEmpty(localized?.DeveloperName),
            ReleaseDate = ParseDate(product.MarketProperties?.FirstOrDefault()?.OriginalReleaseDate),
            ImageUri = FindImage(localized?.Images, CoverImagePurposes),
            BackgroundImageUri = FindImage(localized?.Images, BackgroundImagePurposes)
        };

        AddRawIdentifier(game, "MicrosoftProductId", productId);
        AddRawIdentifier(game, "PackageFamilyName", product.Properties?.PackageFamilyName);
        AddRawIdentifier(game, "PackageIdentityName", product.Properties?.PackageIdentityName);

        foreach (var alternateId in product.AlternateIds ?? Enumerable.Empty<StoreAlternateId>())
        {
            AddRawIdentifier(game, alternateId.IdType ?? "AlternateId", alternateId.Value);
        }

        var packageFamilyNames = product.DisplaySkuAvailabilities?
            .Where(display => display.Sku?.Properties?.Packages is not null)
            .SelectMany(display => display.Sku!.Properties!.Packages!)
            .Select(package => package.PackageFamilyName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase) ??
            Enumerable.Empty<string?>();
        foreach (var packageFamilyName in packageFamilyNames)
        {
            AddRawIdentifier(game, "PackageFamilyName", packageFamilyName);
        }

        return game;
    }

    private static RejectedCatalogProduct Reject(
        MicrosoftStoreProduct product,
        ProductPlatformClassification classification,
        string reason) => new()
        {
            ProductId = product.ProductId,
            Name = product.LocalizedProperties?.FirstOrDefault()?.ProductTitle,
            Classification = classification,
            Reason = reason
        };

    private static void AddRawIdentifier(SubscriptionGame game, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var candidate = key.Trim();
        var suffix = 2;
        while (game.RawSourceIdentifiers.TryGetValue(candidate, out var existing))
        {
            if (string.Equals(existing, value!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            candidate = $"{key.Trim()}:{suffix++}";
        }

        game.RawSourceIdentifiers[candidate] = value!.Trim();
    }

    private static Uri? FindImage(IEnumerable<StoreImage>? images, IEnumerable<string> purposes)
    {
        if (images is null)
        {
            return null;
        }

        var imageList = images.ToList();
        foreach (var purpose in purposes)
        {
            var candidates = imageList
                .Where(image => string.Equals(
                    image.ImagePurpose,
                    purpose,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(image => (long)(image.Width ?? 0) * (image.Height ?? 0));
            foreach (var candidate in candidates)
            {
                var uri = NormalizationHelper.NormalizeHttpsUri(candidate.Uri);
                if (uri is not null)
                {
                    return uri;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(NullIfEmpty).FirstOrDefault(value => value is not null);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
