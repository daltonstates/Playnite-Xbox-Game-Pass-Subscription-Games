using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SubscriptionLibraries.Core.Services;
using SubscriptionLibraries.Core.Utilities;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public sealed class MicrosoftStoreCatalogClient
{
    private readonly IHttpClientService httpClient;
    private readonly GamePassProviderOptions options;
    private readonly ISubscriptionLogger logger;

    public MicrosoftStoreCatalogClient(
        IHttpClientService httpClient,
        GamePassProviderOptions options,
        ISubscriptionLogger? logger = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = (options ?? throw new ArgumentNullException(nameof(options))).NormalizeAndValidate();
        this.logger = logger ?? NullSubscriptionLogger.Instance;
    }

    public async Task<IReadOnlyList<MicrosoftStoreProduct>> GetProductsAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken = default)
    {
        if (productIds is null)
        {
            throw new ArgumentNullException(nameof(productIds));
        }

        var ids = productIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<MicrosoftStoreProduct>();
        }

        var products = new List<MicrosoftStoreProduct>(ids.Length);
        for (var start = 0; start < ids.Length; start += options.ProductBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = ids.Skip(start).Take(options.ProductBatchSize).ToArray();
            var uri = BuildProductsUri(batch);
            var json = await httpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            var batchProducts = ParseProducts(json);
            products.AddRange(batchProducts);
            logger.Debug(
                $"Resolved Microsoft Store product batch {start + 1}-" +
                $"{start + batch.Length} of {ids.Length} ({batchProducts.Count} returned).");
        }

        return products;
    }

    private Uri BuildProductsUri(IReadOnlyCollection<string> ids)
    {
        var builder = new UriBuilder(options.DisplayCatalogEndpoint);
        var encodedIds = string.Join(",", ids.Select(Uri.EscapeDataString));
        builder.Query =
            $"bigIds={encodedIds}&market={Uri.EscapeDataString(options.Region)}" +
            $"&languages={Uri.EscapeDataString(options.Language)}";
        return builder.Uri;
    }

    internal static IReadOnlyList<MicrosoftStoreProduct> ParseProducts(string json)
    {
        JObject root;
        try
        {
            root = SafeJson.ParseToken(json) as JObject ??
                throw new CatalogDataException("The Microsoft display catalog response was not a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new CatalogDataException(
                "The Microsoft display catalog response was not valid JSON.",
                exception);
        }

        var productsToken = root.GetValue("Products", StringComparison.OrdinalIgnoreCase);
        if (productsToken is not JArray)
        {
            throw new CatalogDataException(
                "The Microsoft display catalog response did not contain a Products array.");
        }

        try
        {
            var response = root.ToObject<MicrosoftStoreProductsResponse>();
            return response?.Products ?? new List<MicrosoftStoreProduct>();
        }
        catch (JsonException exception)
        {
            throw new CatalogDataException(
                "The Microsoft display catalog Products array could not be parsed.",
                exception);
        }
    }
}
