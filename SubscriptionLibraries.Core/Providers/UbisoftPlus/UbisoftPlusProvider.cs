using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Services;
using SubscriptionLibraries.Core.Utilities;

namespace SubscriptionLibraries.Core.Providers.UbisoftPlus;

public enum UbisoftPlusPlanSelection
{
    Classics,
    Premium,
    AllCatalogs
}

public sealed class UbisoftPlusProvider : ISubscriptionCatalogProvider
{
    public const string ProviderId = "ubisoft-plus-pc";
    public const string ProviderName = "Ubisoft+";
    public const string ClassicsPlan = "Ubisoft+ Classics";
    public const string PremiumPlan = "Ubisoft+ Premium";
    public const string GameIdPrefix = "sub:ubisoft-plus-pc:";

    private const string CatalogUrl =
        "https://store.ubisoft.com/us/ubisoftplus/games?lang=en_US";
    private const string IndexName =
        "production__us_ubisoft__products__en_US__release_date";
    private const int MaximumProducts = 1000;

    private static readonly Regex ProductIdPattern = new(
        "^[a-f0-9]{24}$", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private readonly IHttpClientService httpClient;

    public UbisoftPlusProvider(IHttpClientService httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string Id => ProviderId;

    public string Name => ProviderName;

    // Other store regions have different indexes and have not passed the same
    // completeness check. Never import US membership for another region.
    public static bool Supports(string region, string language) =>
        string.Equals(region, "US", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase);

    public static bool Includes(UbisoftPlusPlanSelection selection, SubscriptionGame game) =>
        selection switch
        {
            UbisoftPlusPlanSelection.Classics => game.PlanPlatforms.ContainsKey(ClassicsPlan),
            UbisoftPlusPlanSelection.Premium => game.PlanPlatforms.ContainsKey(PremiumPlan),
            UbisoftPlusPlanSelection.AllCatalogs =>
                game.PlanPlatforms.ContainsKey(ClassicsPlan) ||
                game.PlanPlatforms.ContainsKey(PremiumPlan),
            _ => false
        };

    public async Task<IReadOnlyCollection<SubscriptionGame>> GetGamesAsync(
        CancellationToken cancellationToken = default) =>
        (await GetCatalogSnapshotAsync(cancellationToken).ConfigureAwait(false)).Games;

    public async Task<SubscriptionCatalogSnapshot> GetCatalogSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var catalogPage = await httpClient.GetStringAsync(
            new Uri(CatalogUrl), cancellationToken).ConfigureAwait(false);
        if (catalogPage.Length > 4_000_000 ||
            catalogPage.IndexOf("id=\"ubisoftplus-games-redesign\"", StringComparison.Ordinal) < 0 ||
            catalogPage.IndexOf("locale=\"en_US\"", StringComparison.Ordinal) < 0 ||
            catalogPage.IndexOf(IndexName, StringComparison.Ordinal) < 0)
        {
            throw new CatalogDataException("The Ubisoft+ US catalog page changed or has no US index.");
        }

        var appId = ReadPublicAttribute(catalogPage, "algolia-app-id", "^[A-Z0-9]{6,20}$");
        var searchKey = ReadPublicAttribute(catalogPage, "algolia-api-key", "^[a-f0-9]{32}$");
        var endpoint = new Uri(
            $"https://{appId}-dsn.algolia.net/1/indexes/{IndexName}?" +
            "query=&hitsPerPage=1000&facetFilters=" +
            Uri.EscapeDataString("[[\"partOfUbisoftPlus:true\"]]") +
            "&x-algolia-api-key=" + Uri.EscapeDataString(searchKey) +
            "&x-algolia-application-id=" + Uri.EscapeDataString(appId));
        var response = await httpClient.GetStringAsync(endpoint, cancellationToken)
            .ConfigureAwait(false);
        var root = SafeJson.ParseToken(response) as JObject ??
            throw new CatalogDataException("The Ubisoft+ search response was not an object.");
        var hits = root["hits"] as JArray ??
            throw new CatalogDataException("The Ubisoft+ search response has no product list.");
        var reportedCount = root.Value<int?>("nbHits");
        var pageCount = root.Value<int?>("nbPages");
        var page = root.Value<int?>("page");
        if (reportedCount is null or < 20 or > MaximumProducts ||
            reportedCount != hits.Count || pageCount != 1 || page != 0)
        {
            throw new CatalogDataException(
                "The Ubisoft+ catalog was incomplete or its pagination changed.");
        }

        var games = new List<SubscriptionGame>();
        var incompleteIds = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in hits)
        {
            if (token is not JObject product)
            {
                throw new CatalogDataException("The Ubisoft+ catalog contained a malformed product.");
            }

            var id = product.Value<string>("id") ?? string.Empty;
            if (!ProductIdPattern.IsMatch(id) || !seenIds.Add(id) ||
                product.Value<bool?>("partOfUbisoftPlus") != true)
            {
                throw new CatalogDataException("The Ubisoft+ catalog has invalid or duplicate product IDs.");
            }

            var preorder = product.Value<bool?>("preorder");
            var productType = product.Value<string>("product_type");
            var availability = product.Value<int?>("availability");
            var platform = product.Value<string>("Platform");
            var comingSoon = product.Value<string>("comingSoon");
            if (preorder is null ||
                (productType != "Games" && productType != "DLCs") ||
                availability is not (0 or 1) ||
                !string.Equals(platform, "PC (Digital)", StringComparison.OrdinalIgnoreCase) ||
                (comingSoon != "No" && comingSoon != "Yes"))
            {
                throw new CatalogDataException("The Ubisoft+ product schema or PC platform changed.");
            }

            // Preorders, DLC, and unavailable products are not current game access.
            if (preorder.Value || productType == "DLCs" ||
                availability == 0 || comingSoon == "Yes")
            {
                continue;
            }

            var gameId = GameIdPrefix + id;
            var plans = product["partofSubscriptionOffer"] is JArray labels
                ? new HashSet<string>(labels.Values<string>().Where(label => label is not null)
                    .Select(label => label!), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (plans.Any(label => label != ClassicsPlan && label != PremiumPlan))
            {
                throw new CatalogDataException("The Ubisoft+ catalog introduced an unknown plan label.");
            }
            if (!plans.Contains(ClassicsPlan) && !plans.Contains(PremiumPlan))
            {
                incompleteIds.Add(gameId);
                continue;
            }

            var title = product.Value<string>("title");
            var link = product.Value<string>("link");
            if (string.IsNullOrWhiteSpace(title) ||
                !Uri.TryCreate(link, UriKind.Absolute, out var storeUri) ||
                storeUri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(storeUri.Host, "store.ubisoft.com", StringComparison.OrdinalIgnoreCase) ||
                !storeUri.AbsolutePath.StartsWith("/us/", StringComparison.OrdinalIgnoreCase) ||
                storeUri.AbsolutePath.IndexOf(id, StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new CatalogDataException("The Ubisoft+ catalog has a game without a valid product page.");
            }

            var planPlatforms = new Dictionary<string, SubscriptionPlatforms>(
                StringComparer.OrdinalIgnoreCase);
            if (plans.Contains(ClassicsPlan)) planPlatforms[ClassicsPlan] = SubscriptionPlatforms.WindowsPc;
            if (plans.Contains(PremiumPlan)) planPlatforms[PremiumPlan] = SubscriptionPlatforms.WindowsPc;
            var image = product.Value<string>("image_link");
            games.Add(new SubscriptionGame
            {
                ProviderId = ProviderId,
                ProviderName = ProviderName,
                ProviderGameId = gameId,
                Name = WebUtility.HtmlDecode(title),
                Platform = "Windows PC",
                AccessPlatforms = SubscriptionPlatforms.WindowsPc,
                PlanPlatforms = planPlatforms,
                SubscriptionTier = string.Join(" + ", planPlatforms.Keys),
                StoreUri = storeUri,
                ImageUri = Uri.TryCreate(image, UriKind.Absolute, out var imageUri) &&
                    imageUri.Scheme == Uri.UriSchemeHttps ? imageUri : null,
                RawSourceIdentifiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["UbisoftProductId"] = id,
                    ["UbisoftMasterId"] = product.Value<string>("MasterID") ?? string.Empty
                }
            });
        }

        if (games.Count < 20 ||
            !games.Any(game => game.PlanPlatforms.ContainsKey(ClassicsPlan)) ||
            !games.Any(game => game.PlanPlatforms.ContainsKey(PremiumPlan)))
        {
            throw new CatalogDataException("The Ubisoft+ catalog has no verified Classics or Premium games.");
        }

        return new SubscriptionCatalogSnapshot
        {
            Games = games,
            ProductIds = games.Select(game => game.ProviderGameId).Concat(incompleteIds).ToArray(),
            IncompleteProductIds = incompleteIds,
            RejectMissingPlansForPreviouslyActiveGames = true,
            LeavingSoonStatusKnown = false
        };
    }

    private static string ReadPublicAttribute(string html, string attribute, string pattern)
    {
        var match = Regex.Match(html, attribute + "=\"([^\"]+)\"",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var value = match.Success ? match.Groups[1].Value : string.Empty;
        if (!Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
        {
            throw new CatalogDataException("The Ubisoft+ catalog has no valid public search configuration.");
        }

        return value;
    }
}
