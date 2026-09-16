using System;
using System.Collections.Generic;
using SubscriptionLibraries.Core.Models;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public enum GamePassPlanSelection
{
    AllCatalogs,
    PcGamePass,
    XboxGamePassConsole,
    Essential,
    Premium,
    Ultimate
}

public static class GamePassPlanSelectionExtensions
{
    public static string DisplayName(this GamePassPlanSelection plan) => plan switch
    {
        GamePassPlanSelection.AllCatalogs => "All Game Pass catalogs",
        GamePassPlanSelection.PcGamePass => "PC Game Pass",
        GamePassPlanSelection.XboxGamePassConsole => "Xbox Game Pass for Console (legacy)",
        GamePassPlanSelection.Essential => "Xbox Game Pass Essential",
        GamePassPlanSelection.Premium => "Xbox Game Pass Premium",
        GamePassPlanSelection.Ultimate => "Xbox Game Pass Ultimate",
        _ => throw new ArgumentOutOfRangeException(nameof(plan))
    };

    public static string Key(this GamePassPlanSelection plan) => plan switch
    {
        GamePassPlanSelection.PcGamePass => "pc-game-pass",
        GamePassPlanSelection.XboxGamePassConsole => "xbox-game-pass-console",
        GamePassPlanSelection.Essential => "essential",
        GamePassPlanSelection.Premium => "premium",
        GamePassPlanSelection.Ultimate => "ultimate",
        _ => throw new ArgumentOutOfRangeException(nameof(plan),
            "All catalogs is a view, not a subscription plan.")
    };

    public static SubscriptionPlatforms EligiblePlatforms(
        this GamePassPlanSelection plan,
        SubscriptionGame game)
    {
        if (game is null)
        {
            throw new ArgumentNullException(nameof(game));
        }

        if (plan == GamePassPlanSelection.AllCatalogs)
        {
            return game.AccessPlatforms;
        }

        return game.PlanPlatforms is not null &&
               game.PlanPlatforms.TryGetValue(plan.Key(), out var platforms)
            ? platforms & game.AccessPlatforms
            : SubscriptionPlatforms.None;
    }

    public static SubscriptionGame Project(this GamePassPlanSelection plan, SubscriptionGame game)
    {
        var platforms = plan.EligiblePlatforms(game);
        if (platforms == SubscriptionPlatforms.None)
        {
            throw new ArgumentException("The game is not in the selected plan.", nameof(game));
        }

        var projected = game.WithAccess(platforms, plan.DisplayName());
        if (plan != GamePassPlanSelection.AllCatalogs)
        {
            projected.XboxGenerations = game.PlanXboxGenerations is not null &&
                game.PlanXboxGenerations.TryGetValue(plan.Key(), out var generations)
                    ? generations : XboxConsoleGenerations.None;
            projected.Availability = game.LeavingSoonPlanPlatforms is not null &&
                game.LeavingSoonPlanPlatforms.TryGetValue(plan.Key(), out var leavingPlatforms) &&
                (leavingPlatforms & platforms) != 0
                    ? SubscriptionAvailability.LeavingSoon
                    : SubscriptionAvailability.Active;
        }
        return projected;
    }

    public static SubscriptionGame Project(
        this GamePassPlanSelection plan, GamePassConsoleSelection consoleSelection,
        SubscriptionGame game)
    {
        var platforms = consoleSelection.EligiblePlatforms(plan, game);
        if (platforms == SubscriptionPlatforms.None)
        {
            throw new ArgumentException("The game is not in the selected plan and console view.",
                nameof(game));
        }

        var projected = plan.Project(game);
        projected.AccessPlatforms = platforms;
        projected.Platform = SubscriptionPlatformNames.Format(platforms);
        if ((platforms & SubscriptionPlatforms.XboxConsole) == 0)
        {
            projected.XboxGenerations = XboxConsoleGenerations.None;
        }
        if (plan != GamePassPlanSelection.AllCatalogs)
        {
            projected.Availability = game.LeavingSoonPlanPlatforms is not null &&
                game.LeavingSoonPlanPlatforms.TryGetValue(plan.Key(), out var leavingPlatforms) &&
                (leavingPlatforms & platforms) != 0
                    ? SubscriptionAvailability.LeavingSoon
                    : SubscriptionAvailability.Active;
        }
        return projected;
    }
}

public sealed class GamePassPlanCatalog
{
    public GamePassPlanCatalog(
        GamePassPlanSelection plan,
        SubscriptionPlatforms platform,
        string siglId,
        string platformContext,
        string subscriptionContext)
    {
        Plan = plan;
        Platform = platform;
        SiglId = siglId;
        PlatformContext = platformContext;
        SubscriptionContext = subscriptionContext;
    }

    public GamePassPlanSelection Plan { get; }

    public SubscriptionPlatforms Platform { get; }

    public string SiglId { get; }

    public string PlatformContext { get; }

    public XboxConsoleGenerations ConsoleGeneration => PlatformContext switch
    {
        "ConsoleGen8" => XboxConsoleGenerations.XboxOne,
        "ConsoleGen9" => XboxConsoleGenerations.SeriesXorS,
        _ => XboxConsoleGenerations.None
    };

    public string SubscriptionContext { get; }
}
