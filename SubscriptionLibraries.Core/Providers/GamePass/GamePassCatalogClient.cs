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

public sealed class GamePassCatalogClient
{
    private readonly IHttpClientService httpClient;
    private readonly GamePassProviderOptions options;

    public GamePassCatalogClient(IHttpClientService httpClient, GamePassProviderOptions options)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = (options ?? throw new ArgumentNullException(nameof(options))).NormalizeAndValidate();
    }

    public async Task<GamePassCatalogIndex> GetProductIdsAsync(
        CancellationToken cancellationToken = default) =>
        await GetProductIdsAsync(options.SiglId, cancellationToken).ConfigureAwait(false);

    public async Task<GamePassCatalogIndex> GetProductIdsAsync(
        string siglId,
        CancellationToken cancellationToken = default)
    {
        return await GetProductIdsCoreAsync(
            siglId,
            options.CatalogEndpoint,
            Array.Empty<KeyValuePair<string, string>>(),
            true,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<GamePassCatalogIndex> GetPlanProductIdsAsync(
        GamePassPlanCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        return GetProductIdsCoreAsync(
            catalog.SiglId,
            options.PlanCatalogEndpoint,
            new[]
            {
                new KeyValuePair<string, string>("platformContext", catalog.PlatformContext),
                new KeyValuePair<string, string>("subscriptionContext", catalog.SubscriptionContext)
            },
            false,
            cancellationToken);
    }

    private async Task<GamePassCatalogIndex> GetProductIdsCoreAsync(
        string siglId,
        Uri endpoint,
        IReadOnlyCollection<KeyValuePair<string, string>> context,
        bool requireProducts,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(siglId))
        {
            throw new ArgumentException("A SIGL identifier is required.", nameof(siglId));
        }

        var parameters = new[]
        {
            new KeyValuePair<string, string>("id", siglId),
            new KeyValuePair<string, string>("language", options.Language),
            new KeyValuePair<string, string>("market", options.Region)
        }.Concat(context).ToArray();
        var uri = BuildUri(endpoint, parameters);

        var json = await httpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        JArray array;
        try
        {
            array = SafeJson.ParseToken(json) as JArray ??
                throw new CatalogDataException("The Game Pass SIGL response was not a JSON array.");
        }
        catch (JsonException exception)
        {
            throw new CatalogDataException("The Game Pass SIGL response was not valid JSON.", exception);
        }

        List<GamePassSiglEntry> entries;
        try
        {
            entries = array.ToObject<List<GamePassSiglEntry>>() ?? new List<GamePassSiglEntry>();
        }
        catch (JsonException exception)
        {
            throw new CatalogDataException("The Game Pass SIGL response could not be parsed.", exception);
        }

        var header = entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.SiglId));
        if (header is null || !string.Equals(header.SiglId, siglId, StringComparison.OrdinalIgnoreCase))
        {
            throw new CatalogDataException(
                "The Game Pass SIGL response did not contain the expected catalog header.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = entries
            .Select(entry => entry.Id?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id) && seen.Add(id!))
            .Cast<string>()
            .ToList();

        if (requireProducts && ids.Count == 0)
        {
            throw new CatalogDataException("The Game Pass SIGL response contained no product IDs.");
        }

        return new GamePassCatalogIndex
        {
            SiglId = header.SiglId,
            Title = header.Title,
            ProductIds = ids
        };
    }

    internal static Uri BuildUri(
        Uri endpoint,
        params KeyValuePair<string, string>[] parameters)
    {
        var builder = new UriBuilder(endpoint);
        var query = string.Join(
            "&",
            parameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
        builder.Query = query;
        return builder.Uri;
    }
}
