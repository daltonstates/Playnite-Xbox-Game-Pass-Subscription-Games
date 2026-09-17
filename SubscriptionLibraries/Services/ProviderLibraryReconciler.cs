using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Playnite;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Providers.UbisoftPlus;
using SubscriptionLibraries.Core.Services;

namespace SubscriptionLibraries.Services;

/// <summary>Reconciles one namespaced subscription catalog at a time.</summary>
internal sealed class ProviderLibraryReconciler
{
    private readonly IPlayniteAPI api;
    private readonly LibraryPlugin plugin;
    private readonly ILogger logger;

    public ProviderLibraryReconciler(IPlayniteAPI api, LibraryPlugin plugin, ILogger logger)
    {
        this.api = api;
        this.plugin = plugin;
        this.logger = logger;
    }

    public string Preview(
        SubscriptionSyncResult result,
        UbisoftPlusPlanSelection selection,
        UnavailableGameHandling handling)
    {
        if (!result.IsVerified)
        {
            return "The Ubisoft+ catalog is unverified. No library changes will be applied.";
        }

        var selectedIds = new HashSet<string>(result.Games.Where(game =>
            game.Availability != SubscriptionAvailability.Removed &&
            UbisoftPlusProvider.Includes(selection, game))
            .Select(game => game.ProviderGameId), StringComparer.OrdinalIgnoreCase);
        var owned = OwnedGames();
        var newCount = selectedIds.Count(id =>
            owned.All(game => !string.Equals(game.GameId, id, StringComparison.OrdinalIgnoreCase)) &&
            api.Database.ImportExclusions[ImportExclusionItem.GetId(id, plugin.Id)] is null);
        var unavailable = owned.Where(game =>
            string.IsNullOrWhiteSpace(game.GameId) || !selectedIds.Contains(game.GameId)).ToList();
        var hiddenMarkerIds = HiddenMarkerIds();
        var removeCount = unavailable.Count(game => Decide(game, handling, hiddenMarkerIds) ==
            UnavailableGameAction.Remove);
        return $"Verified {result.CatalogTimestampUtc:u}\n" +
            $"Selected Ubisoft+ games: {selectedIds.Count:N0}\n" +
            $"New entries: {newCount:N0}\n" +
            $"Existing entries outside selection or no longer listed: {unavailable.Count:N0}\n" +
            $"Will permanently remove: {removeCount:N0}\n\n" +
            "Only this extension's Ubisoft+ PC entries are affected. Apply these changes?";
    }

    public IReadOnlyList<Game> Reconcile(
        SubscriptionSyncResult result,
        UbisoftPlusPlanSelection selection,
        UnavailableGameHandling handling,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.IsVerified)
        {
            MarkUnverified(cancellationToken);
            return Array.Empty<Game>();
        }

        var owned = OwnedGames();
        var ownedById = owned.Where(game => !string.IsNullOrWhiteSpace(game.GameId))
            .ToLookup(game => game.GameId, StringComparer.OrdinalIgnoreCase);
        var current = result.Games.Where(game =>
            game.Availability != SubscriptionAvailability.Removed)
            .ToDictionary(game => game.ProviderGameId, StringComparer.OrdinalIgnoreCase);
        var selected = current.Values.Where(game => UbisoftPlusProvider.Includes(selection, game))
            .ToDictionary(game => game.ProviderGameId, StringComparer.OrdinalIgnoreCase);
        var added = new List<Game>();
        var changed = new List<Game>();
        var removed = new HashSet<Guid>();
        var hiddenMarkerIds = HiddenMarkerIds();

        foreach (var subscriptionGame in selected.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = ownedById[subscriptionGame.ProviderGameId].ToList();
            if (matches.Count == 0)
            {
                if (api.Database.ImportExclusions[
                        ImportExclusionItem.GetId(subscriptionGame.ProviderGameId, plugin.Id)] is not null)
                {
                    continue;
                }

                try
                {
                    added.Add(api.Database.ImportGame(
                        PlayniteGameMapper.Map(subscriptionGame, now, true,
                            result.CatalogTimestampUtc), plugin));
                }
                catch (Exception exception)
                {
                    logger.Error(exception,
                        $"Could not import Ubisoft+ product {subscriptionGame.ProviderGameId}.");
                }
                continue;
            }

            var activeTags = PlayniteGameMapper.GetActiveTagNames(subscriptionGame, now);
            foreach (var game in matches)
            {
                var visibilityChanged = hiddenMarkerIds.Any(id => game.TagIds?.Contains(id) == true) &&
                    game.Hidden;
                if (visibilityChanged) game.Hidden = false;
                if (ApplyManagedTags(game, activeTags) || visibilityChanged)
                {
                    changed.Add(game);
                }
            }
        }

        foreach (var game in owned.Where(game => !selected.ContainsKey(game.GameId ?? string.Empty)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stillListed = game.GameId is not null && current.ContainsKey(game.GameId);
            var tags = stillListed
                ? new[] { "Subscription: Ubisoft+", "Access: Not in selected plan" }
                : PlayniteGameMapper.GetRemovedTagNames(UbisoftPlusProvider.ProviderName);
            ApplyUnavailable(game, tags, handling, hiddenMarkerIds, changed, removed);
        }

        if (changed.Count > 0)
        {
            api.Database.Games.Update(changed.Distinct().ToList());
        }

        // Playnite can attach CollectionViews to Links. Edit them on its UI thread.
        var linkUpdates = new List<Game>();
        void UpdateLinks()
        {
            foreach (var game in owned.Where(game => !removed.Contains(game.Id) &&
                         selected.ContainsKey(game.GameId ?? string.Empty)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (UpdateManagedLinks(game, selected[game.GameId!], result.CatalogTimestampUtc))
                {
                    linkUpdates.Add(game);
                }
            }
        }
        var dispatcher = api.MainView.UIDispatcher;
        if (dispatcher.CheckAccess()) UpdateLinks();
        else dispatcher.Invoke((Action)UpdateLinks);
        if (linkUpdates.Count > 0) api.Database.Games.Update(linkUpdates);

        logger.Info($"Ubisoft+ reconciliation: {selected.Count} selected, " +
            $"{added.Count} imported, {removed.Count} removed.");
        return added;
    }

    private List<Game> OwnedGames() => api.Database.Games.Where(game =>
        game.PluginId == plugin.Id && game.GameId?.StartsWith(
            UbisoftPlusProvider.GameIdPrefix, StringComparison.OrdinalIgnoreCase) == true).ToList();

    private HashSet<Guid> HiddenMarkerIds() => new(api.Database.Tags.Where(tag =>
        string.Equals(tag.Name, PlayniteGameMapper.HiddenBySelectionTag,
            StringComparison.OrdinalIgnoreCase)).Select(tag => tag.Id));

    private static UnavailableGameAction Decide(
        Game game, UnavailableGameHandling handling, HashSet<Guid> hiddenMarkerIds)
    {
        var hiddenByPlugin = game.TagIds?.Any(hiddenMarkerIds.Contains) == true;
        return UnavailableGamePolicy.Decide(handling, true, game.Playtime, game.IsInstalled,
            game.IsInstalling || game.IsRunning || game.IsLaunching || game.IsUninstalling,
            game.Hidden && !hiddenByPlugin);
    }

    private void ApplyUnavailable(
        Game game, IReadOnlyCollection<string> baseTags, UnavailableGameHandling handling,
        HashSet<Guid> hiddenMarkerIds, List<Game> changed, HashSet<Guid> removed)
    {
        var action = Decide(game, handling, hiddenMarkerIds);
        if (action == UnavailableGameAction.Remove)
        {
            try
            {
                if (api.Database.Games.Remove(game.Id))
                {
                    removed.Add(game.Id);
                    return;
                }
            }
            catch (Exception exception)
            {
                logger.Error(exception, $"Could not remove Ubisoft+ game {game.GameId}.");
            }
            action = UnavailableGameAction.Hide;
        }

        var tags = baseTags.ToList();
        var visibilityChanged = false;
        var hiddenByPlugin = game.TagIds?.Any(hiddenMarkerIds.Contains) == true;
        var hiddenByUser = game.Hidden && !hiddenByPlugin;
        if (action == UnavailableGameAction.Hide)
        {
            if (!game.Hidden) { game.Hidden = true; visibilityChanged = true; }
            if (!hiddenByUser)
            {
                tags.Add(PlayniteGameMapper.HiddenBySelectionTag);
            }
        }
        else if (hiddenByPlugin && game.Hidden)
        {
            game.Hidden = false;
            visibilityChanged = true;
        }

        if (ApplyManagedTags(game, tags) || visibilityChanged) changed.Add(game);
    }

    private bool ApplyManagedTags(Game game, IReadOnlyCollection<string> desired)
    {
        var managedNames = new HashSet<string>(
            PlayniteGameMapper.GetManagedTagNames(UbisoftPlusProvider.ProviderName),
            StringComparer.OrdinalIgnoreCase);
        var managedIds = new HashSet<Guid>(api.Database.Tags.Where(tag =>
            managedNames.Contains(tag.Name)).Select(tag => tag.Id));
        var desiredIds = api.Database.Tags.Add(desired.ToList()).Select(tag => tag.Id);
        var updated = (game.TagIds ?? new List<Guid>()).Where(id => !managedIds.Contains(id))
            .Concat(desiredIds).Distinct().ToList();
        var original = game.TagIds ?? new List<Guid>();
        if (original.Count == updated.Count && new HashSet<Guid>(original).SetEquals(updated))
        {
            return false;
        }
        game.TagIds = updated;
        return true;
    }

    private void MarkUnverified(CancellationToken cancellationToken)
    {
        var owned = OwnedGames();
        var activeIds = new HashSet<Guid>(api.Database.Tags.Where(tag =>
            string.Equals(tag.Name, PlayniteGameMapper.ActiveAccessTag,
                StringComparison.OrdinalIgnoreCase)).Select(tag => tag.Id));
        var unverifiedId = api.Database.Tags.Add(PlayniteGameMapper.UnverifiedAccessTag).Id;
        var changed = new List<Game>();
        foreach (var game in owned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = game.TagIds ?? new List<Guid>();
            var updated = original.Where(id => !activeIds.Contains(id))
                .Concat(new[] { unverifiedId }).Distinct().ToList();
            if (original.Count == updated.Count && new HashSet<Guid>(original).SetEquals(updated))
                continue;
            game.TagIds = updated;
            changed.Add(game);
        }
        if (changed.Count > 0) api.Database.Games.Update(changed);
    }

    private static bool UpdateManagedLinks(
        Game game, SubscriptionGame subscriptionGame, DateTimeOffset timestamp)
    {
        game.Links ??= new ObservableCollection<Link>();
        var desiredPage = subscriptionGame.StoreUri?.AbsoluteUri;
        var desiredVerification = PlayniteGameMapper.CreateVerificationLink(
            timestamp, UbisoftPlusProvider.ProviderId);
        var pageLinks = game.Links.Where(link => link.Name == "Ubisoft Store").ToList();
        var verificationLinks = game.Links.Where(link => link.Name?.StartsWith(
            PlayniteGameMapper.VerifiedLinkPrefix, StringComparison.Ordinal) == true).ToList();
        if (pageLinks.Count == 1 && pageLinks[0].Url == desiredPage &&
            verificationLinks.Count == 1 &&
            verificationLinks[0].Name == desiredVerification.Name &&
            verificationLinks[0].Url == desiredVerification.Url)
        {
            return false;
        }
        foreach (var link in pageLinks.Concat(verificationLinks)) game.Links.Remove(link);
        if (desiredPage is not null) game.Links.Add(new Link("Ubisoft Store", desiredPage));
        game.Links.Add(desiredVerification);
        return true;
    }
}
