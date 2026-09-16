using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SubscriptionLibraries.Core.Models;

namespace SubscriptionLibraries.Core.Providers;

public interface ISubscriptionProvider
{
    string Id { get; }

    string Name { get; }

    Task<IReadOnlyCollection<SubscriptionGame>> GetGamesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional richer provider contract used when a source can distinguish full
/// catalog membership from products whose metadata could not be normalized.
/// </summary>
public interface ISubscriptionCatalogProvider : ISubscriptionProvider
{
    Task<SubscriptionCatalogSnapshot> GetCatalogSnapshotAsync(
        CancellationToken cancellationToken = default);
}

public sealed class SubscriptionCatalogSnapshot
{
    public IReadOnlyCollection<SubscriptionGame> Games { get; set; } =
        Array.Empty<SubscriptionGame>();

    public IReadOnlyCollection<string> ProductIds { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<string> IncompleteProductIds { get; set; } =
        Array.Empty<string>();
}
