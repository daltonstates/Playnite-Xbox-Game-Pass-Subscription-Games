using System;

namespace SubscriptionLibraries.Core.Providers.GamePass;

/// <summary>
/// Product pages where a user can choose to install a PC game. Opening either
/// page does not start or confirm an installation.
/// </summary>
public sealed class GamePassProductPages
{
    private GamePassProductPages(string productId)
    {
        XboxApp = new Uri($"msxbox://game/?productId={productId}");
        MicrosoftStore = new Uri($"ms-windows-store://pdp/?ProductId={productId}");
    }

    public Uri XboxApp { get; }

    public Uri MicrosoftStore { get; }

    public static GamePassProductPages? FromProductId(string? productId)
    {
        var id = productId?.Trim();
        if (id is null || id.Length != 12)
        {
            return null;
        }

        foreach (var character in id)
        {
            if (!((character >= 'A' && character <= 'Z') ||
                  (character >= 'a' && character <= 'z') ||
                  (character >= '0' && character <= '9')))
            {
                return null;
            }
        }

        return new GamePassProductPages(id.ToUpperInvariant());
    }
}
