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
        var pcIds = new HashSet<string>(result.PcProductIds, StringComparer.OrdinalIgnoreCase);
        var consoleIds = new HashSet<string>(result.ConsoleProductIds, StringComparer.OrdinalIgnoreCase);
        return new SubscriptionCatalogSnapshot
        {
            Games = result.Games,
            LeavingSoonStatusKnown = result.LeavingSoonStatusKnown,
            ProductIds = result.ProductIds,
            IncompleteProductIds = result.IncompleteProductIds,
            DeclaredPlatformsByProductId = result.ProductIds.ToDictionary(
                id => id,
                id =>
                    (pcIds.Contains(id)
                        ? SubscriptionPlatforms.WindowsPc
                        : SubscriptionPlatforms.None) |
                    (consoleIds.Contains(id)
                        ? SubscriptionPlatforms.XboxConsole
                        : SubscriptionPlatforms.None),
                StringComparer.OrdinalIgnoreCase),
            DeclaredPlanPlatformsByProductId = result.DeclaredPlanPlatformsByProductId,
            DeclaredPlanGenerationsByProductId = result.DeclaredPlanGenerationsByProductId
        };
    }

    public async Task<GamePassCatalogResult> GetCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        logger.Info("Starting PC and Xbox console Game Pass catalog synchronization.");
        var pcIndex = await catalogClient.GetProductIdsAsync(
            options.SiglId, cancellationToken).ConfigureAwait(false);
        var consoleIndex = await catalogClient.GetProductIdsAsync(
            options.ConsoleSiglId, cancellationToken).ConfigureAwait(false);
        var pcIds = new HashSet<string>(pcIndex.ProductIds, StringComparer.OrdinalIgnoreCase);
        var consoleIds = new HashSet<string>(consoleIndex.ProductIds, StringComparer.OrdinalIgnoreCase);
        var planPlatformsByProductId = new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>(
            StringComparer.OrdinalIgnoreCase);
        var planGenerationsByProductId =
            new Dictionary<string, Dictionary<string, XboxConsoleGenerations>>(
                StringComparer.OrdinalIgnoreCase);
        var planCatalogProductCount = 0;
        foreach (var catalog in GamePassConstants.PlanCatalogs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = await catalogClient.GetPlanProductIdsAsync(
                catalog, cancellationToken).ConfigureAwait(false);
            logger.Info(
                $"Retrieved {index.ProductIds.Count} {catalog.Plan.DisplayName()} " +
                $"{SubscriptionPlatformNames.Format(catalog.Platform)} catalog IDs.");
            planCatalogProductCount += index.ProductIds.Count;
            foreach (var id in index.ProductIds)
            {
                if (!planPlatformsByProductId.TryGetValue(id, out var planPlatforms))
                {
                    planPlatforms = new Dictionary<string, SubscriptionPlatforms>(
                        StringComparer.OrdinalIgnoreCase);
                    planPlatformsByProductId.Add(id, planPlatforms);
                }

                var planKey = catalog.Plan.Key();
                planPlatforms.TryGetValue(planKey, out var existing);
                planPlatforms[planKey] = existing | catalog.Platform;
                if (catalog.ConsoleGeneration != XboxConsoleGenerations.None)
                {
                    if (!planGenerationsByProductId.TryGetValue(id, out var generations))
                    {
                        generations = new Dictionary<string, XboxConsoleGenerations>(
                            StringComparer.OrdinalIgnoreCase);
                        planGenerationsByProductId.Add(id, generations);
                    }
                    generations.TryGetValue(planKey, out var existingGeneration);
                    generations[planKey] = existingGeneration | catalog.ConsoleGeneration;
                }
                if (catalog.Platform == SubscriptionPlatforms.WindowsPc)
                {
                    pcIds.Add(id);
                }
                else
                {
                    consoleIds.Add(id);
                }
            }
        }

        if (planCatalogProductCount == 0)
        {
            throw new CatalogDataException(
                "Microsoft returned no plan-specific Game Pass catalog memberships.");
        }

        var leavingSoonPcIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var leavingSoonConsoleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var leavingSoonStatusKnown = false;
        try
        {
            var leavingPc = await catalogClient.GetPlanProductIdsAsync(
                GamePassConstants.LeavingSoonPcCatalog, cancellationToken).ConfigureAwait(false);
            var leavingConsole = await catalogClient.GetPlanProductIdsAsync(
                GamePassConstants.LeavingSoonConsoleCatalog, cancellationToken).ConfigureAwait(false);
            leavingSoonPcIds.UnionWith(leavingPc.ProductIds);
            leavingSoonConsoleIds.UnionWith(leavingConsole.ProductIds);
            leavingSoonStatusKnown = true;
            logger.Info($"Retrieved {leavingSoonPcIds.Count} PC and " +
                $"{leavingSoonConsoleIds.Count} console Game Pass leaving-soon product IDs.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Warn(exception, "Leaving-soon catalog unavailable; prior warnings will be preserved.");
        }

        var productIds = pcIds.Concat(consoleIds)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        logger.Info(
            $"Retrieved {pcIds.Count} PC and {consoleIds.Count} Xbox console Game Pass product IDs " +
            $"({productIds.Count} distinct).");

        var products = await storeClient
            .GetProductsAsync(productIds, cancellationToken)
            .ConfigureAwait(false);
        logger.Info($"Resolved {products.Count} Microsoft Store products.");

        if (products.Count == 0)
        {
            throw new CatalogDataException(
                "Microsoft returned no product metadata for the non-empty Game Pass catalog.");
        }

        var requestedIds = new HashSet<string>(productIds, StringComparer.OrdinalIgnoreCase);
        var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var incompleteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unclassifiedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var games = new List<SubscriptionGame>();
        var rejected = new List<RejectedCatalogProduct>();
        var nonPcCount = 0;

        foreach (var product in products)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var productId = product.ProductId?.Trim();
            if (string.IsNullOrWhiteSpace(productId))
            {
                unclassifiedIds.Add("<missing product id>");
                rejected.Add(Reject(product, ProductPlatformClassification.Unknown, "Product metadata had no ProductId."));
                continue;
            }

            if (!requestedIds.Contains(productId!) || !resolvedIds.Add(productId!))
            {
                continue;
            }

            var accessPlatforms = SubscriptionPlatforms.None;
            if (consoleIds.Contains(productId!))
            {
                accessPlatforms |= SubscriptionPlatforms.XboxConsole;
            }

            if (pcIds.Contains(productId!))
            {
                var classification = platformClassifier.Classify(product);
                if (classification.Classification == ProductPlatformClassification.WindowsPc)
                {
                    accessPlatforms |= SubscriptionPlatforms.WindowsPc;
                }
                else
                {
                    if (classification.Classification == ProductPlatformClassification.NonPc)
                    {
                        nonPcCount++;
                    }
                    else
                    {
                        unclassifiedIds.Add(productId!);
                        incompleteIds.Add(productId!);
                    }

                    rejected.Add(Reject(product, classification.Classification, classification.Evidence));
                }
            }

            if (accessPlatforms == SubscriptionPlatforms.None)
            {
                continue;
            }

            planPlatformsByProductId.TryGetValue(productId!, out var declaredPlans);
            planGenerationsByProductId.TryGetValue(productId!, out var declaredGenerations);
            var game = Normalize(product, accessPlatforms, declaredPlans,
                declaredGenerations);
            if (game is null)
            {
                unclassifiedIds.Add(productId!);
                incompleteIds.Add(productId!);
                rejected.Add(Reject(
                    product,
                    ProductPlatformClassification.Unknown,
                    "Required normalized metadata (ProductId or title) was missing."));
                continue;
            }

            if (importedIds.Add(game.ProviderGameId))
            {
                if (leavingSoonPcIds.Contains(game.ProviderGameId))
                {
                    game.LeavingSoonPlanPlatforms[GamePassPlanSelection.PcGamePass.Key()] =
                        SubscriptionPlatforms.WindowsPc;
                }
                if (leavingSoonConsoleIds.Contains(game.ProviderGameId))
                {
                    game.LeavingSoonPlanPlatforms[GamePassPlanSelection.Ultimate.Key()] =
                        SubscriptionPlatforms.XboxConsole;
                }
                if (game.LeavingSoonPlanPlatforms.Count > 0)
                {
                    game.Availability = SubscriptionAvailability.LeavingSoon;
                }
                games.Add(game);
            }
        }

        incompleteIds.UnionWith(requestedIds.Where(id => !resolvedIds.Contains(id)));

        var diagnostics = new CatalogDiagnostics
        {
            TotalCatalogIds = productIds.Count,
            PcCatalogIds = pcIds.Count,
            ConsoleCatalogIds = consoleIds.Count,
            ProductsSuccessfullyResolved = resolvedIds.Count,
            ProductsIdentifiedAsPc = games.Count(game =>
                (game.AccessPlatforms & SubscriptionPlatforms.WindowsPc) != 0),
            ProductsIdentifiedAsConsole = games.Count(game =>
                (game.AccessPlatforms & SubscriptionPlatforms.XboxConsole) != 0),
            ProductsInBothCatalogs = games.Count(game =>
                game.AccessPlatforms == (SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole)),
            ProductsRejectedAsNonPc = nonPcCount,
            ProductsThatCouldNotBeClassified = unclassifiedIds.Count,
            ProductIdsWithoutMetadata = Math.Max(0, productIds.Count - resolvedIds.Count)
        };
        LastDiagnostics = diagnostics;

        logger.Info($"Filtered {nonPcCount} products that are not Windows PC titles.");
        if (unclassifiedIds.Count > 0 || diagnostics.ProductIdsWithoutMetadata > 0)
        {
            logger.Warn(
                $"Could not classify {unclassifiedIds.Count} resolved products; " +
                $"{diagnostics.ProductIdsWithoutMetadata} product IDs had no metadata.");
        }

        logger.Info($"Resolved {games.Count} Game Pass games across PC and Xbox console.");
        if (games.Count == 0)
        {
            throw new CatalogDataException(
                "The catalogs contained no games with supported PC or Xbox console access.");
        }

        return new GamePassCatalogResult
        {
            LeavingSoonStatusKnown = leavingSoonStatusKnown,
            PcProductIds = pcIds.ToList(),
            ConsoleProductIds = consoleIds.ToList(),
            ProductIds = productIds,
            DeclaredPlanPlatformsByProductId = planPlatformsByProductId,
            DeclaredPlanGenerationsByProductId = planGenerationsByProductId,
            IncompleteProductIds = incompleteIds.ToList(),
            Games = games,
            RejectedProducts = rejected,
            Diagnostics = diagnostics
        };
    }

    private SubscriptionGame? Normalize(
        MicrosoftStoreProduct product,
        SubscriptionPlatforms accessPlatforms,
        IReadOnlyDictionary<string, SubscriptionPlatforms>? declaredPlans,
        IReadOnlyDictionary<string, XboxConsoleGenerations>? declaredGenerations)
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
            Platform = SubscriptionPlatformNames.Format(accessPlatforms),
            AccessPlatforms = accessPlatforms,
            XboxGenerations = declaredGenerations?.Values.Aggregate(
                XboxConsoleGenerations.None, (current, next) => current | next) ??
                XboxConsoleGenerations.None,
            PlanXboxGenerations = declaredGenerations is null
                ? new Dictionary<string, XboxConsoleGenerations>(StringComparer.OrdinalIgnoreCase)
                : declaredGenerations.ToDictionary(pair => pair.Key, pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase),
            PlanPlatforms = declaredPlans is null
                ? new Dictionary<string, SubscriptionPlatforms>(StringComparer.OrdinalIgnoreCase)
                : declaredPlans
                    .Select(pair => new KeyValuePair<string, SubscriptionPlatforms>(
                        pair.Key, pair.Value & accessPlatforms))
                    .Where(pair => pair.Value != SubscriptionPlatforms.None)
                    .ToDictionary(pair => pair.Key, pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase),
            SubscriptionTier = GamePassConstants.SubscriptionTier,
            Availability = SubscriptionAvailability.Active,
            IsConfirmedFreeToPlay = GamePassStorePricing.IsConfirmedFreeToPlay(product),
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
