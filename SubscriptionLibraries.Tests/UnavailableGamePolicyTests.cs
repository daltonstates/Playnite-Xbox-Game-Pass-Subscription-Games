using SubscriptionLibraries.Core.Models;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class UnavailableGamePolicyTests
{
    [Theory]
    [InlineData(UnavailableGameHandling.KeepAndMark, true, 0UL, false, false, false,
        UnavailableGameAction.Keep)]
    [InlineData(UnavailableGameHandling.Hide, true, 0UL, false, false, false,
        UnavailableGameAction.Hide)]
    [InlineData(UnavailableGameHandling.RemoveUnplayedAndHideRest, true, 0UL, false, false, false,
        UnavailableGameAction.Remove)]
    [InlineData(UnavailableGameHandling.RemoveUnplayedAndHideRest, false, 0UL, false, false, false,
        UnavailableGameAction.Hide)]
    [InlineData(UnavailableGameHandling.RemoveUnplayedAndHideRest, true, 1UL, false, false, false,
        UnavailableGameAction.Hide)]
    [InlineData(UnavailableGameHandling.RemoveUnplayedAndHideRest, true, 0UL, true, false, false,
        UnavailableGameAction.Hide)]
    [InlineData(UnavailableGameHandling.RemoveUnplayedAndHideRest, true, 0UL, false, true, false,
        UnavailableGameAction.Hide)]
    [InlineData(UnavailableGameHandling.RemoveUnplayedAndHideRest, true, 0UL, false, false, true,
        UnavailableGameAction.Hide)]
    public void ChoosesSafeAction(
        UnavailableGameHandling handling,
        bool fresh,
        ulong playtime,
        bool installed,
        bool busy,
        bool userHidden,
        UnavailableGameAction expected)
    {
        Assert.Equal(expected,
            UnavailableGamePolicy.Decide(handling, fresh, playtime, installed, busy, userHidden));
    }
}
