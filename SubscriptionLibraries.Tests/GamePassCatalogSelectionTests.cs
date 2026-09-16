using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Providers.GamePass;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class GamePassCatalogSelectionTests
{
    [Fact]
    public void PlanAndPlatformSelectionsAreIndependent()
    {
        var game = new SubscriptionGame
        {
            AccessPlatforms = SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole,
            PlanPlatforms = new Dictionary<string, SubscriptionPlatforms>
            {
                [GamePassPlanSelection.PcGamePass.Key()] = SubscriptionPlatforms.WindowsPc,
                [GamePassPlanSelection.Essential.Key()] = SubscriptionPlatforms.XboxConsole,
                [GamePassPlanSelection.Ultimate.Key()] =
                    SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole
            }
        };

        Assert.True(GamePassCatalogSelection.PcOnly.Includes(GamePassPlanSelection.PcGamePass, game));
        Assert.False(GamePassCatalogSelection.XboxOnly.Includes(GamePassPlanSelection.PcGamePass, game));
        Assert.False(GamePassCatalogSelection.PcOnly.Includes(GamePassPlanSelection.Essential, game));
        Assert.True(GamePassCatalogSelection.XboxOnly.Includes(GamePassPlanSelection.Essential, game));
        Assert.True(GamePassCatalogSelection.Both.Includes(GamePassPlanSelection.Ultimate, game));
        Assert.Equal(SubscriptionPlatforms.XboxConsole,
            GamePassPlanSelection.Essential.Project(game).AccessPlatforms);
    }

    [Fact]
    public void UnknownPlanMembershipFailsClosed()
    {
        var game = new SubscriptionGame { AccessPlatforms = SubscriptionPlatforms.WindowsPc };

        Assert.False(GamePassCatalogSelection.Both.Includes(GamePassPlanSelection.Premium, game));
        Assert.True(GamePassCatalogSelection.PcOnly.Includes(GamePassPlanSelection.AllCatalogs, game));
    }

    [Fact]
    public void XboxGenerationSelectionUsesMembershipForSelectedPlan()
    {
        var game = new SubscriptionGame
        {
            AccessPlatforms = SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole,
            XboxGenerations = XboxConsoleGenerations.Both,
            PlanPlatforms = new Dictionary<string, SubscriptionPlatforms>
            {
                [GamePassPlanSelection.Essential.Key()] = SubscriptionPlatforms.XboxConsole,
                [GamePassPlanSelection.Ultimate.Key()] =
                    SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole
            },
            PlanXboxGenerations = new Dictionary<string, XboxConsoleGenerations>
            {
                [GamePassPlanSelection.Essential.Key()] = XboxConsoleGenerations.XboxOne,
                [GamePassPlanSelection.Ultimate.Key()] = XboxConsoleGenerations.Both
            }
        };

        Assert.True(GamePassCatalogSelection.XboxOnly.Includes(
            GamePassPlanSelection.Essential, GamePassConsoleSelection.XboxOne, game));
        Assert.False(GamePassCatalogSelection.XboxOnly.Includes(
            GamePassPlanSelection.Essential, GamePassConsoleSelection.SeriesXorS, game));
        Assert.True(GamePassCatalogSelection.XboxOnly.Includes(
            GamePassPlanSelection.Ultimate, GamePassConsoleSelection.SeriesXorS, game));
        Assert.Equal(XboxConsoleGenerations.XboxOne,
            GamePassPlanSelection.Essential.Project(
                GamePassConsoleSelection.XboxOne, game).XboxGenerations);
    }

    [Fact]
    public void UnknownGenerationDoesNotEnterGenerationSpecificView()
    {
        var game = new SubscriptionGame
        {
            AccessPlatforms = SubscriptionPlatforms.XboxConsole,
            PlanPlatforms = new Dictionary<string, SubscriptionPlatforms>
            {
                [GamePassPlanSelection.Ultimate.Key()] = SubscriptionPlatforms.XboxConsole
            }
        };

        Assert.False(GamePassCatalogSelection.XboxOnly.Includes(
            GamePassPlanSelection.Ultimate, GamePassConsoleSelection.XboxOne, game));
        Assert.True(GamePassCatalogSelection.XboxOnly.Includes(
            GamePassPlanSelection.Ultimate, GamePassConsoleSelection.Both, game));
    }

    [Fact]
    public void ConsoleLeavingSoonDoesNotWarnPcGamePassForSameProduct()
    {
        var game = new SubscriptionGame
        {
            Availability = SubscriptionAvailability.LeavingSoon,
            AccessPlatforms = SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole,
            PlanPlatforms = new Dictionary<string, SubscriptionPlatforms>
            {
                [GamePassPlanSelection.PcGamePass.Key()] = SubscriptionPlatforms.WindowsPc,
                [GamePassPlanSelection.Ultimate.Key()] =
                    SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole
            },
            LeavingSoonPlanPlatforms = new Dictionary<string, SubscriptionPlatforms>
            {
                [GamePassPlanSelection.Ultimate.Key()] = SubscriptionPlatforms.XboxConsole
            }
        };

        Assert.Equal(SubscriptionAvailability.Active,
            GamePassPlanSelection.PcGamePass.Project(game).Availability);
        Assert.Equal(SubscriptionAvailability.LeavingSoon,
            GamePassPlanSelection.Ultimate.Project(game).Availability);
        Assert.Equal(SubscriptionAvailability.Active,
            GamePassPlanSelection.Ultimate.Project(
                GamePassConsoleSelection.SeriesXorS, game).Availability);
    }

    [Theory]
    [InlineData(GamePassCatalogSelection.PcOnly, SubscriptionPlatforms.WindowsPc, true)]
    [InlineData(GamePassCatalogSelection.PcOnly, SubscriptionPlatforms.XboxConsole, false)]
    [InlineData(GamePassCatalogSelection.PcOnly, SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole, true)]
    [InlineData(GamePassCatalogSelection.XboxOnly, SubscriptionPlatforms.WindowsPc, false)]
    [InlineData(GamePassCatalogSelection.XboxOnly, SubscriptionPlatforms.XboxConsole, true)]
    [InlineData(GamePassCatalogSelection.XboxOnly, SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole, true)]
    [InlineData(GamePassCatalogSelection.Both, SubscriptionPlatforms.WindowsPc, true)]
    [InlineData(GamePassCatalogSelection.Both, SubscriptionPlatforms.XboxConsole, true)]
    [InlineData(GamePassCatalogSelection.Both, SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole, true)]
    public void SelectionIncludesExpectedCatalogs(
        GamePassCatalogSelection selection,
        SubscriptionPlatforms platforms,
        bool expected)
    {
        var game = new SubscriptionGame { AccessPlatforms = platforms };

        Assert.Equal(expected, selection.Includes(game));
    }
}
