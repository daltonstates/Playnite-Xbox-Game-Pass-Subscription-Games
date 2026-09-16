using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Services;
using SubscriptionLibraries.Services;

namespace SubscriptionLibraries;

public sealed class SubscriptionLibrariesPlugin : LibraryPlugin
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    private readonly HttpClient httpClient;
    private readonly HttpClientService httpClientService;
    private readonly SubscriptionSyncService syncService;
    private readonly SemaphoreSlim synchronizationLock = new(1, 1);
    private readonly PlayniteLibraryReconciler libraryReconciler;
    private readonly PlayniteSubscriptionLogger subscriptionLogger;
    private readonly SubscriptionLibrariesSettingsViewModel settingsViewModel;

    public SubscriptionLibrariesPlugin(IPlayniteAPI api)
        : base(api)
    {
        subscriptionLogger = new PlayniteSubscriptionLogger(Logger);
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SubscriptionLibraries", "1.0"));
        httpClientService = new HttpClientService(httpClient, subscriptionLogger);

        var cacheDirectory = Path.Combine(GetPluginUserDataPath(), "CatalogCache");
        var cacheService = new CatalogCacheService(cacheDirectory, subscriptionLogger);
        syncService = new SubscriptionSyncService(cacheService, subscriptionLogger);
        settingsViewModel = new SubscriptionLibrariesSettingsViewModel(this);
        libraryReconciler = new PlayniteLibraryReconciler(api, this, Logger);

        Properties = new LibraryPluginProperties
        {
            HasSettings = true,
            HasCustomizedGameImport = true
        };
    }

    public override Guid Id { get; } = Guid.Parse("fa24a220-5ac0-440b-a816-30186a40c1a6");

    public override string Name => "Subscription Libraries - Game Pass";

    public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
    {
        yield return new MainMenuItem
        {
            Description = "Show active Game Pass games",
            MenuSection = "@Subscription Libraries",
            Action = _ => ShowActiveGamePassGames()
        };
        yield return new MainMenuItem
        {
            Description = "Refresh and apply Game Pass catalog...",
            MenuSection = "@Subscription Libraries",
            Action = async _ => await RefreshAndApplyFromMenuAsync()
        };
    }

    public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
    {
        if (args.Games?.Count == 1 && settingsViewModel.Settings.PcGamePassEnabled)
        {
            yield return new GameMenuItem
            {
                Description = "Check Game Pass availability",
                MenuSection = "@Subscription Libraries",
                Action = async action => await CheckGamePassAvailabilityAsync(
                    action.Games.First())
            };
        }
    }

    public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
    {
        if (!settingsViewModel.Settings.PcGamePassEnabled)
        {
            Logger.Info("Game Pass provider is disabled; returning no subscription games.");
            return Array.Empty<GameMetadata>();
        }

        try
        {
            var result = SynchronizeAsync(
                    false,
                    settingsViewModel.Settings.RefreshDuringLibraryUpdate,
                    args.CancelToken)
                .GetAwaiter()
                .GetResult();
            var now = DateTimeOffset.UtcNow;
            var metadata = result.Games
                .Where(game =>
                    game.Availability != Core.Models.SubscriptionAvailability.Removed &&
                    settingsViewModel.Settings.Includes(game))
                .Select(game => settingsViewModel.Settings.GamePassPlanSelection.Project(
                    settingsViewModel.Settings.GamePassConsoleSelection, game))
                .Select(game => PlayniteGameMapper.Map(
                    game, now, result.IsVerified, result.CatalogTimestampUtc))
                .ToList();
            Logger.Info(
                $"Returning {metadata.Count} selected Game Pass library entries from {result.Source}.");
            return metadata;
        }
        catch (OperationCanceledException) when (args.CancelToken.IsCancellationRequested)
        {
            Logger.Info("Game Pass library synchronization was canceled.");
            throw;
        }
        catch (Exception exception)
        {
            ScheduleSettingsUpdate(() => settingsViewModel.RecordSyncError(exception));
            Logger.Error(exception, "Game Pass library synchronization failed.");
            throw;
        }
    }

    public override IEnumerable<Game> ImportGames(LibraryImportGamesArgs args)
    {
        if (!settingsViewModel.Settings.PcGamePassEnabled)
        {
            Logger.Info("Game Pass provider is disabled; existing library records were left unchanged.");
            return Array.Empty<Game>();
        }

        try
        {
            var result = SynchronizeAsync(
                    false,
                    settingsViewModel.Settings.RefreshDuringLibraryUpdate,
                    args.CancelToken)
                .GetAwaiter()
                .GetResult();
            return libraryReconciler.Reconcile(
                result,
                settingsViewModel.Settings.GamePassCatalogSelection,
                settingsViewModel.Settings.GamePassPlanSelection,
                settingsViewModel.Settings.GamePassConsoleSelection,
                settingsViewModel.Settings.ExcludeConfirmedFreeToPlay,
                settingsViewModel.Settings.UnavailableGameHandling,
                DateTimeOffset.UtcNow,
                args.CancelToken);
        }
        catch (OperationCanceledException) when (args.CancelToken.IsCancellationRequested)
        {
            Logger.Info("Game Pass library synchronization was canceled.");
            throw;
        }
        catch (Exception exception)
        {
            ScheduleSettingsUpdate(() => settingsViewModel.RecordSyncError(exception));
            Logger.Error(exception, "Game Pass library synchronization failed.");
            throw;
        }
    }

    public override ISettings GetSettings(bool firstRunSettings) => settingsViewModel;

    public override UserControl GetSettingsView(bool firstRunSettings) =>
        new SubscriptionLibrariesSettingsView();

    public override void Dispose()
    {
        synchronizationLock.Dispose();
        httpClient.Dispose();
        base.Dispose();
    }

    internal Task<SubscriptionSyncResult> RefreshCatalogAsync() =>
        SynchronizeAsync(true, true, CancellationToken.None);

    private void ShowActiveGamePassGames()
    {
        var activeTag = PlayniteApi.Database.Tags.FirstOrDefault(tag =>
            string.Equals(tag.Name, PlayniteGameMapper.ActiveAccessTag,
                StringComparison.OrdinalIgnoreCase));
        if (activeTag is null)
        {
            PlayniteApi.Dialogs.ShowMessage(
                "There are no active Game Pass entries yet. Update the library or use Refresh and Apply.",
                "Subscription Libraries");
            return;
        }

        PlayniteApi.MainView.ApplyFilterPreset(new FilterPreset
        {
            Name = "Active Game Pass",
            Settings = new FilterPresetSettings
            {
                UseAndFilteringStyle = true,
                Library = new IdItemFilterItemProperties(Id),
                Tag = new IdItemFilterItemProperties(activeTag.Id)
            }
        });
    }

    private async Task RefreshAndApplyFromMenuAsync()
    {
        try
        {
            var message = await RefreshAndApplyAsync();
            PlayniteApi.Dialogs.ShowMessage(message, "Subscription Libraries");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Game Pass refresh and apply failed.");
            PlayniteApi.Dialogs.ShowErrorMessage(exception.Message,
                "Game Pass refresh and apply failed");
        }
    }

    private async Task CheckGamePassAvailabilityAsync(Game selected)
    {
        try
        {
            var result = await SynchronizeAsync(false, false, CancellationToken.None);
            var candidate = new MatchableGame
            {
                GameId = selected.GameId ?? string.Empty,
                Name = selected.Name ?? string.Empty
            };
            var matching = new GameMatchingService();
            var matches = result.Games
                .Where(game => game.Availability != SubscriptionAvailability.Removed &&
                    settingsViewModel.Settings.Includes(game))
                .Select(game => new
                {
                    Game = game,
                    Match = matching.FindLikelyRelationships(game, new[] { candidate })
                        .FirstOrDefault()
                })
                .Where(item => item.Match is not null)
                .OrderBy(item => item.Match!.Kind)
                .ThenByDescending(item => item.Match!.Confidence)
                .Take(10)
                .ToList();
            var freshness = result.IsVerified
                ? $"Catalog verified {result.CatalogTimestampUtc:u}."
                : $"Catalog unverified; last cached {result.CatalogTimestampUtc:u}.";
            var lines = matches.Count == 0
                ? "No likely match in your selected Game Pass plan/platform."
                : string.Join("\n", matches.Select(item =>
                    $"{item.Game.Name} - {item.Game.Platform} - " +
                    $"{item.Match!.Kind} ({item.Match.Confidence:P0})" +
                    (item.Game.Availability == SubscriptionAvailability.LeavingSoon
                        ? " - Leaving soon" : string.Empty)));
            PlayniteApi.Dialogs.ShowMessage(
                $"{selected.Name}\n{freshness}\n\n{lines}\n\n" +
                "Title matches are suggestions only; no records are merged.",
                "Check Game Pass availability");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Could not check Game Pass availability.");
            PlayniteApi.Dialogs.ShowErrorMessage(exception.Message,
                "Game Pass availability check failed");
        }
    }

    internal async Task<string> RefreshAndApplyAsync()
    {
        var result = await RefreshCatalogAsync();
        if (!result.IsVerified)
        {
            throw new InvalidOperationException(
                "Microsoft's catalog could not be verified. The previous cache remains available, " +
                "but no library changes were applied. " + result.Warning);
        }

        var settings = settingsViewModel.Settings;
        var preview = libraryReconciler.Preview(
            result, settings.GamePassCatalogSelection, settings.GamePassPlanSelection,
            settings.GamePassConsoleSelection,
            settings.ExcludeConfirmedFreeToPlay, settings.UnavailableGameHandling);
        if (PlayniteApi.Dialogs.ShowMessage(
                preview, "Game Pass - Refresh and Apply",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return "Catalog refreshed; Playnite library changes were canceled.";
        }

        var added = libraryReconciler.Reconcile(
            result, settings.GamePassCatalogSelection, settings.GamePassPlanSelection,
            settings.GamePassConsoleSelection,
            settings.ExcludeConfirmedFreeToPlay, settings.UnavailableGameHandling,
            DateTimeOffset.UtcNow, CancellationToken.None);
        return $"Game Pass catalog applied. {added.Count:N0} new entries were imported.";
    }

    private async Task<SubscriptionSyncResult> SynchronizeAsync(
        bool forceRefresh,
        bool refreshFromNetwork,
        CancellationToken cancellationToken)
    {
        if (!settingsViewModel.Settings.PcGamePassEnabled)
        {
            throw new InvalidOperationException("Enable Game Pass before refreshing the catalog.");
        }

        await synchronizationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = settingsViewModel.Settings;
            var providerOptions = new GamePassProviderOptions
            {
                Region = settings.Region,
                Language = settings.Language,
                SiglId = settings.GamePassSiglId,
                ConsoleSiglId = settings.ConsoleGamePassSiglId
            }.NormalizeAndValidate();
            var provider = new GamePassProvider(
                new GamePassCatalogClient(httpClientService, providerOptions),
                new MicrosoftStoreCatalogClient(
                    httpClientService,
                    providerOptions,
                    subscriptionLogger),
                new GamePassPlatformClassifier(),
                providerOptions,
                subscriptionLogger);
            var result = await syncService.SynchronizeAsync(
                    provider,
                    new SubscriptionSyncOptions
                    {
                        Region = providerOptions.Region,
                        Language = providerOptions.Language,
                        CacheLifetime = TimeSpan.FromHours(settings.CacheDurationHours),
                        ForceRefresh = forceRefresh,
                        RefreshFromNetwork = refreshFromNetwork
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            ScheduleSettingsUpdate(() => settingsViewModel.RecordSyncResult(result));
            var newlyLeavingNames = result.NewlyLeavingSoonGames
                .Where(settings.Includes)
                .Select(game => settings.GamePassPlanSelection.Project(
                    settings.GamePassConsoleSelection, game))
                .Where(game => game.Availability == SubscriptionAvailability.LeavingSoon)
                .Select(game => game.Name)
                .OrderBy(name => name)
                .ToList();
            if (result.Source == SubscriptionCatalogSource.Live &&
                settings.NotifyLeavingSoon && newlyLeavingNames.Count > 0)
            {
                ScheduleSettingsUpdate(() => PlayniteApi.Notifications.Add(
                    new NotificationMessage(
                        $"SubscriptionLibraries.GamePass.LeavingSoon.{result.CatalogTimestampUtc:yyyyMMddHHmm}",
                        $"{newlyLeavingNames.Count} Game Pass games are leaving soon: " +
                        string.Join(", ", newlyLeavingNames.Take(5)) +
                        (newlyLeavingNames.Count > 5 ? ", ..." : string.Empty),
                        NotificationType.Info)));
            }
            return result;
        }
        finally
        {
            synchronizationLock.Release();
        }
    }

    private void ScheduleSettingsUpdate(Action update)
    {
        var dispatcher = PlayniteApi.MainView.UIDispatcher;
        if (dispatcher.CheckAccess())
        {
            update();
        }
        else
        {
            dispatcher.BeginInvoke(update);
        }
    }
}
