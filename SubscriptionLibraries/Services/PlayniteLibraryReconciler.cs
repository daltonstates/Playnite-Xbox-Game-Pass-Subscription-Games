using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Playnite;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SubscriptionLibraries.Core.Models;
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

    public IReadOnlyList<Game> Reconcile(
        SubscriptionSyncResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var activeGames = result.Games
            .Where(game => game.Availability != SubscriptionAvailability.Removed)
            .GroupBy(game => game.ProviderGameId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var activeIds = new HashSet<string>(
            activeGames.Select(game => game.ProviderGameId),
            StringComparer.OrdinalIgnoreCase);
        var ownedGames = playniteApi.Database.Games
            .Where(game => game.PluginId == plugin.Id)
            .ToList();
        var ownedByProviderId = ownedGames
            .Where(game => !string.IsNullOrWhiteSpace(game.GameId))
            .ToLookup(game => game.GameId, StringComparer.OrdinalIgnoreCase);

        var addedGames = new List<Game>();
        var gamesToUpdate = new List<Game>();
        var activeStatusUpdates = 0;
        var removedStatusUpdates = 0;
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
                        PlayniteGameMapper.Map(subscriptionGame, now),
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
                if (ApplyManagedTags(existingGame, desiredTags, subscriptionGame.ProviderName))
                {
                    gamesToUpdate.Add(existingGame);
                    activeStatusUpdates++;
                }
            }
        }

        var removedTags = PlayniteGameMapper.GetRemovedTagNames(plugin.Name);
        foreach (var departedGame in ownedGames.Where(
                     game => string.IsNullOrWhiteSpace(game.GameId) ||
                         !activeIds.Contains(game.GameId)))
        {
            if (ApplyManagedTags(departedGame, removedTags, plugin.Name))
            {
                gamesToUpdate.Add(departedGame);
                removedStatusUpdates++;
            }
        }

        if (gamesToUpdate.Count > 0)
        {
            playniteApi.Database.Games.Update(gamesToUpdate.Distinct().ToList());
        }

        logger.Info(
            $"PC Game Pass reconciliation completed: {activeGames.Count} active, " +
            $"{addedGames.Count} imported, {activeStatusUpdates} active status updates, " +
            $"{removedStatusUpdates} marked removed, {excludedGames} excluded by user, " +
            $"{importFailures} import failures.");
        return addedGames;
    }

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
}
