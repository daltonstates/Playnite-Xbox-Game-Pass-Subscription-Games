using System;
using System.Collections.Generic;

namespace SubscriptionLibraries.Core.Models;

public sealed class SubscriptionGame
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string ProviderGameId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? MicrosoftProductId { get; set; }

    public string Platform { get; set; } = string.Empty;

    public SubscriptionPlatforms AccessPlatforms { get; set; }

    public XboxConsoleGenerations XboxGenerations { get; set; }

    public Dictionary<string, XboxConsoleGenerations> PlanXboxGenerations { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // A plan may grant access on PC, console, or both. These are public catalog
    // memberships, not a statement about the user's personal entitlement.
    public Dictionary<string, SubscriptionPlatforms> PlanPlatforms { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string SubscriptionTier { get; set; } = string.Empty;

    public Uri? StoreUri { get; set; }

    public Uri? ImageUri { get; set; }

    public Uri? BackgroundImageUri { get; set; }

    public string? Description { get; set; }

    public string? Publisher { get; set; }

    public string? Developer { get; set; }

    public DateTimeOffset? ReleaseDate { get; set; }

    public DateTimeOffset? DateAdded { get; set; }

    public DateTimeOffset? LeavingDate { get; set; }

    // Public Leaving Soon feeds are plan/platform-scoped, not a universal
    // product-level departure claim.
    public Dictionary<string, SubscriptionPlatforms> LeavingSoonPlanPlatforms { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Only set when the public Store purchase offer has a zero MSRP. A zero
    // subscription/license offer is not evidence that a game is free to play.
    public bool IsConfirmedFreeToPlay { get; set; }

    public SubscriptionAvailability Availability { get; set; } = SubscriptionAvailability.Active;

    public Dictionary<string, string> RawSourceIdentifiers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public SubscriptionGame WithAccess(SubscriptionPlatforms platforms, string tier) => new()
    {
        ProviderId = ProviderId,
        ProviderName = ProviderName,
        ProviderGameId = ProviderGameId,
        Name = Name,
        MicrosoftProductId = MicrosoftProductId,
        Platform = SubscriptionPlatformNames.Format(platforms),
        AccessPlatforms = platforms,
        XboxGenerations = (platforms & SubscriptionPlatforms.XboxConsole) != 0
            ? XboxGenerations : XboxConsoleGenerations.None,
        PlanXboxGenerations = new Dictionary<string, XboxConsoleGenerations>(
            PlanXboxGenerations ?? new Dictionary<string, XboxConsoleGenerations>(),
            StringComparer.OrdinalIgnoreCase),
        PlanPlatforms = new Dictionary<string, SubscriptionPlatforms>(
            PlanPlatforms ?? new Dictionary<string, SubscriptionPlatforms>(),
            StringComparer.OrdinalIgnoreCase),
        SubscriptionTier = tier,
        StoreUri = StoreUri,
        ImageUri = ImageUri,
        BackgroundImageUri = BackgroundImageUri,
        Description = Description,
        Publisher = Publisher,
        Developer = Developer,
        ReleaseDate = ReleaseDate,
        DateAdded = DateAdded,
        LeavingDate = LeavingDate,
        LeavingSoonPlanPlatforms = new Dictionary<string, SubscriptionPlatforms>(
            LeavingSoonPlanPlatforms ?? new Dictionary<string, SubscriptionPlatforms>(),
            StringComparer.OrdinalIgnoreCase),
        IsConfirmedFreeToPlay = IsConfirmedFreeToPlay,
        Availability = Availability,
        RawSourceIdentifiers = new Dictionary<string, string>(
            RawSourceIdentifiers ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase)
    };
}
