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

    public SubscriptionAvailability Availability { get; set; } = SubscriptionAvailability.Active;

    public Dictionary<string, string> RawSourceIdentifiers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
