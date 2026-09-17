using System;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace SubscriptionLibraries.Services;

internal sealed class StorePageInstallController : InstallController
{
    private readonly Action openPage;

    public StorePageInstallController(Game game, string appName, Action openPage)
        : base(game)
    {
        Name = $"Open {appName} page to install";
        this.openPage = openPage ?? throw new ArgumentNullException(nameof(openPage));
    }

    public override void Install(InstallActionArgs args)
    {
        openPage();
        // The app owns the download. Clear Playnite's temporary installing
        // state without claiming this catalog entry was installed.
        InvokeOnInstallationCancelled(new GameInstallationCancelledEventArgs());
    }
}
