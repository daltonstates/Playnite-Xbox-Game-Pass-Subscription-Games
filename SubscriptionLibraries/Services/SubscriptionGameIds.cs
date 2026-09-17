using System;

namespace SubscriptionLibraries.Services;

internal static class SubscriptionGameIds
{
    // Game Pass keeps its historical Microsoft product IDs. Every new catalog
    // uses this prefix so one feed cannot reconcile another feed's records.
    public static bool IsNamespaced(string? gameId) =>
        gameId?.StartsWith("sub:", StringComparison.OrdinalIgnoreCase) == true;
}
