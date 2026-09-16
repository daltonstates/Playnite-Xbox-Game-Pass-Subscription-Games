using System;

namespace SubscriptionLibraries.Core.Models;

public enum UnavailableGameHandling
{
    KeepAndMark,
    Hide,
    RemoveUnplayedAndHideRest
}

public enum UnavailableGameAction
{
    Keep,
    Hide,
    Remove
}

public static class UnavailableGamePolicy
{
    public static UnavailableGameAction Decide(
        UnavailableGameHandling handling,
        bool catalogIsFresh,
        ulong playtime,
        bool isInstalled,
        bool isBusy,
        bool wasHiddenByUser)
    {
        return handling switch
        {
            UnavailableGameHandling.KeepAndMark => UnavailableGameAction.Keep,
            UnavailableGameHandling.Hide => UnavailableGameAction.Hide,
            UnavailableGameHandling.RemoveUnplayedAndHideRest =>
                catalogIsFresh && playtime == 0 && !isInstalled && !isBusy && !wasHiddenByUser
                    ? UnavailableGameAction.Remove
                    : UnavailableGameAction.Hide,
            _ => throw new ArgumentOutOfRangeException(nameof(handling))
        };
    }
}
