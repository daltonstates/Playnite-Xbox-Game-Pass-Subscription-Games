using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SubscriptionLibraries.Core.Providers.GamePass;
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

    public override string Name => "PC Game Pass";

    public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
    {
        if (!settingsViewModel.Settings.PcGamePassEnabled)
        {
            Logger.Info("PC Game Pass provider is disabled; returning no subscription games.");
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
                .Where(game => game.Availability != Core.Models.SubscriptionAvailability.Removed)
                .Select(game => PlayniteGameMapper.Map(game, now))
                .ToList();
            Logger.Info(
                $"Returning {metadata.Count} PC Game Pass library entries from {result.Source}.");
            return metadata;
        }
        catch (OperationCanceledException) when (args.CancelToken.IsCancellationRequested)
        {
            Logger.Info("PC Game Pass library synchronization was canceled.");
            throw;
        }
        catch (Exception exception)
        {
            ScheduleSettingsUpdate(() => settingsViewModel.RecordSyncError(exception));
            Logger.Error(exception, "PC Game Pass library synchronization failed.");
            throw;
        }
    }

    public override IEnumerable<Game> ImportGames(LibraryImportGamesArgs args)
    {
        if (!settingsViewModel.Settings.PcGamePassEnabled)
        {
            Logger.Info("PC Game Pass provider is disabled; existing library records were left unchanged.");
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
                DateTimeOffset.UtcNow,
                args.CancelToken);
        }
        catch (OperationCanceledException) when (args.CancelToken.IsCancellationRequested)
        {
            Logger.Info("PC Game Pass library synchronization was canceled.");
            throw;
        }
        catch (Exception exception)
        {
            ScheduleSettingsUpdate(() => settingsViewModel.RecordSyncError(exception));
            Logger.Error(exception, "PC Game Pass library synchronization failed.");
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

    private async Task<SubscriptionSyncResult> SynchronizeAsync(
        bool forceRefresh,
        bool refreshFromNetwork,
        CancellationToken cancellationToken)
    {
        if (!settingsViewModel.Settings.PcGamePassEnabled)
        {
            throw new InvalidOperationException("Enable PC Game Pass before refreshing the catalog.");
        }

        await synchronizationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = settingsViewModel.Settings;
            var providerOptions = new GamePassProviderOptions
            {
                Region = settings.Region,
                Language = settings.Language,
                SiglId = settings.GamePassSiglId
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
