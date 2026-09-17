using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Providers.UbisoftPlus;
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
    private readonly ProviderLibraryReconciler providerReconciler;
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
        providerReconciler = new ProviderLibraryReconciler(api, this, Logger);

        Properties = new LibraryPluginProperties
        {
            HasSettings = true,
            HasCustomizedGameImport = true
        };
    }

    public override Guid Id { get; } = Guid.Parse("fa24a220-5ac0-440b-a816-30186a40c1a6");

    public override string Name => "Subscription Libraries";

    public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
    {
        yield return new MainMenuItem
        {
            Description = "Show active subscription games",
            MenuSection = "@Subscription Libraries",
            Action = _ => ShowActiveSubscriptionGames()
        };
        if (settingsViewModel.Settings.PcGamePassEnabled)
        {
            yield return new MainMenuItem
            {
                Description = "Refresh and apply Game Pass catalog...",
                MenuSection = "@Subscription Libraries",
                Action = async _ => await RefreshAndApplyFromMenuAsync()
            };
        }
        if (settingsViewModel.Settings.UbisoftPlusEnabled &&
            UbisoftPlusProvider.Supports(settingsViewModel.Settings.Region,
                settingsViewModel.Settings.Language))
        {
            yield return new MainMenuItem
            {
                Description = "Refresh and apply Ubisoft+ PC catalog...",
                MenuSection = "@Subscription Libraries",
                Action = async _ => await RefreshUbisoftFromMenuAsync()
            };
        }
    }

    public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
    {
        if (args.Games?.Count != 1)
        {
            yield break;
        }

        var selected = args.Games[0];
        if (settingsViewModel.Settings.PcGamePassEnabled)
        {
            yield return new GameMenuItem
            {
                Description = "Check Game Pass availability",
                MenuSection = "@Subscription Libraries",
                Action = async action => await CheckGamePassAvailabilityAsync(
                    action.Games.First())
            };
        }

        if (settingsViewModel.Settings.PcGamePassEnabled && CanOpenPcInstallPage(selected))
        {
            var installApp = settingsViewModel.Settings.PreferredInstallApp;
            var appName = installApp == GamePassInstallApp.MicrosoftStore
                ? "Microsoft Store" : "Xbox app";
            yield return new GameMenuItem
            {
                Description = $"Open {appName} page to install...",
                MenuSection = "@Subscription Libraries",
                Action = _ => OpenPcInstallPage(selected, installApp)
            };
        }
        if (settingsViewModel.Settings.UbisoftPlusEnabled && CanOpenUbisoftInstallPage(selected))
        {
            yield return new GameMenuItem
            {
                Description = "Open Ubisoft Store page to install...",
                MenuSection = "@Subscription Libraries",
                Action = _ => OpenUbisoftInstallPage(selected)
            };
        }
    }

    public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
    {
        if (args.Game is null)
        {
            yield break;
        }

        var game = args.Game;
        if (settingsViewModel.Settings.PcGamePassEnabled && CanOpenPcInstallPage(game))
        {
            var installApp = settingsViewModel.Settings.PreferredInstallApp;
            var appName = installApp == GamePassInstallApp.MicrosoftStore
                ? "Microsoft Store" : "Xbox app";
            yield return new StorePageInstallController(
                game, appName, () => OpenPcInstallPage(game, installApp));
        }
        if (settingsViewModel.Settings.UbisoftPlusEnabled && CanOpenUbisoftInstallPage(game))
        {
            yield return new StorePageInstallController(
                game, "Ubisoft Store", () => OpenUbisoftInstallPage(game));
        }
    }

    public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
    {
        var settings = CaptureSettings();
        var ubisoftEnabled = settings.UbisoftPlusEnabled &&
            UbisoftPlusProvider.Supports(settings.Region, settings.Language);
        if (!settings.PcGamePassEnabled && !ubisoftEnabled)
        {
            Logger.Info("No supported subscription provider is enabled.");
            return Array.Empty<GameMetadata>();
        }

        var metadata = new List<GameMetadata>();
        if (settings.PcGamePassEnabled)
        {
            try
            {
                var result = SynchronizeAsync(
                    settings,
                    false,
                    settings.RefreshDuringLibraryUpdate,
                    args.CancelToken)
                    .GetAwaiter().GetResult();
                EnsureSettingsUnchanged(settings);
                var now = DateTimeOffset.UtcNow;
                metadata.AddRange(result.Games
                    .Where(game =>
                        game.Availability != SubscriptionAvailability.Removed &&
                        settings.Includes(game))
                    .Select(game => settings.GamePassPlanSelection.Project(
                        settings.GamePassCatalogSelection,
                        settings.GamePassConsoleSelection, game))
                    .Select(game => PlayniteGameMapper.Map(
                        game, now, result.IsVerified, result.CatalogTimestampUtc)));
            }
            catch (OperationCanceledException) when (args.CancelToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ScheduleSettingsUpdate(() => settingsViewModel.RecordSyncError(exception));
                Logger.Error(exception, "Game Pass library synchronization failed.");
                if (!ubisoftEnabled) throw;
            }
        }
        if (ubisoftEnabled)
        {
            try
            {
                var result = SynchronizeUbisoftAsync(settings, false,
                    settings.RefreshDuringLibraryUpdate, args.CancelToken)
                    .GetAwaiter().GetResult();
                EnsureSettingsUnchanged(settings);
                metadata.AddRange(result.Games.Where(game =>
                        game.Availability != SubscriptionAvailability.Removed &&
                        UbisoftPlusProvider.Includes(settings.UbisoftPlusPlanSelection, game))
                    .Select(game => PlayniteGameMapper.Map(game, DateTimeOffset.UtcNow,
                        result.IsVerified, result.CatalogTimestampUtc)));
            }
            catch (OperationCanceledException) when (args.CancelToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ScheduleSettingsUpdate(() => settingsViewModel.RecordUbisoftError(exception));
                Logger.Error(exception, "Ubisoft+ catalog synchronization failed.");
                if (!settings.PcGamePassEnabled) throw;
            }
        }
        return metadata;
    }

    public override IEnumerable<Game> ImportGames(LibraryImportGamesArgs args)
    {
        var settings = CaptureSettings();
        var ubisoftEnabled = settings.UbisoftPlusEnabled &&
            UbisoftPlusProvider.Supports(settings.Region, settings.Language);
        if (!settings.PcGamePassEnabled && !ubisoftEnabled)
        {
            Logger.Info("No supported provider is enabled; library records were left unchanged.");
            return Array.Empty<Game>();
        }

        var added = new List<Game>();
        if (settings.PcGamePassEnabled)
        {
            try
            {
                var result = SynchronizeAsync(
                    settings,
                    false,
                    settings.RefreshDuringLibraryUpdate,
                    args.CancelToken)
                    .GetAwaiter().GetResult();
                EnsureSettingsUnchanged(settings);
                added.AddRange(libraryReconciler.Reconcile(result,
                    settings.GamePassCatalogSelection,
                    settings.GamePassPlanSelection,
                    settings.GamePassConsoleSelection,
                    settings.ExcludeConfirmedFreeToPlay,
                    settings.UnavailableGameHandling,
                    DateTimeOffset.UtcNow, args.CancelToken));
            }
            catch (OperationCanceledException) when (args.CancelToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ScheduleSettingsUpdate(() => settingsViewModel.RecordSyncError(exception));
                Logger.Error(exception, "Game Pass library synchronization failed.");
                if (!ubisoftEnabled) throw;
            }
        }
        if (ubisoftEnabled)
        {
            try
            {
                var result = SynchronizeUbisoftAsync(settings, false,
                    settings.RefreshDuringLibraryUpdate, args.CancelToken)
                    .GetAwaiter().GetResult();
                EnsureSettingsUnchanged(settings);
                added.AddRange(providerReconciler.Reconcile(result,
                    settings.UbisoftPlusPlanSelection, settings.UnavailableGameHandling,
                    DateTimeOffset.UtcNow, args.CancelToken));
            }
            catch (OperationCanceledException) when (args.CancelToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ScheduleSettingsUpdate(() => settingsViewModel.RecordUbisoftError(exception));
                Logger.Error(exception, "Ubisoft+ catalog synchronization failed.");
                if (!settings.PcGamePassEnabled) throw;
            }
        }
        return added;
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

    private void ShowActiveSubscriptionGames()
    {
        var activeTag = PlayniteApi.Database.Tags.FirstOrDefault(tag =>
            string.Equals(tag.Name, PlayniteGameMapper.ActiveAccessTag,
                StringComparison.OrdinalIgnoreCase));
        if (activeTag is null)
        {
            PlayniteApi.Dialogs.ShowMessage(
                "There are no active subscription entries yet. Update the library or use Refresh and Apply.",
                "Subscription Libraries");
            return;
        }

        PlayniteApi.MainView.ApplyFilterPreset(new FilterPreset
        {
            Name = "Active subscription games",
            Settings = new FilterPresetSettings
            {
                UseAndFilteringStyle = true,
                Library = new IdItemFilterItemProperties(Id),
                Tag = new IdItemFilterItemProperties(activeTag.Id)
            }
        });
    }

    private bool CanOpenPcInstallPage(Game game)
    {
        if (game.PluginId != Id || game.IsInstalled ||
            GamePassProductPages.FromProductId(game.GameId) is null ||
            game.TagIds is null)
        {
            return false;
        }

        var tagIds = new HashSet<Guid>(game.TagIds);
        var tagNames = new HashSet<string>(PlayniteApi.Database.Tags
            .Where(tag => tagIds.Contains(tag.Id))
            .Select(tag => tag.Name), StringComparer.OrdinalIgnoreCase);
        return tagNames.Contains(PlayniteGameMapper.ActiveAccessTag) &&
               (tagNames.Contains(PlayniteGameMapper.PcOnlyMembershipTag) ||
                tagNames.Contains(PlayniteGameMapper.PcAndXboxMembershipTag));
    }

    private void OpenPcInstallPage(Game game, GamePassInstallApp installApp)
    {
        if (!CanOpenPcInstallPage(game))
        {
            PlayniteApi.Dialogs.ShowMessage(
                "This game is not currently marked as an uninstalled PC game in your selected Game Pass view. Refresh the library and try again.",
                "Game Pass install page unavailable");
            return;
        }

        var pages = GamePassProductPages.FromProductId(game.GameId)!;
        var useXboxApp = installApp != GamePassInstallApp.MicrosoftStore;
        var appName = useXboxApp ? "Xbox app" : "Microsoft Store";
        var uri = useXboxApp ? pages.XboxApp : pages.MicrosoftStore;
        try
        {
            using var launched = Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Could not open the {appName} page for {game.GameId}.");
            PlayniteApi.Dialogs.ShowErrorMessage(
                $"Could not open the {appName} product page. Check that the app is installed, or choose the other app in extension settings.\n\n{exception.Message}",
                "Game Pass install page unavailable");
        }
    }

    private bool CanOpenUbisoftInstallPage(Game game)
    {
        if (game.PluginId != Id || game.IsInstalled ||
            !settingsViewModel.Settings.UbisoftPlusEnabled ||
            !UbisoftPlusProvider.Supports(settingsViewModel.Settings.Region,
                settingsViewModel.Settings.Language) ||
            !TryGetUbisoftPage(game, out _) || game.TagIds is null)
        {
            return false;
        }

        var tagIds = new HashSet<Guid>(game.TagIds);
        return PlayniteApi.Database.Tags.Any(tag =>
            tagIds.Contains(tag.Id) &&
            string.Equals(tag.Name, PlayniteGameMapper.ActiveAccessTag,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetUbisoftPage(Game game, out Uri? page)
    {
        page = null;
        var gameId = game.GameId;
        if (gameId?.StartsWith(UbisoftPlusProvider.GameIdPrefix,
                StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }
        var productId = gameId.Substring(UbisoftPlusProvider.GameIdPrefix.Length);
        if (productId.Length != 24 || !productId.All(Uri.IsHexDigit))
        {
            return false;
        }
        foreach (var link in game.Links ?? Enumerable.Empty<Link>())
        {
            if (link.Name != "Ubisoft Store" ||
                !Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "store.ubisoft.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith("/us/", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.IndexOf(productId, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            page = uri;
            return true;
        }
        return false;
    }

    private void OpenUbisoftInstallPage(Game game)
    {
        if (!CanOpenUbisoftInstallPage(game) || !TryGetUbisoftPage(game, out var page))
        {
            PlayniteApi.Dialogs.ShowMessage(
                "This Ubisoft+ PC game has no verified install page. Refresh its catalog and try again.",
                "Ubisoft+ install page unavailable");
            return;
        }
        try
        {
            using var launched = Process.Start(new ProcessStartInfo(page!.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Could not open the Ubisoft Store page for {game.GameId}.");
            PlayniteApi.Dialogs.ShowErrorMessage(
                $"Could not open the Ubisoft Store product page.\n\n{exception.Message}",
                "Ubisoft+ install page unavailable");
        }
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

    private async Task RefreshUbisoftFromMenuAsync()
    {
        try
        {
            var message = await RefreshUbisoftAndApplyAsync();
            PlayniteApi.Dialogs.ShowMessage(message, "Subscription Libraries");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Ubisoft+ refresh and apply failed.");
            PlayniteApi.Dialogs.ShowErrorMessage(exception.Message,
                "Ubisoft+ refresh and apply failed");
        }
    }

    private async Task CheckGamePassAvailabilityAsync(Game selected)
    {
        try
        {
            var settings = CaptureSettings();
            var result = await SynchronizeAsync(settings, false, false, CancellationToken.None);
            EnsureSettingsUnchanged(settings);
            var candidate = new MatchableGame
            {
                GameId = selected.GameId ?? string.Empty,
                Name = selected.Name ?? string.Empty
            };
            var matching = new GameMatchingService();
            var matches = result.Games
                .Where(game => game.Availability != SubscriptionAvailability.Removed &&
                    settings.Includes(game))
                .Select(game => settings.GamePassPlanSelection.Project(
                    settings.GamePassCatalogSelection, settings.GamePassConsoleSelection, game))
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
        if (!settingsViewModel.VerifySettings(out var errors))
        {
            throw new ArgumentException(string.Join(" ", errors));
        }

        var settings = CaptureSettings();
        var result = await SynchronizeAsync(settings, true, true, CancellationToken.None);
        EnsureSettingsUnchanged(settings);
        if (!result.IsVerified)
        {
            throw new InvalidOperationException(
                "Microsoft's catalog could not be verified. The previous cache remains available, " +
                "but no library changes were applied. " + result.Warning);
        }

        // The settings button can run before Playnite's settings dialog is saved.
        // Record the verified view before committing it with the library changes.
        settingsViewModel.RecordSyncResult(result, settings);
        var preview = (settingsViewModel.IsEditing
            ? "Applying will also save the current settings, even if you later cancel this settings dialog.\n\n"
            : string.Empty) + libraryReconciler.Preview(
            result, settings.GamePassCatalogSelection, settings.GamePassPlanSelection,
            settings.GamePassConsoleSelection,
            settings.ExcludeConfirmedFreeToPlay, settings.UnavailableGameHandling);
        if (PlayniteApi.Dialogs.ShowMessage(
                preview, "Game Pass - Refresh and Apply",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return "Catalog refreshed; Playnite library changes were canceled.";
        }

        EnsureSettingsUnchanged(settings);
        settingsViewModel.CommitAppliedSettings();
        var added = libraryReconciler.Reconcile(
            result, settings.GamePassCatalogSelection, settings.GamePassPlanSelection,
            settings.GamePassConsoleSelection,
            settings.ExcludeConfirmedFreeToPlay, settings.UnavailableGameHandling,
            DateTimeOffset.UtcNow, CancellationToken.None);
        return $"Game Pass catalog applied. {added.Count:N0} new entries were imported.";
    }

    internal async Task<string> RefreshUbisoftAndApplyAsync()
    {
        if (!settingsViewModel.VerifySettings(out var errors))
        {
            throw new ArgumentException(string.Join(" ", errors));
        }
        var settings = CaptureSettings();
        if (!settings.UbisoftPlusEnabled ||
            !UbisoftPlusProvider.Supports(settings.Region, settings.Language))
        {
            throw new InvalidOperationException(
                "Enable Ubisoft+ while using the verified US / en-US catalog.");
        }

        var result = await SynchronizeUbisoftAsync(settings, true, true, CancellationToken.None);
        EnsureSettingsUnchanged(settings);
        if (!result.IsVerified)
        {
            throw new InvalidOperationException(
                "Ubisoft's catalog could not be verified. The previous cache remains available, " +
                "but no library changes were applied. " + result.Warning);
        }

        settingsViewModel.RecordUbisoftSyncResult(result, settings);
        var preview = (settingsViewModel.IsEditing
            ? "Applying will also save the current settings, even if you later cancel this settings dialog.\n\n"
            : string.Empty) + providerReconciler.Preview(result,
            settings.UbisoftPlusPlanSelection, settings.UnavailableGameHandling);
        if (PlayniteApi.Dialogs.ShowMessage(preview,
                "Ubisoft+ - Refresh and Apply", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return "Catalog refreshed; Playnite library changes were canceled.";
        }

        EnsureSettingsUnchanged(settings);
        settingsViewModel.CommitAppliedSettings();
        var added = providerReconciler.Reconcile(result,
            settings.UbisoftPlusPlanSelection, settings.UnavailableGameHandling,
            DateTimeOffset.UtcNow, CancellationToken.None);
        return $"Ubisoft+ PC catalog applied. {added.Count:N0} new entries were imported.";
    }

    private async Task<SubscriptionSyncResult> SynchronizeAsync(
        SubscriptionLibrariesSettings settings,
        bool forceRefresh,
        bool refreshFromNetwork,
        CancellationToken cancellationToken)
    {
        if (!settings.PcGamePassEnabled)
        {
            throw new InvalidOperationException("Enable Game Pass before refreshing the catalog.");
        }

        await synchronizationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
                        CatalogConfigurationKey = providerOptions.CatalogConfigurationKey,
                        ForceRefresh = forceRefresh,
                        RefreshFromNetwork = refreshFromNetwork
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            ScheduleSettingsUpdate(() => settingsViewModel.RecordSyncResult(result, settings));
            var newlyLeavingNames = result.NewlyLeavingSoonGames
                .Where(settings.Includes)
                .Select(game => settings.GamePassPlanSelection.Project(
                    settings.GamePassCatalogSelection,
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

    private async Task<SubscriptionSyncResult> SynchronizeUbisoftAsync(
        SubscriptionLibrariesSettings settings,
        bool forceRefresh,
        bool refreshFromNetwork,
        CancellationToken cancellationToken)
    {
        if (!settings.UbisoftPlusEnabled ||
            !UbisoftPlusProvider.Supports(settings.Region, settings.Language))
        {
            throw new InvalidOperationException(
                "Ubisoft+ PC is available only for the verified US / en-US catalog.");
        }

        await synchronizationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var provider = new UbisoftPlusProvider(httpClientService);
            var result = await syncService.SynchronizeAsync(provider,
                new SubscriptionSyncOptions
                {
                    Region = settings.Region,
                    Language = settings.Language,
                    CacheLifetime = TimeSpan.FromHours(settings.CacheDurationHours),
                    CatalogConfigurationKey = "ubisoft-plus-pc-us-en-US-v1",
                    ForceRefresh = forceRefresh,
                    RefreshFromNetwork = refreshFromNetwork
                }, cancellationToken).ConfigureAwait(false);
            ScheduleSettingsUpdate(() => settingsViewModel.RecordUbisoftSyncResult(result, settings));
            return result;
        }
        finally
        {
            synchronizationLock.Release();
        }
    }

    private SubscriptionLibrariesSettings CaptureSettings()
    {
        var dispatcher = PlayniteApi.MainView.UIDispatcher;
        SubscriptionLibrariesSettings Capture() =>
            Serialization.GetClone(settingsViewModel.Settings);
        return dispatcher.CheckAccess() ? Capture() : dispatcher.Invoke(Capture);
    }

    private void EnsureSettingsUnchanged(SubscriptionLibrariesSettings snapshot)
    {
        var dispatcher = PlayniteApi.MainView.UIDispatcher;
        bool Matches() => SameOperationSettings(snapshot, settingsViewModel.Settings);
        var unchanged = dispatcher.CheckAccess() ? Matches() : dispatcher.Invoke(Matches);
        if (!unchanged)
        {
            throw new InvalidOperationException(
                "Subscription settings changed during the catalog operation. Run the update again with the current settings.");
        }
    }

    private static bool SameOperationSettings(
        SubscriptionLibrariesSettings left, SubscriptionLibrariesSettings right) =>
        left.PcGamePassEnabled == right.PcGamePassEnabled &&
        left.UbisoftPlusEnabled == right.UbisoftPlusEnabled &&
        left.UbisoftPlusPlanSelection == right.UbisoftPlusPlanSelection &&
        left.GamePassCatalogSelection == right.GamePassCatalogSelection &&
        left.GamePassPlanSelection == right.GamePassPlanSelection &&
        left.GamePassConsoleSelection == right.GamePassConsoleSelection &&
        left.PreferredInstallApp == right.PreferredInstallApp &&
        left.UnavailableGameHandling == right.UnavailableGameHandling &&
        string.Equals(left.Region, right.Region, StringComparison.Ordinal) &&
        string.Equals(left.Language, right.Language, StringComparison.Ordinal) &&
        left.RefreshDuringLibraryUpdate == right.RefreshDuringLibraryUpdate &&
        left.CacheDurationHours == right.CacheDurationHours &&
        string.Equals(left.GamePassSiglId, right.GamePassSiglId, StringComparison.Ordinal) &&
        string.Equals(left.ConsoleGamePassSiglId, right.ConsoleGamePassSiglId, StringComparison.Ordinal) &&
        left.ExcludeConfirmedFreeToPlay == right.ExcludeConfirmedFreeToPlay &&
        left.NotifyLeavingSoon == right.NotifyLeavingSoon;

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
