using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Providers;
using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Utilities;

namespace SubscriptionLibraries.Core.Services;

public sealed class SubscriptionSyncOptions
{
    public string Region { get; set; } = "US";

    public string Language { get; set; } = "en-US";

    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromHours(24);

    public bool ForceRefresh { get; set; }

    public bool RefreshFromNetwork { get; set; } = true;

    public string? CatalogConfigurationKey { get; set; }
}

public enum SubscriptionCatalogSource
{
    Live,
    FreshCache,
    StaleCache
}

public sealed class SubscriptionSyncResult
{
    public bool IsVerified => !UsedFallback && Source != SubscriptionCatalogSource.StaleCache;

    public IReadOnlyList<SubscriptionGame> Games { get; set; } = Array.Empty<SubscriptionGame>();

    public SubscriptionCatalogSource Source { get; set; }

    public DateTimeOffset CatalogTimestampUtc { get; set; }

    public bool UsedFallback { get; set; }

    public string? Warning { get; set; }

    public CatalogDiagnostics? Diagnostics { get; set; }

    public int RemovedGameCount { get; set; }

    public IReadOnlyList<SubscriptionGame> NewlyLeavingSoonGames { get; set; } =
        Array.Empty<SubscriptionGame>();
}

public sealed class SubscriptionSyncService
{
    private const int MaximumRemovedHistory = 2000;
    private readonly CatalogCacheService cacheService;
    private readonly ISubscriptionLogger logger;
    private readonly IClock clock;

    public SubscriptionSyncService(
        CatalogCacheService cacheService,
        ISubscriptionLogger? logger = null,
        IClock? clock = null)
    {
        this.cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        this.logger = logger ?? NullSubscriptionLogger.Instance;
        this.clock = clock ?? SystemClock.Instance;
    }

    public async Task<SubscriptionSyncResult> SynchronizeAsync(
        ISubscriptionProvider provider,
        SubscriptionSyncOptions options,
        CancellationToken cancellationToken = default)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        ValidateOptions(options);
        var cache = cacheService.TryRead(
            provider.Id,
            options.Region,
            options.Language,
            options.CacheLifetime,
            options.CatalogConfigurationKey);

        if (!options.ForceRefresh && cache is not null && (cache.IsFresh || !options.RefreshFromNetwork))
        {
            return FromCache(provider, cache, false, null);
        }

        try
        {
            var snapshot = provider is ISubscriptionCatalogProvider catalogProvider
                ? await catalogProvider.GetCatalogSnapshotAsync(cancellationToken).ConfigureAwait(false)
                : new SubscriptionCatalogSnapshot
                {
                    Games = await provider.GetGamesAsync(cancellationToken).ConfigureAwait(false)
                };
            var games = snapshot.Games
                .Where(game => game is not null && !string.IsNullOrWhiteSpace(game.ProviderGameId))
                .GroupBy(game => game.ProviderGameId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (games.Count == 0)
            {
                throw new CatalogDataException($"{provider.Name} returned an empty catalog.");
            }

            GuardAgainstCatastrophicCatalogDrop(provider, cache, games.Count);

            var now = clock.UtcNow;
            var previousActive = cache?.Envelope.Games
                .GroupBy(game => game.ProviderGameId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase) ??
                new Dictionary<string, SubscriptionGame>(StringComparer.OrdinalIgnoreCase);
            var previousRemoved = cache?.Envelope.RemovedGames ?? new List<SubscriptionGame>();

            var incompleteIds = new HashSet<string>(
                snapshot.IncompleteProductIds.Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            var returnedIds = new HashSet<string>(
                games.Select(game => game.ProviderGameId),
                StringComparer.OrdinalIgnoreCase);
            var preservedIncompleteCount = 0;
            foreach (var incompleteId in incompleteIds)
            {
                if (!previousActive.TryGetValue(incompleteId, out var previousIncomplete))
                {
                    continue;
                }

                var hasDeclaredMembership = snapshot.DeclaredPlatformsByProductId.TryGetValue(
                    incompleteId, out var declaredMembership);
                var preservedPlatforms = hasDeclaredMembership
                    ? (previousIncomplete.AccessPlatforms & declaredMembership) |
                      (declaredMembership & SubscriptionPlatforms.XboxConsole)
                    : previousIncomplete.AccessPlatforms;

                if (returnedIds.Contains(incompleteId))
                {
                    if (hasDeclaredMembership)
                    {
                        var partial = games.First(game =>
                            string.Equals(game.ProviderGameId, incompleteId,
                                StringComparison.OrdinalIgnoreCase));
                        partial.AccessPlatforms |= preservedPlatforms;
                        partial.Platform = SubscriptionPlatformNames.Format(partial.AccessPlatforms);
                        UpdateDeclaredPlans(partial, incompleteId, snapshot);
                    }

                    continue;
                }

                if (hasDeclaredMembership && preservedPlatforms == SubscriptionPlatforms.None)
                {
                    continue;
                }

                var preserved = Clone(previousIncomplete);
                if (hasDeclaredMembership)
                {
                    preserved.AccessPlatforms = preservedPlatforms;
                    preserved.Platform = SubscriptionPlatformNames.Format(preservedPlatforms);
                    UpdateDeclaredPlans(preserved, incompleteId, snapshot);
                }

                preserved.Availability = SubscriptionAvailability.Active;
                preserved.LeavingDate = null;
                games.Add(preserved);
                returnedIds.Add(incompleteId);
                preservedIncompleteCount++;
            }

            if (preservedIncompleteCount > 0)
            {
                logger.Warn(
                    $"Preserved {preservedIncompleteCount} previously active {provider.Name} games " +
                    "whose current catalog metadata was incomplete.");
            }

            GuardAgainstCatastrophicMembershipDrop(provider, cache, games);

            var currentIds = new HashSet<string>(
                games.Select(game => game.ProviderGameId),
                StringComparer.OrdinalIgnoreCase);

            foreach (var game in games)
            {
                if (!snapshot.LeavingSoonStatusKnown &&
                    previousActive.TryGetValue(game.ProviderGameId, out var previousStatus) &&
                    previousStatus.Availability == SubscriptionAvailability.LeavingSoon)
                {
                    game.Availability = SubscriptionAvailability.LeavingSoon;
                    game.LeavingSoonPlanPlatforms = new Dictionary<string, SubscriptionPlatforms>(
                        previousStatus.LeavingSoonPlanPlatforms ??
                            new Dictionary<string, SubscriptionPlatforms>(),
                        StringComparer.OrdinalIgnoreCase);
                }
                if (game.Availability != SubscriptionAvailability.LeavingSoon)
                {
                    game.LeavingDate = null;
                }
                if (previousActive.TryGetValue(game.ProviderGameId, out var previous))
                {
                    game.DateAdded = previous.DateAdded;
                }
                else if (cache is not null)
                {
                    game.DateAdded = now;
                }
            }

            var removedById = previousRemoved
                .Where(game => !string.IsNullOrWhiteSpace(game.ProviderGameId))
                .GroupBy(game => game.ProviderGameId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            foreach (var previous in previousActive.Values.Where(
                         game => !currentIds.Contains(game.ProviderGameId)))
            {
                var removed = Clone(previous);
                removed.Availability = SubscriptionAvailability.Removed;
                removed.LeavingDate ??= now;
                removedById[removed.ProviderGameId] = removed;
            }

            foreach (var currentId in currentIds)
            {
                removedById.Remove(currentId);
            }

            var removedGames = removedById.Values
                .OrderByDescending(game => game.LeavingDate)
                .Take(MaximumRemovedHistory)
                .ToList();
            var newlyLeavingSoonGames = cache is null || !snapshot.LeavingSoonStatusKnown
                ? new List<SubscriptionGame>()
                : games.Where(game =>
                        game.Availability == SubscriptionAvailability.LeavingSoon &&
                        (!previousActive.TryGetValue(game.ProviderGameId, out var prior) ||
                         prior.Availability != SubscriptionAvailability.LeavingSoon ||
                         !SameLeavingMembership(prior, game)))
                    .OrderBy(game => game.Name).ToList();
            var diagnostics = (provider as GamePassProvider)?.LastDiagnostics;
            var envelope = new CatalogCacheEnvelope
            {
                ProviderId = provider.Id,
                Region = options.Region,
                Language = options.Language,
                CatalogConfigurationKey = options.CatalogConfigurationKey,
                CachedAtUtc = now,
                ProductIds = snapshot.ProductIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToList(),
                Games = games,
                RemovedGames = removedGames,
                Diagnostics = diagnostics
            };
            if (envelope.ProductIds.Count == 0)
            {
                envelope.ProductIds = games.Select(game => game.ProviderGameId).ToList();
            }
            await cacheService.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);

            return new SubscriptionSyncResult
            {
                Games = games,
                Source = SubscriptionCatalogSource.Live,
                CatalogTimestampUtc = now,
                Diagnostics = diagnostics,
                RemovedGameCount = removedGames.Count,
                NewlyLeavingSoonGames = newlyLeavingSoonGames
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (cache is null)
            {
                throw;
            }

            var warning = $"{provider.Name} synchronization failed; using cached catalog from " +
                $"{cache.Envelope.CachedAtUtc:u}. {exception.Message}";
            logger.Warn(exception, warning);
            return FromCache(provider, cache, true, warning);
        }
    }

    private SubscriptionSyncResult FromCache(
        ISubscriptionProvider provider,
        CatalogCacheReadResult cache,
        bool usedFallback,
        string? warning)
    {
        logger.Info($"Using cached {provider.Name} catalog from {cache.Envelope.CachedAtUtc:u}.");
        return new SubscriptionSyncResult
        {
            Games = cache.Envelope.Games,
            Source = cache.IsFresh ? SubscriptionCatalogSource.FreshCache : SubscriptionCatalogSource.StaleCache,
            CatalogTimestampUtc = cache.Envelope.CachedAtUtc,
            UsedFallback = usedFallback,
            Warning = warning,
            Diagnostics = cache.Envelope.Diagnostics,
            RemovedGameCount = cache.Envelope.RemovedGames.Count
        };
    }

    private static void GuardAgainstCatastrophicCatalogDrop(
        ISubscriptionProvider provider,
        CatalogCacheReadResult? cache,
        int currentCount)
    {
        var previousCount = cache?.Envelope.Games.Count ?? 0;
        if (previousCount >= 20 && currentCount * 2L < previousCount)
        {
            throw new CatalogDataException(
                $"{provider.Name} unexpectedly dropped from {previousCount} to {currentCount} games; " +
                "the previous cache was preserved.");
        }
    }

    private static void GuardAgainstCatastrophicMembershipDrop(
        ISubscriptionProvider provider,
        CatalogCacheReadResult? cache,
        IReadOnlyCollection<SubscriptionGame> games)
    {
        if (cache is null)
        {
            return;
        }

        var currentCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            AddMembershipCounts(currentCounts, game);
        }

        var previousCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in cache.Envelope.Games)
        {
            AddMembershipCounts(previousCounts, game);
        }

        foreach (var previous in previousCounts)
        {
            currentCounts.TryGetValue(previous.Key, out var currentCount);
            if ((previous.Value >= 20 && currentCount * 2L < previous.Value) ||
                (previous.Value >= 5 && currentCount == 0))
            {
                throw new CatalogDataException(
                    $"{provider.Name} {previous.Key} membership unexpectedly dropped " +
                    $"from {previous.Value} to {currentCount}; the previous cache was preserved.");
            }
        }
    }

    private static void AddMembershipCounts(
        Dictionary<string, int> counts, SubscriptionGame game)
    {
        if ((game.AccessPlatforms & SubscriptionPlatforms.WindowsPc) != 0)
        {
            Increment(counts, "all catalogs PC");
        }
        if ((game.AccessPlatforms & SubscriptionPlatforms.XboxConsole) != 0)
        {
            Increment(counts, "all catalogs Xbox");
            if ((game.XboxGenerations & XboxConsoleGenerations.XboxOne) != 0)
            {
                Increment(counts, "all catalogs Xbox One");
            }
            if ((game.XboxGenerations & XboxConsoleGenerations.SeriesXorS) != 0)
            {
                Increment(counts, "all catalogs Xbox Series X|S");
            }
        }

        var platformsByPlan = game.PlanPlatforms;
        var generationsByPlan = game.PlanXboxGenerations;
        if (platformsByPlan is null)
        {
            return;
        }

        foreach (var plan in platformsByPlan)
        {
            if ((plan.Value & SubscriptionPlatforms.WindowsPc) != 0)
            {
                Increment(counts, $"{plan.Key} PC");
            }
            if ((plan.Value & SubscriptionPlatforms.XboxConsole) == 0)
            {
                continue;
            }

            Increment(counts, $"{plan.Key} Xbox");
            if (generationsByPlan is null ||
                !generationsByPlan.TryGetValue(plan.Key, out var generations))
            {
                continue;
            }
            if ((generations & XboxConsoleGenerations.XboxOne) != 0)
            {
                Increment(counts, $"{plan.Key} Xbox One");
            }
            if ((generations & XboxConsoleGenerations.SeriesXorS) != 0)
            {
                Increment(counts, $"{plan.Key} Xbox Series X|S");
            }
        }
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out var count);
        counts[key] = count + 1;
    }

    private static void ValidateOptions(SubscriptionSyncOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Region) || string.IsNullOrWhiteSpace(options.Language))
        {
            throw new ArgumentException("Region and language are required.", nameof(options));
        }

        if (options.CacheLifetime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.CacheLifetime));
        }
    }

    private static SubscriptionGame Clone(SubscriptionGame game) => new()
    {
        ProviderId = game.ProviderId,
        ProviderName = game.ProviderName,
        ProviderGameId = game.ProviderGameId,
        Name = game.Name,
        MicrosoftProductId = game.MicrosoftProductId,
        Platform = game.Platform,
        AccessPlatforms = game.AccessPlatforms,
        XboxGenerations = game.XboxGenerations,
        PlanXboxGenerations = game.PlanXboxGenerations is null
            ? new Dictionary<string, XboxConsoleGenerations>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, XboxConsoleGenerations>(
                game.PlanXboxGenerations, StringComparer.OrdinalIgnoreCase),
        PlanPlatforms = game.PlanPlatforms is null
            ? new Dictionary<string, SubscriptionPlatforms>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SubscriptionPlatforms>(
                game.PlanPlatforms, StringComparer.OrdinalIgnoreCase),
        SubscriptionTier = game.SubscriptionTier,
        StoreUri = game.StoreUri,
        ImageUri = game.ImageUri,
        BackgroundImageUri = game.BackgroundImageUri,
        Description = game.Description,
        Publisher = game.Publisher,
        Developer = game.Developer,
        ReleaseDate = game.ReleaseDate,
        DateAdded = game.DateAdded,
        LeavingDate = game.LeavingDate,
        LeavingSoonPlanPlatforms = game.LeavingSoonPlanPlatforms is null
            ? new Dictionary<string, SubscriptionPlatforms>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SubscriptionPlatforms>(
                game.LeavingSoonPlanPlatforms, StringComparer.OrdinalIgnoreCase),
        IsConfirmedFreeToPlay = game.IsConfirmedFreeToPlay,
        Availability = game.Availability,
        RawSourceIdentifiers = game.RawSourceIdentifiers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(
                game.RawSourceIdentifiers,
                StringComparer.OrdinalIgnoreCase)
    };

    private static void UpdateDeclaredPlans(
        SubscriptionGame game,
        string productId,
        SubscriptionCatalogSnapshot snapshot)
    {
        if (!snapshot.DeclaredPlanPlatformsByProductId.TryGetValue(
                productId, out var declaredPlans))
        {
            game.PlanPlatforms = new Dictionary<string, SubscriptionPlatforms>(
                StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            game.PlanPlatforms = declaredPlans
                .Select(pair => new KeyValuePair<string, SubscriptionPlatforms>(
                    pair.Key, pair.Value & game.AccessPlatforms))
                .Where(pair => pair.Value != SubscriptionPlatforms.None)
                .ToDictionary(pair => pair.Key, pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
        }
        if (snapshot.DeclaredPlanGenerationsByProductId.TryGetValue(
                productId, out var declaredGenerations))
        {
            game.PlanXboxGenerations = new Dictionary<string, XboxConsoleGenerations>(
                declaredGenerations, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            game.PlanXboxGenerations = new Dictionary<string, XboxConsoleGenerations>(
                StringComparer.OrdinalIgnoreCase);
        }
        game.XboxGenerations = game.PlanXboxGenerations.Values.Aggregate(
            XboxConsoleGenerations.None, (current, next) => current | next);
    }

    private static bool SameLeavingMembership(SubscriptionGame previous, SubscriptionGame current)
    {
        var before = previous.LeavingSoonPlanPlatforms ??
            new Dictionary<string, SubscriptionPlatforms>();
        var after = current.LeavingSoonPlanPlatforms ??
            new Dictionary<string, SubscriptionPlatforms>();
        return before.Count == after.Count && before.All(pair =>
            after.TryGetValue(pair.Key, out var platforms) && platforms == pair.Value);
    }
}
