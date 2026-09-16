using System;
using SubscriptionLibraries.Core.Models;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public enum GamePassConsoleSelection
{
    Both,
    XboxOne,
    SeriesXorS
}

public static class GamePassConsoleSelectionExtensions
{
    public static XboxConsoleGenerations RequiredGenerations(
        this GamePassConsoleSelection selection) => selection switch
        {
            GamePassConsoleSelection.Both => XboxConsoleGenerations.Both,
            GamePassConsoleSelection.XboxOne => XboxConsoleGenerations.XboxOne,
            GamePassConsoleSelection.SeriesXorS => XboxConsoleGenerations.SeriesXorS,
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };

    public static SubscriptionPlatforms EligiblePlatforms(
        this GamePassConsoleSelection selection,
        GamePassPlanSelection plan,
        SubscriptionGame game)
    {
        var platforms = plan.EligiblePlatforms(game);
        if ((platforms & SubscriptionPlatforms.XboxConsole) == 0 ||
            selection == GamePassConsoleSelection.Both)
        {
            return platforms;
        }

        var generations = plan == GamePassPlanSelection.AllCatalogs
            ? game.XboxGenerations
            : game.PlanXboxGenerations is not null &&
              game.PlanXboxGenerations.TryGetValue(plan.Key(), out var planGenerations)
                ? planGenerations : XboxConsoleGenerations.None;
        return (generations & selection.RequiredGenerations()) != 0
            ? platforms
            : platforms & ~SubscriptionPlatforms.XboxConsole;
    }
}
