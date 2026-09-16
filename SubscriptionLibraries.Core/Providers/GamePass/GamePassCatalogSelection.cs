using System;
using SubscriptionLibraries.Core.Models;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public enum GamePassCatalogSelection
{
    PcOnly,
    XboxOnly,
    Both
}

public static class GamePassCatalogSelectionExtensions
{
    public static bool Includes(this GamePassCatalogSelection selection, SubscriptionGame game)
    {
        if (game is null)
        {
            throw new ArgumentNullException(nameof(game));
        }

        return selection switch
        {
            GamePassCatalogSelection.PcOnly =>
                (game.AccessPlatforms & SubscriptionPlatforms.WindowsPc) != 0,
            GamePassCatalogSelection.XboxOnly =>
                (game.AccessPlatforms & SubscriptionPlatforms.XboxConsole) != 0,
            GamePassCatalogSelection.Both => game.AccessPlatforms != SubscriptionPlatforms.None,
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
    }

    public static bool Includes(
        this GamePassCatalogSelection selection,
        GamePassPlanSelection plan,
        SubscriptionGame game)
    {
        var platforms = plan.EligiblePlatforms(game);
        return selection switch
        {
            GamePassCatalogSelection.PcOnly =>
                (platforms & SubscriptionPlatforms.WindowsPc) != 0,
            GamePassCatalogSelection.XboxOnly =>
                (platforms & SubscriptionPlatforms.XboxConsole) != 0,
            GamePassCatalogSelection.Both => platforms != SubscriptionPlatforms.None,
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
    }

    public static bool Includes(
        this GamePassCatalogSelection selection,
        GamePassPlanSelection plan,
        GamePassConsoleSelection consoleSelection,
        SubscriptionGame game)
    {
        var platforms = consoleSelection.EligiblePlatforms(plan, game);
        return selection switch
        {
            GamePassCatalogSelection.PcOnly =>
                (platforms & SubscriptionPlatforms.WindowsPc) != 0,
            GamePassCatalogSelection.XboxOnly =>
                (platforms & SubscriptionPlatforms.XboxConsole) != 0,
            GamePassCatalogSelection.Both => platforms != SubscriptionPlatforms.None,
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
    }
}
