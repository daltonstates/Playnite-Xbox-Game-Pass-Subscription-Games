using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Playnite;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Services;

namespace SubscriptionLibraries.Services;

/// <summary>
/// Reconciles only records owned by this library plugin. Other libraries and
/// user-created games are outside this class's mutation boundary.
/// </summary>
internal sealed class PlayniteLibraryReconciler
{
    private readonly IPlayniteAPI playniteApi;
    private readonly LibraryPlugin plugin;
    private readonly ILogger logger;

    public PlayniteLibraryReconciler(
        IPlayniteAPI playniteApi,
        LibraryPlugin plugin,
        ILogger logger)
    {
        this.playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
        this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Preview(
        SubscriptionSyncResult result,
        GamePassCatalogSelection selection,
        GamePassPlanSelection plan,
        GamePassConsoleSelection consoleSelection,
        bool excludeConfirmedFreeToPlay,
        UnavailableGameHandling handling)
    {
        if (!result.IsVerified)
        {
            return "The catalog is unverified. No library changes will be applied.";
        }

        var selected = result.Games
            .Where(game => game.Availability != SubscriptionAvailability.Removed &&
                selection.Includes(plan, consoleSelection, game) &&
                (!excludeConfirmedFreeToPlay || !game.IsConfirmedFreeToPlay))
            .GroupBy(game => game.ProviderGameId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToList();
        var selectedIds = new HashSet<string>(
            selected.Select(game => game.ProviderGameId), StringComparer.OrdinalIgnoreCase);
        var owned = playniteApi.Database.Games
            .Where(game => game.PluginId == plugin.Id).ToList();
        var ownedIds = new HashSet<string>(
            owned.Where(game => !string.IsNullOrWhiteSpace(game.GameId))
                .Select(game => game.GameId), StringComparer.OrdinalIgnoreCase);
        var newCount = selected.Count(game =>
            !ownedIds.Contains(game.ProviderGameId) &&
            playniteApi.Database.ImportExclusions[
                ImportExclusionItem.GetId(game.ProviderGameId, plugin.Id)] is null);
        var markerIds = new HashSet<Guid>(playniteApi.Database.Tags
            .Where(tag => tag.Name == PlayniteGameMapper.HiddenBySelectionTag)
            .Select(tag => tag.Id));
        var unavailable = owned.Where(game =>
            string.IsNullOrWhiteSpace(game.GameId) || !selectedIds.Contains(game.GameId))
            .ToList();
        var removeCount = 0;
        var hideCount = 0;
        foreach (var game in unavailable)
        {
            var pluginHidden = game.TagIds?.Any(markerIds.Contains) == true;
            var action = UnavailableGamePolicy.Decide(
                handling, true, game.Playtime, game.IsInstalled,
                game.IsInstalling || game.IsRunning || game.IsLaunching || game.IsUninstalling,
                game.Hidden && !pluginHidden);
            if (action == UnavailableGameAction.Remove) removeCount++;
            if (action == UnavailableGameAction.Hide && !game.Hidden) hideCount++;
        }

        return $"Verified {result.CatalogTimestampUtc:u}\n" +
            $"Selected catalog: {selected.Count:N0} games\n" +
            $"New entries: {newCount:N0}\n" +
            $"Existing entries outside selection or no longer listed: {unavailable.Count:N0}\n" +
            $"Will hide: {hideCount:N0}\n" +
            $"Will permanently remove: {removeCount:N0}\n\n" +
            "Only this extension's library entries are affected. Existing Steam, Epic, " +
            "GOG, Xbox, and other libraries are untouched. Apply these changes?";
    }

    public IReadOnlyList<Game> Reconcile(
        SubscriptionSyncResult result,
        GamePassCatalogSelection selection,
        GamePassPlanSelection plan,
        GamePassConsoleSelection consoleSelection,
        bool excludeConfirmedFreeToPlay,
        UnavailableGameHandling unavailableHandling,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var catalogIsFresh = result.IsVerified;
        if (!catalogIsFresh)
        {
            MarkCatalogUnverified(cancellationToken);
            return Array.Empty<Game>();
        }

        var catalogGames = result.Games
            .Where(game => game.Availability != SubscriptionAvailability.Removed)
            .GroupBy(game => game.ProviderGameId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var activeGames = catalogGames
            .Where(game => selection.Includes(plan, consoleSelection, game) &&
                (!excludeConfirmedFreeToPlay || !game.IsConfirmedFreeToPlay))
            .Select(game => plan.Project(consoleSelection, game))
            .ToList();
        var activeIds = new HashSet<string>(
            catalogGames.Select(game => game.ProviderGameId),
            StringComparer.OrdinalIgnoreCase);
        var ownedGames = playniteApi.Database.Games
            .Where(game => game.PluginId == plugin.Id)
            .ToList();
        var ownedByProviderId = ownedGames
            .Where(game => !string.IsNullOrWhiteSpace(game.GameId))
            .ToLookup(game => game.GameId, StringComparer.OrdinalIgnoreCase);

        var addedGames = new List<Game>();
        var gamesToUpdate = new List<Game>();
        var deletedIds = new HashSet<Guid>();
        var managedHiddenTagIds = new HashSet<Guid>(playniteApi.Database.Tags
            .Where(tag => string.Equals(tag.Name,
                PlayniteGameMapper.HiddenBySelectionTag,
                StringComparison.OrdinalIgnoreCase))
            .Select(tag => tag.Id));
        var activeStatusUpdates = 0;
        var platformUpdates = 0;
        var restoredVisibilityCount = 0;
        var hiddenUnavailableCount = 0;
        var deletedUnavailableCount = 0;
        var removedStatusUpdates = 0;
        var notSelectedStatusUpdates = 0;
        var migratedLegacySources = 0;
        var excludedGames = 0;
        var importFailures = 0;

        foreach (var subscriptionGame in activeGames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existingGames = ownedByProviderId[subscriptionGame.ProviderGameId].ToList();
            if (existingGames.Count == 0)
            {
                var exclusionId = ImportExclusionItem.GetId(
                    subscriptionGame.ProviderGameId,
                    plugin.Id);
                if (playniteApi.Database.ImportExclusions[exclusionId] is not null)
                {
                    excludedGames++;
                    logger.Debug(
                        $"Excluding {subscriptionGame.Name} from {plugin.Name} import.");
                    continue;
                }

                try
                {
                    var imported = playniteApi.Database.ImportGame(
                        PlayniteGameMapper.Map(subscriptionGame, now, true,
                            result.CatalogTimestampUtc),
                        plugin);
                    addedGames.Add(imported);
                }
                catch (Exception exception)
                {
                    importFailures++;
                    logger.Error(
                        exception,
                        $"Failed to import {subscriptionGame.ProviderGameId} from {plugin.Name}.");
                }

                continue;
            }

            var desiredTags = PlayniteGameMapper.GetActiveTagNames(subscriptionGame, now);
            foreach (var existingGame in existingGames)
            {
                var visibilityChanged = RestoreManagedVisibility(
                    existingGame, managedHiddenTagIds);
                var tagsChanged = ApplyManagedTags(
                    existingGame, desiredTags, subscriptionGame.ProviderName);
                var platformsChanged = ApplyManagedPlatforms(existingGame, subscriptionGame);
                if (tagsChanged || platformsChanged || visibilityChanged)
                {
                    gamesToUpdate.Add(existingGame);
                    if (visibilityChanged)
                    {
                        restoredVisibilityCount++;
                    }
                    if (tagsChanged)
                    {
                        activeStatusUpdates++;
                    }
                    if (platformsChanged)
                    {
                        platformUpdates++;
                    }
                }
            }
        }

        foreach (var unselectedGame in catalogGames.Where(game =>
                     !selection.Includes(plan, consoleSelection, game) ||
                     (excludeConfirmedFreeToPlay && game.IsConfirmedFreeToPlay)))
        {
            var desiredTags = PlayniteGameMapper.GetNotSelectedTagNames(
                unselectedGame, plan,
                excludeConfirmedFreeToPlay && unselectedGame.IsConfirmedFreeToPlay);
            foreach (var existingGame in ownedByProviderId[unselectedGame.ProviderGameId])
            {
                var action = HandleUnavailable(
                    existingGame, desiredTags, unselectedGame.ProviderName,
                    unavailableHandling, catalogIsFresh, managedHiddenTagIds,
                    gamesToUpdate, deletedIds);
                if (action == UnavailableGameAction.Remove)
                {
                    deletedUnavailableCount++;
                }
                else
                {
                    notSelectedStatusUpdates++;
                    if (action == UnavailableGameAction.Hide)
                    {
                        hiddenUnavailableCount++;
                    }
                }
            }
        }

        var removedTags = PlayniteGameMapper.GetRemovedTagNames(GamePassConstants.ProviderName);
        foreach (var departedGame in ownedGames.Where(
                     game => string.IsNullOrWhiteSpace(game.GameId) ||
                         !activeIds.Contains(game.GameId)))
        {
            var action = HandleUnavailable(
                departedGame, removedTags, GamePassConstants.ProviderName,
                unavailableHandling, catalogIsFresh, managedHiddenTagIds,
                gamesToUpdate, deletedIds);
            if (action == UnavailableGameAction.Remove)
            {
                deletedUnavailableCount++;
            }
            else
            {
                removedStatusUpdates++;
                if (action == UnavailableGameAction.Hide)
                {
                    hiddenUnavailableCount++;
                }
            }
        }

        // Version 1.0 imported with Source = "PC Game Pass". Migrate only this
        // plugin's records still using that default; preserve custom sources.
        var legacySourceIds = new HashSet<Guid>(playniteApi.Database.Sources
            .Where(source => string.Equals(
                source.Name, "PC Game Pass", StringComparison.OrdinalIgnoreCase))
            .Select(source => source.Id));
        var legacyOwnedGames = ownedGames
            .Where(game => !deletedIds.Contains(game.Id) &&
                legacySourceIds.Contains(game.SourceId))
            .ToList();
        if (legacyOwnedGames.Count > 0)
        {
            var gamePassSourceId = playniteApi.Database.Sources.Add("Game Pass").Id;
            foreach (var legacyGame in legacyOwnedGames)
            {
                legacyGame.SourceId = gamePassSourceId;
                gamesToUpdate.Add(legacyGame);
                migratedLegacySources++;
            }
        }

        if (gamesToUpdate.Count > 0)
        {
            playniteApi.Database.Games.Update(gamesToUpdate.Distinct().ToList());
        }

        var linkUpdates = new List<Game>();
        foreach (var game in ownedGames.Where(game => !deletedIds.Contains(game.Id)))
        {
            if (ApplyVerificationLink(game, result.CatalogTimestampUtc))
            {
                linkUpdates.Add(game);
            }
        }
        if (linkUpdates.Count > 0)
        {
            playniteApi.Database.Games.Update(linkUpdates);
        }

        logger.Info(
            $"Game Pass reconciliation completed: {activeGames.Count} selected active, " +
            $"{addedGames.Count} imported, {activeStatusUpdates} active status updates, " +
            $"{platformUpdates} platform updates, " +
            $"{restoredVisibilityCount} shown again, " +
            $"{notSelectedStatusUpdates} outside selected catalog, " +
            $"{removedStatusUpdates} marked removed, {excludedGames} excluded by user, " +
            $"{hiddenUnavailableCount} unavailable hidden, " +
            $"{deletedUnavailableCount} unavailable unplayed entries deleted, " +
            $"{migratedLegacySources} legacy sources migrated, " +
            $"{importFailures} import failures.");
        return addedGames;
    }

    private void MarkCatalogUnverified(CancellationToken cancellationToken)
    {
        var owned = playniteApi.Database.Games
            .Where(game => game.PluginId == plugin.Id).ToList();
        if (owned.Count == 0)
        {
            return;
        }
        var activeTagIds = new HashSet<Guid>(playniteApi.Database.Tags
            .Where(tag => string.Equals(tag.Name, PlayniteGameMapper.ActiveAccessTag,
                StringComparison.OrdinalIgnoreCase)).Select(tag => tag.Id));
        var unverifiedId = playniteApi.Database.Tags.Add(
            PlayniteGameMapper.UnverifiedAccessTag).Id;
        var changed = new List<Game>();
        foreach (var game in owned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = game.TagIds ?? new List<Guid>();
            var updated = original.Where(id => !activeTagIds.Contains(id))
                .Concat(new[] { unverifiedId }).Distinct().ToList();
            if (updated.Count == original.Count &&
                new HashSet<Guid>(updated).SetEquals(original))
            {
                continue;
            }
            game.TagIds = updated;
            changed.Add(game);
        }
        if (changed.Count > 0)
        {
            playniteApi.Database.Games.Update(changed);
        }
        logger.Warn($"Game Pass catalog is unverified; marked {changed.Count} library entries. " +
            "No games were imported, hidden, restored, or removed.");
    }

    private static bool ApplyVerificationLink(Game game, DateTimeOffset timestamp)
    {
        var desired = PlayniteGameMapper.CreateVerificationLink(timestamp);
        game.Links ??= new System.Collections.ObjectModel.ObservableCollection<Link>();
        var existing = game.Links.Where(link => link.Name?.StartsWith(
            PlayniteGameMapper.VerifiedLinkPrefix, StringComparison.Ordinal) == true).ToList();
        if (existing.Count == 1 && existing[0].Name == desired.Name &&
            existing[0].Url == desired.Url)
        {
            return false;
        }
        foreach (var link in existing)
        {
            game.Links.Remove(link);
        }
        game.Links.Add(desired);
        return true;
    }

    private UnavailableGameAction HandleUnavailable(
        Game game,
        IReadOnlyCollection<string> baseTags,
        string providerName,
        UnavailableGameHandling handling,
        bool catalogIsFresh,
        HashSet<Guid> managedHiddenTagIds,
        List<Game> gamesToUpdate,
        HashSet<Guid> deletedIds)
    {
        var wasHiddenByPlugin = HasManagedHideMarker(game, managedHiddenTagIds);
        var wasHiddenByUser = game.Hidden && !wasHiddenByPlugin;
        var isBusy = game.IsInstalling || game.IsRunning ||
            game.IsLaunching || game.IsUninstalling;
        var action = UnavailableGamePolicy.Decide(
            handling, catalogIsFresh, game.Playtime, game.IsInstalled,
            isBusy, wasHiddenByUser);

        if (action == UnavailableGameAction.Remove)
        {
            try
            {
                if (playniteApi.Database.Games.Remove(game.Id))
                {
                    deletedIds.Add(game.Id);
                    return UnavailableGameAction.Remove;
                }

                logger.Warn($"Could not remove unavailable {plugin.Name} game {game.GameId}; hiding it.");
            }
            catch (Exception exception)
            {
                logger.Error(exception,
                    $"Could not remove unavailable {plugin.Name} game {game.GameId}; hiding it.");
            }

            action = UnavailableGameAction.Hide;
        }

        var desiredTags = baseTags.ToList();
        var visibilityChanged = false;
        if (action == UnavailableGameAction.Hide)
        {
            if (!game.Hidden)
            {
                game.Hidden = true;
                visibilityChanged = true;
            }

            if (!wasHiddenByUser)
            {
                desiredTags.Add(PlayniteGameMapper.HiddenBySelectionTag);
            }
        }
        else if (wasHiddenByPlugin && game.Hidden)
        {
            game.Hidden = false;
            visibilityChanged = true;
        }

        if (ApplyManagedTags(game, desiredTags, providerName) || visibilityChanged)
        {
            gamesToUpdate.Add(game);
        }

        return action;
    }

    private static bool RestoreManagedVisibility(
        Game game,
        HashSet<Guid> managedHiddenTagIds)
    {
        if (!HasManagedHideMarker(game, managedHiddenTagIds) || !game.Hidden)
        {
            return false;
        }

        game.Hidden = false;
        return true;
    }

    private static bool HasManagedHideMarker(
        Game game,
        HashSet<Guid> managedHiddenTagIds) =>
        game.TagIds?.Any(managedHiddenTagIds.Contains) == true;

    private bool ApplyManagedTags(
        Game game,
        IReadOnlyCollection<string> desiredTagNames,
        string providerName)
    {
        var managedNames = new HashSet<string>(
            PlayniteGameMapper.GetManagedTagNames(providerName),
            StringComparer.OrdinalIgnoreCase);
        var managedIds = new HashSet<Guid>(
            playniteApi.Database.Tags
                .Where(tag => managedNames.Contains(tag.Name))
                .Select(tag => tag.Id));
        var desiredIds = playniteApi.Database.Tags
            .Add(desiredTagNames.ToList())
            .Select(tag => tag.Id)
            .ToList();
        var updatedIds = (game.TagIds ?? new List<Guid>())
            .Where(id => !managedIds.Contains(id))
            .Concat(desiredIds)
            .Distinct()
            .ToList();
        var currentIds = game.TagIds ?? new List<Guid>();
        if (currentIds.Count == updatedIds.Count &&
            new HashSet<Guid>(currentIds).SetEquals(updatedIds))
        {
            return false;
        }

        game.TagIds = updatedIds;
        return true;
    }

    private bool ApplyManagedPlatforms(Game game, SubscriptionGame subscriptionGame)
    {
        var managedIds = new HashSet<Guid>(playniteApi.Database.Platforms
            .Where(platform =>
                string.Equals(platform.SpecificationId, "pc_windows",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(platform.Name, "Xbox console",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(platform.Name, "Xbox One",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(platform.Name, "Xbox Series X|S",
                    StringComparison.OrdinalIgnoreCase))
            .Select(platform => platform.Id));
        var desiredIds = playniteApi.Database.Platforms
            .Add(PlayniteGameMapper.GetPlatformProperties(subscriptionGame))
            .Select(platform => platform.Id)
            .ToList();
        var updatedIds = (game.PlatformIds ?? new List<Guid>())
            .Where(id => !managedIds.Contains(id))
            .Concat(desiredIds)
            .Distinct()
            .ToList();
        var currentIds = game.PlatformIds ?? new List<Guid>();
        if (currentIds.Count == updatedIds.Count &&
            new HashSet<Guid>(currentIds).SetEquals(updatedIds))
        {
            return false;
        }

        game.PlatformIds = updatedIds;
        return true;
    }
}
