using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Data;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Services;

namespace SubscriptionLibraries;

public enum GamePassInstallApp
{
    XboxApp,
    MicrosoftStore
}

public sealed class SubscriptionLibrariesSettings : ObservableObject
{
    private bool pcGamePassEnabled = true;
    private GamePassCatalogSelection gamePassCatalogSelection = GamePassCatalogSelection.PcOnly;
    private GamePassPlanSelection gamePassPlanSelection = GamePassPlanSelection.PcGamePass;
    private GamePassConsoleSelection gamePassConsoleSelection = GamePassConsoleSelection.Both;
    private GamePassInstallApp preferredInstallApp = GamePassInstallApp.XboxApp;
    private UnavailableGameHandling unavailableGameHandling = UnavailableGameHandling.KeepAndMark;
    private string region = "US";
    private string language = "en-US";
    private bool refreshDuringLibraryUpdate = true;
    private int cacheDurationHours = 24;
    private string gamePassSiglId = GamePassConstants.PcCatalogSiglId;
    private string consoleGamePassSiglId = GamePassConstants.ConsoleCatalogSiglId;
    private DateTime? lastSuccessfulSynchronizationUtc;
    private int lastPcGamePassGameCount;
    private string? lastCountViewDescription;
    private DateTime? lastCountCatalogTimestampUtc;
    private string? lastSynchronizationError;
    private string? lastCatalogSource;
    private int lastRejectedNonPcCount;
    private int lastUnclassifiedCount;
    private int lastPcCatalogCount;
    private int lastConsoleCatalogCount;
    private int lastBothGameCount;
    private bool excludeConfirmedFreeToPlay;
    private bool notifyLeavingSoon = true;

    public bool NotifyLeavingSoon
    {
        get => notifyLeavingSoon;
        set => SetValue(ref notifyLeavingSoon, value);
    }

    public bool ExcludeConfirmedFreeToPlay
    {
        get => excludeConfirmedFreeToPlay;
        set => SetValue(ref excludeConfirmedFreeToPlay, value);
    }

    public bool Includes(SubscriptionGame game) =>
        GamePassCatalogSelection.Includes(GamePassPlanSelection,
            GamePassConsoleSelection, game) &&
        (!ExcludeConfirmedFreeToPlay || !game.IsConfirmedFreeToPlay);

    public bool PcGamePassEnabled
    {
        get => pcGamePassEnabled;
        set => SetValue(ref pcGamePassEnabled, value);
    }

    public GamePassCatalogSelection GamePassCatalogSelection
    {
        get => gamePassCatalogSelection;
        set => SetValue(ref gamePassCatalogSelection, value);
    }

    public GamePassPlanSelection GamePassPlanSelection
    {
        get => gamePassPlanSelection;
        set => SetValue(ref gamePassPlanSelection, value);
    }

    public GamePassConsoleSelection GamePassConsoleSelection
    {
        get => gamePassConsoleSelection;
        set => SetValue(ref gamePassConsoleSelection, value);
    }

    public GamePassInstallApp PreferredInstallApp
    {
        get => preferredInstallApp;
        set => SetValue(ref preferredInstallApp, value);
    }

    public UnavailableGameHandling UnavailableGameHandling
    {
        get => unavailableGameHandling;
        set => SetValue(ref unavailableGameHandling, value);
    }

    public string Region
    {
        get => region;
        set => SetValue(ref region, value);
    }

    public string Language
    {
        get => language;
        set => SetValue(ref language, value);
    }

    public bool RefreshDuringLibraryUpdate
    {
        get => refreshDuringLibraryUpdate;
        set => SetValue(ref refreshDuringLibraryUpdate, value);
    }

    public int CacheDurationHours
    {
        get => cacheDurationHours;
        set => SetValue(ref cacheDurationHours, value);
    }

    public string GamePassSiglId
    {
        get => gamePassSiglId;
        set => SetValue(ref gamePassSiglId, value);
    }

    public string ConsoleGamePassSiglId
    {
        get => consoleGamePassSiglId;
        set => SetValue(ref consoleGamePassSiglId, value);
    }

    public DateTime? LastSuccessfulSynchronizationUtc
    {
        get => lastSuccessfulSynchronizationUtc;
        set => SetValue(ref lastSuccessfulSynchronizationUtc, value);
    }

    public int LastPcGamePassGameCount
    {
        get => lastPcGamePassGameCount;
        set => SetValue(ref lastPcGamePassGameCount, value);
    }

    public string? LastCountViewDescription
    {
        get => lastCountViewDescription;
        set => SetValue(ref lastCountViewDescription, value);
    }

    public DateTime? LastCountCatalogTimestampUtc
    {
        get => lastCountCatalogTimestampUtc;
        set => SetValue(ref lastCountCatalogTimestampUtc, value);
    }

    public string? LastSynchronizationError
    {
        get => lastSynchronizationError;
        set => SetValue(ref lastSynchronizationError, value);
    }

    public string? LastCatalogSource
    {
        get => lastCatalogSource;
        set => SetValue(ref lastCatalogSource, value);
    }

    public int LastRejectedNonPcCount
    {
        get => lastRejectedNonPcCount;
        set => SetValue(ref lastRejectedNonPcCount, value);
    }

    public int LastUnclassifiedCount
    {
        get => lastUnclassifiedCount;
        set => SetValue(ref lastUnclassifiedCount, value);
    }

    public int LastPcCatalogCount
    {
        get => lastPcCatalogCount;
        set => SetValue(ref lastPcCatalogCount, value);
    }

    public int LastConsoleCatalogCount
    {
        get => lastConsoleCatalogCount;
        set => SetValue(ref lastConsoleCatalogCount, value);
    }

    public int LastBothGameCount
    {
        get => lastBothGameCount;
        set => SetValue(ref lastBothGameCount, value);
    }
}

public sealed class LocaleOption
{
    public LocaleOption(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    public string Code { get; }

    public string DisplayName { get; }
}

public sealed class CatalogSelectionOption
{
    public CatalogSelectionOption(GamePassCatalogSelection value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public GamePassCatalogSelection Value { get; }

    public string DisplayName { get; }
}

public sealed class PlanSelectionOption
{
    public PlanSelectionOption(GamePassPlanSelection value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public GamePassPlanSelection Value { get; }

    public string DisplayName { get; }
}

public sealed class ConsoleSelectionOption
{
    public ConsoleSelectionOption(GamePassConsoleSelection value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public GamePassConsoleSelection Value { get; }

    public string DisplayName { get; }
}

public sealed class UnavailableHandlingOption
{
    public UnavailableHandlingOption(UnavailableGameHandling value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public UnavailableGameHandling Value { get; }

    public string DisplayName { get; }
}

public sealed class InstallAppOption
{
    public InstallAppOption(GamePassInstallApp value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public GamePassInstallApp Value { get; }

    public string DisplayName { get; }
}

public sealed class SubscriptionLibrariesSettingsViewModel : ObservableObject, ISettings
{
    private readonly SubscriptionLibrariesPlugin plugin;
    private SubscriptionLibrariesSettings? editingClone;
    private SubscriptionLibrariesSettings settings;
    private bool isRefreshing;
    private bool isEditing;
    private string? refreshStatus;

    public SubscriptionLibrariesSettingsViewModel(SubscriptionLibrariesPlugin plugin)
    {
        this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        settings = plugin.LoadPluginSettings<SubscriptionLibrariesSettings>() ??
            new SubscriptionLibrariesSettings();
        RepairDefaults(settings);
        settings.PropertyChanged += OnSettingsPropertyChanged;
        RefreshNowCommand = new RelayCommand(
            async () => await RefreshNowAsync().ConfigureAwait(true),
            () => !IsRefreshing);
    }

    public SubscriptionLibrariesSettings Settings
    {
        get => settings;
        private set
        {
            settings.PropertyChanged -= OnSettingsPropertyChanged;
            settings = value;
            settings.PropertyChanged += OnSettingsPropertyChanged;
            OnPropertyChanged();
            RaiseStatusProperties();
            RaiseSelectionProperties();
        }
    }

    public IReadOnlyList<LocaleOption> Regions { get; } = new[]
    {
        new LocaleOption("US", "United States (US)"),
        new LocaleOption("CA", "Canada (CA)"),
        new LocaleOption("GB", "United Kingdom (GB)"),
        new LocaleOption("AU", "Australia (AU)"),
        new LocaleOption("DE", "Germany (DE)"),
        new LocaleOption("FR", "France (FR)"),
        new LocaleOption("JP", "Japan (JP)")
    };

    public IReadOnlyList<LocaleOption> Languages { get; } = new[]
    {
        new LocaleOption("en-US", "English - United States (en-US)"),
        new LocaleOption("en-GB", "English - United Kingdom (en-GB)"),
        new LocaleOption("de-DE", "German (de-DE)"),
        new LocaleOption("fr-FR", "French (fr-FR)"),
        new LocaleOption("ja-JP", "Japanese (ja-JP)")
    };

    public IReadOnlyList<CatalogSelectionOption> GamePassSelections { get; } = new[]
    {
        new CatalogSelectionOption(GamePassCatalogSelection.PcOnly, "PC games only"),
        new CatalogSelectionOption(GamePassCatalogSelection.XboxOnly, "Xbox console games only"),
        new CatalogSelectionOption(GamePassCatalogSelection.Both, "PC and Xbox console games")
    };

    public IReadOnlyList<PlanSelectionOption> GamePassPlans { get; } = new[]
    {
        new PlanSelectionOption(GamePassPlanSelection.PcGamePass, "PC Game Pass"),
        new PlanSelectionOption(GamePassPlanSelection.Essential, "Xbox Game Pass Essential"),
        new PlanSelectionOption(GamePassPlanSelection.Premium, "Xbox Game Pass Premium"),
        new PlanSelectionOption(GamePassPlanSelection.Ultimate, "Xbox Game Pass Ultimate"),
        new PlanSelectionOption(GamePassPlanSelection.XboxGamePassConsole,
            "Xbox Game Pass for Console (legacy subscribers)"),
        new PlanSelectionOption(GamePassPlanSelection.AllCatalogs,
            "All catalogs (ignore subscription plan)")
    };

    public IReadOnlyList<ConsoleSelectionOption> ConsoleSelections { get; } = new[]
    {
        new ConsoleSelectionOption(GamePassConsoleSelection.Both,
            "Xbox One and Xbox Series X|S"),
        new ConsoleSelectionOption(GamePassConsoleSelection.XboxOne, "Xbox One only"),
        new ConsoleSelectionOption(GamePassConsoleSelection.SeriesXorS,
            "Xbox Series X|S only")
    };

    public IReadOnlyList<UnavailableHandlingOption> UnavailableHandlingOptions { get; } = new[]
    {
        new UnavailableHandlingOption(UnavailableGameHandling.KeepAndMark,
            "Keep and mark unavailable (default)"),
        new UnavailableHandlingOption(UnavailableGameHandling.Hide,
            "Hide unavailable entries (reversible)"),
        new UnavailableHandlingOption(UnavailableGameHandling.RemoveUnplayedAndHideRest,
            "Remove unplayed entries; hide played/installed entries")
    };

    public IReadOnlyList<InstallAppOption> InstallAppOptions { get; } = new[]
    {
        new InstallAppOption(GamePassInstallApp.XboxApp, "Xbox app (default)"),
        new InstallAppOption(GamePassInstallApp.MicrosoftStore, "Microsoft Store")
    };

    public ICommand RefreshNowCommand { get; }

    public bool IsRefreshing
    {
        get => isRefreshing;
        private set
        {
            SetValue(ref isRefreshing, value);
            OnPropertyChanged(nameof(CanEditSettings));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool CanEditSettings => !IsRefreshing;

    internal bool IsEditing => isEditing;

    public bool ConsoleGenerationEnabled =>
        Settings.GamePassCatalogSelection != GamePassCatalogSelection.PcOnly &&
        Settings.GamePassPlanSelection != GamePassPlanSelection.PcGamePass;

    public string SelectionGuidance
    {
        get
        {
            if (Settings.GamePassPlanSelection == GamePassPlanSelection.PcGamePass)
            {
                return Settings.GamePassCatalogSelection == GamePassCatalogSelection.XboxOnly
                    ? "PC Game Pass has no Xbox console catalog. Choose PC games or another plan."
                    : "PC Game Pass includes PC games only; the Xbox generation setting has no effect.";
            }

            if (Settings.GamePassPlanSelection == GamePassPlanSelection.XboxGamePassConsole)
            {
                return Settings.GamePassCatalogSelection == GamePassCatalogSelection.PcOnly
                    ? "Legacy Xbox Game Pass for Console has no PC catalog. Choose Xbox games or another plan."
                    : "The legacy console plan includes Xbox games only; selecting PC adds no games.";
            }

            return Settings.GamePassCatalogSelection == GamePassCatalogSelection.PcOnly
                ? "Xbox console generation does not affect a PC-only view."
                : "Xbox console generation filters console games; PC games are unaffected.";
        }
    }

    public string? RefreshStatus
    {
        get => refreshStatus;
        private set => SetValue(ref refreshStatus, value);
    }

    public string LastSuccessfulSynchronizationDisplay =>
        Settings.LastSuccessfulSynchronizationUtc is DateTime utc
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("g")
            : "Never";

    public string LastGameCountDisplay
    {
        get
        {
            if (Settings.LastCountCatalogTimestampUtc is not DateTime timestamp ||
                string.IsNullOrWhiteSpace(Settings.LastCountViewDescription))
            {
                return Settings.LastSuccessfulSynchronizationUtc is null
                    ? "No catalog checked yet"
                    : $"{Settings.LastPcGamePassGameCount:N0} (view not recorded; refresh to update)";
            }

            var localTime = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
                .ToLocalTime().ToString("g");
            return $"{Settings.LastPcGamePassGameCount:N0} - " +
                $"{Settings.LastCountViewDescription}; catalog verified {localTime}";
        }
    }

    public string LastSynchronizationErrorDisplay =>
        string.IsNullOrWhiteSpace(Settings.LastSynchronizationError)
            ? "None"
            : Settings.LastSynchronizationError!;

    public string CatalogVerificationDisplay
    {
        get
        {
            if (Settings.LastSuccessfulSynchronizationUtc is not DateTime verified)
            {
                return "Never verified";
            }

            var age = DateTime.UtcNow - DateTime.SpecifyKind(verified, DateTimeKind.Utc);
            return $"{Settings.LastCatalogSource ?? "Unknown"}; " +
                $"last live verification {Math.Max(0, (int)age.TotalHours)} hours ago";
        }
    }

    public string LastDiagnosticsDisplay =>
        $"PC catalog: {Settings.LastPcCatalogCount:N0}; " +
        $"Xbox catalog: {Settings.LastConsoleCatalogCount:N0}; " +
        $"both: {Settings.LastBothGameCount:N0}; " +
        $"PC entries without Windows evidence: {Settings.LastRejectedNonPcCount:N0}; " +
        $"unclassified: {Settings.LastUnclassifiedCount:N0}";

    public void BeginEdit()
    {
        editingClone = Serialization.GetClone(Settings);
        isEditing = true;
    }

    public void CancelEdit()
    {
        if (editingClone is not null)
        {
            Settings = editingClone;
        }

        isEditing = false;
    }

    public void EndEdit()
    {
        isEditing = false;
        plugin.SavePluginSettings(Settings);
    }

    internal void CommitAppliedSettings()
    {
        plugin.SavePluginSettings(Settings);
        if (isEditing)
        {
            editingClone = Serialization.GetClone(Settings);
        }
    }

    public bool VerifySettings(out List<string> errors)
    {
        errors = new List<string>();
        if (Settings.CacheDurationHours < 1 || Settings.CacheDurationHours > 720)
        {
            errors.Add("Cache duration must be between 1 and 720 hours.");
        }

        if (!Enum.IsDefined(typeof(GamePassCatalogSelection), Settings.GamePassCatalogSelection))
        {
            errors.Add("Choose PC, Xbox console, or both Game Pass catalogs.");
        }

        if (!Enum.IsDefined(typeof(GamePassPlanSelection), Settings.GamePassPlanSelection))
        {
            errors.Add("Choose a Game Pass subscription plan.");
        }

        if (!Enum.IsDefined(typeof(GamePassConsoleSelection), Settings.GamePassConsoleSelection))
        {
            errors.Add("Choose an Xbox console generation.");
        }

        if (!Enum.IsDefined(typeof(GamePassInstallApp), Settings.PreferredInstallApp))
        {
            errors.Add("Choose the Xbox app or Microsoft Store for install pages.");
        }

        if (!Enum.IsDefined(typeof(UnavailableGameHandling), Settings.UnavailableGameHandling))
        {
            errors.Add("Choose how to handle unavailable Game Pass entries.");
        }

        try
        {
            _ = new GamePassProviderOptions
            {
                Region = Settings.Region,
                Language = Settings.Language,
                SiglId = Settings.GamePassSiglId,
                ConsoleSiglId = Settings.ConsoleGamePassSiglId
            }.NormalizeAndValidate();
        }
        catch (ArgumentException exception)
        {
            errors.Add(exception.Message);
        }

        return errors.Count == 0;
    }

    internal void RecordSyncResult(SubscriptionSyncResult result)
    {
        Settings.LastPcGamePassGameCount = result.Games.Count(game =>
            game.Availability != SubscriptionAvailability.Removed && Settings.Includes(game));
        var platform = Settings.GamePassCatalogSelection switch
        {
            GamePassCatalogSelection.PcOnly => "PC",
            GamePassCatalogSelection.XboxOnly => "Xbox",
            _ => "PC + Xbox"
        };
        var generation = ConsoleGenerationEnabled
            ? Settings.GamePassConsoleSelection switch
            {
                GamePassConsoleSelection.XboxOne => " / Xbox One",
                GamePassConsoleSelection.SeriesXorS => " / Series X|S",
                _ => " / both Xbox generations"
            }
            : string.Empty;
        Settings.LastCountViewDescription =
            $"{Settings.GamePassPlanSelection.DisplayName()} / {platform}{generation} / " +
            $"{Settings.Region} {Settings.Language}" +
            (Settings.ExcludeConfirmedFreeToPlay ? " / excluding free-to-play" : string.Empty);
        Settings.LastCountCatalogTimestampUtc = result.CatalogTimestampUtc.UtcDateTime;
        Settings.LastCatalogSource = result.Source.ToString();
        if (result.Source == SubscriptionCatalogSource.Live)
        {
            Settings.LastSuccessfulSynchronizationUtc = result.CatalogTimestampUtc.UtcDateTime;
            Settings.LastSynchronizationError = null;
        }

        if (!string.IsNullOrWhiteSpace(result.Warning))
        {
            Settings.LastSynchronizationError = result.Warning;
        }

        if (result.Diagnostics is not null)
        {
            Settings.LastPcCatalogCount = result.Diagnostics.ProductsIdentifiedAsPc;
            Settings.LastConsoleCatalogCount = result.Diagnostics.ProductsIdentifiedAsConsole;
            Settings.LastBothGameCount = result.Diagnostics.ProductsInBothCatalogs;
            Settings.LastRejectedNonPcCount = result.Diagnostics.ProductsRejectedAsNonPc;
            Settings.LastUnclassifiedCount =
                result.Diagnostics.ProductsThatCouldNotBeClassified +
                result.Diagnostics.ProductIdsWithoutMetadata;
        }

        if (!isEditing)
        {
            plugin.SavePluginSettings(Settings);
        }
        RaiseStatusProperties();
    }

    internal void RecordSyncError(Exception exception)
    {
        Settings.LastSynchronizationError = exception.Message;
        if (!isEditing)
        {
            plugin.SavePluginSettings(Settings);
        }
        RaiseStatusProperties();
    }

    private async Task RefreshNowAsync()
    {
        IsRefreshing = true;
        RefreshStatus = "Refreshing Game Pass and preparing a library preview...";
        try
        {
            RefreshStatus = await plugin.RefreshAndApplyAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            RecordSyncError(exception);
            RefreshStatus = $"Refresh failed: {exception.Message}";
            plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                exception.Message,
                "Game Pass refresh and apply failed");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void RaiseStatusProperties()
    {
        OnPropertyChanged(nameof(LastSuccessfulSynchronizationDisplay));
        OnPropertyChanged(nameof(LastGameCountDisplay));
        OnPropertyChanged(nameof(LastSynchronizationErrorDisplay));
        OnPropertyChanged(nameof(CatalogVerificationDisplay));
        OnPropertyChanged(nameof(LastDiagnosticsDisplay));
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) ||
            args.PropertyName == nameof(SubscriptionLibrariesSettings.GamePassPlanSelection) ||
            args.PropertyName == nameof(SubscriptionLibrariesSettings.GamePassCatalogSelection))
        {
            RaiseSelectionProperties();
        }
    }

    private void RaiseSelectionProperties()
    {
        OnPropertyChanged(nameof(ConsoleGenerationEnabled));
        OnPropertyChanged(nameof(SelectionGuidance));
    }

    private static void RepairDefaults(SubscriptionLibrariesSettings value)
    {
        value.Region = string.IsNullOrWhiteSpace(value.Region) ? "US" : value.Region;
        value.Language = string.IsNullOrWhiteSpace(value.Language) ? "en-US" : value.Language;
        value.GamePassSiglId = string.IsNullOrWhiteSpace(value.GamePassSiglId)
            ? GamePassConstants.PcCatalogSiglId
            : value.GamePassSiglId;
        value.ConsoleGamePassSiglId = string.IsNullOrWhiteSpace(value.ConsoleGamePassSiglId)
            ? GamePassConstants.ConsoleCatalogSiglId
            : value.ConsoleGamePassSiglId;
        if (!Enum.IsDefined(typeof(GamePassCatalogSelection), value.GamePassCatalogSelection))
        {
            value.GamePassCatalogSelection = GamePassCatalogSelection.PcOnly;
        }
        if (!Enum.IsDefined(typeof(GamePassPlanSelection), value.GamePassPlanSelection))
        {
            value.GamePassPlanSelection = GamePassPlanSelection.PcGamePass;
        }
        if (!Enum.IsDefined(typeof(GamePassConsoleSelection), value.GamePassConsoleSelection))
        {
            value.GamePassConsoleSelection = GamePassConsoleSelection.Both;
        }
        if (!Enum.IsDefined(typeof(GamePassInstallApp), value.PreferredInstallApp))
        {
            value.PreferredInstallApp = GamePassInstallApp.XboxApp;
        }
        if (!Enum.IsDefined(typeof(UnavailableGameHandling), value.UnavailableGameHandling))
        {
            value.UnavailableGameHandling = UnavailableGameHandling.KeepAndMark;
        }
        value.CacheDurationHours = value.CacheDurationHours <= 0 ? 24 : value.CacheDurationHours;
    }
}
