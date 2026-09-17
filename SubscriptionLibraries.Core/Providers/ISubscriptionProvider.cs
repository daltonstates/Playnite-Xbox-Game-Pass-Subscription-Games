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
    public bool LeavingSoonStatusKnown { get; set; } = true;

    // Providers with explicit plan labels can reject a refresh when a game that
    // was previously importable loses its tier, instead of hiding/removing it.
    public bool RejectMissingPlansForPreviouslyActiveGames { get; set; }

    public IReadOnlyCollection<SubscriptionGame> Games { get; set; } =
        Array.Empty<SubscriptionGame>();

    public IReadOnlyCollection<string> ProductIds { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<string> IncompleteProductIds { get; set; } =
        Array.Empty<string>();

    // Fresh feed membership is needed when product metadata is too incomplete
    // to normalize a game in the current catalog response.
    public IReadOnlyDictionary<string, Dictionary<string, SubscriptionPlatforms>>
        LeavingSoonPlanPlatformsByProductId { get; set; } =
        new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>(
            StringComparer.OrdinalIgnoreCase);

    // Source-declared membership can remain known even when product metadata
    // cannot be normalized. Used to avoid preserving a stale platform claim.
    public IReadOnlyDictionary<string, SubscriptionPlatforms> DeclaredPlatformsByProductId { get; set; } =
        new Dictionary<string, SubscriptionPlatforms>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Dictionary<string, SubscriptionPlatforms>>
        DeclaredPlanPlatformsByProductId { get; set; } =
        new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>(
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Dictionary<string, XboxConsoleGenerations>>
        DeclaredPlanGenerationsByProductId { get; set; } =
        new Dictionary<string, Dictionary<string, XboxConsoleGenerations>>(
            StringComparer.OrdinalIgnoreCase);
}
