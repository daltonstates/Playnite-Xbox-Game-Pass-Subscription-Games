using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Providers.UbisoftPlus;

namespace SubscriptionLibraries.Services;

internal static class PlayniteGameMapper
{
    internal const string HiddenBySelectionTag = "Subscription Libraries: Hidden by selection";
    internal const string ActiveAccessTag = "Access: Subscription";
    internal const string UnverifiedAccessTag = "Access: Catalog unverified";
    internal const string VerifiedLinkPrefix = "Catalog verified: ";
    internal const string PcOnlyMembershipTag = "Game Pass: PC only";
    internal const string PcAndXboxMembershipTag = "Game Pass: PC + Xbox";
    private const string RemovedAccessTag = "Access: Removed";
    private const string NotSelectedAccessTag = "Access: Outside selected catalog";
    private const string NotSelectedPlanTag = "Access: Not in selected plan";
    private const string NotSelectedPlatformTag = "Access: Outside selected platform";
    private const string ExcludedFreeToPlayTag = "Access: Excluded free-to-play";
    private const string LeavingTag = "Leaving Game Pass";
    private const string RecentlyAddedTag = "Recently Added to Game Pass";

    public static GameMetadata Map(
        SubscriptionGame game, DateTimeOffset now,
        bool verified = true, DateTimeOffset? catalogTimestamp = null)
    {
        if (game is null)
        {
            throw new ArgumentNullException(nameof(game));
        }

        var metadata = new GameMetadata
        {
            GameId = game.ProviderGameId,
            Name = game.Name,
            Description = game.Description,
            IsInstalled = false,
            Source = new MetadataNameProperty(game.ProviderName),
            Platforms = GetPlatformProperties(game),
            Tags = (verified ? GetActiveTagNames(game, now) : GetUnverifiedTagNames(game))
                .Select(name => (MetadataProperty)new MetadataNameProperty(name))
                .ToHashSet()
        };

        if (game.ReleaseDate is DateTimeOffset releaseDate)
        {
            metadata.ReleaseDate = new ReleaseDate(releaseDate.DateTime);
        }

        if (game.StoreUri is not null)
        {
            metadata.Links = new List<Link>
            {
                new(game.ProviderId == UbisoftPlusProvider.ProviderId
                    ? "Ubisoft Store" : "Microsoft Store", game.StoreUri.AbsoluteUri)
            };
        }

        if (catalogTimestamp is DateTimeOffset timestamp)
        {
            metadata.Links ??= new List<Link>();
            metadata.Links.Add(CreateVerificationLink(timestamp, game.ProviderId));
        }

        if (game.ImageUri is not null)
        {
            metadata.CoverImage = new MetadataFile(game.ImageUri.AbsoluteUri);
        }

        if (game.BackgroundImageUri is not null)
        {
            metadata.BackgroundImage = new MetadataFile(game.BackgroundImageUri.AbsoluteUri);
        }

        metadata.Developers = ToMetadataProperties(game.Developer);
        metadata.Publishers = ToMetadataProperties(game.Publisher);
        return metadata;
    }

    public static IReadOnlyCollection<string> GetActiveTagNames(
        SubscriptionGame game,
        DateTimeOffset now)
    {
        if (game is null)
        {
            throw new ArgumentNullException(nameof(game));
        }

        if (game.ProviderId == UbisoftPlusProvider.ProviderId)
        {
            var ubisoftTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                GetSubscriptionTag(game.ProviderName), ActiveAccessTag
            };
            if (game.PlanPlatforms.ContainsKey(UbisoftPlusProvider.ClassicsPlan))
            {
                ubisoftTags.Add("Ubisoft+ plan: Classics");
            }
            if (game.PlanPlatforms.ContainsKey(UbisoftPlusProvider.PremiumPlan))
            {
                ubisoftTags.Add("Ubisoft+ plan: Premium");
            }
            return ubisoftTags;
        }

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            GetSubscriptionTag(game.ProviderName),
            ActiveAccessTag
        };
        AddMembershipTag(tags, game.AccessPlatforms);
        AddConsoleGenerationTag(tags, game);
        if (!string.IsNullOrWhiteSpace(game.SubscriptionTier) &&
            !string.Equals(game.SubscriptionTier, GamePassConstants.SubscriptionTier,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(game.SubscriptionTier,
                GamePassPlanSelection.AllCatalogs.DisplayName(),
                StringComparison.OrdinalIgnoreCase))
        {
            tags.Add($"Game Pass plan: {game.SubscriptionTier}");
        }

        if (game.Availability == SubscriptionAvailability.LeavingSoon)
        {
            tags.Add(LeavingTag);
        }

        if (game.DateAdded is DateTimeOffset dateAdded &&
            dateAdded <= now &&
            now - dateAdded <= TimeSpan.FromDays(14))
        {
            tags.Add(RecentlyAddedTag);
        }

        return tags;
    }

    public static IReadOnlyCollection<string> GetUnverifiedTagNames(SubscriptionGame game) =>
        new[] { GetSubscriptionTag(game.ProviderName), UnverifiedAccessTag };

    public static Link CreateVerificationLink(DateTimeOffset timestamp,
        string providerId = GamePassConstants.ProviderId) => new(
        VerifiedLinkPrefix + timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'"),
        providerId == UbisoftPlusProvider.ProviderId
            ? "https://store.ubisoft.com/us/ubisoftplus/games?lang=en_US"
            : "https://www.xbox.com/xbox-game-pass/games");

    public static IReadOnlyCollection<string> GetRemovedTagNames(string providerName) =>
        new[]
        {
            GetSubscriptionTag(providerName),
            RemovedAccessTag,
            $"Left {providerName}"
        };

    public static IReadOnlyCollection<string> GetNotSelectedTagNames(
        SubscriptionGame game,
        GamePassPlanSelection plan,
        bool excludedFreeToPlay = false)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            GetSubscriptionTag(game.ProviderName),
            excludedFreeToPlay
                ? ExcludedFreeToPlayTag
                : plan.EligiblePlatforms(game) == SubscriptionPlatforms.None
                ? NotSelectedPlanTag
                : NotSelectedPlatformTag
        };
        AddMembershipTag(tags, game.AccessPlatforms);
        AddConsoleGenerationTag(tags, game);
        return tags;
    }

    public static IReadOnlyCollection<string> GetManagedTagNames(string providerName) =>
        string.Equals(providerName, UbisoftPlusProvider.ProviderName,
            StringComparison.OrdinalIgnoreCase)
            ? new[]
            {
                GetSubscriptionTag(providerName), ActiveAccessTag,
                UnverifiedAccessTag, RemovedAccessTag, NotSelectedAccessTag,
                NotSelectedPlanTag, $"Left {providerName}",
                "Ubisoft+ plan: Classics", "Ubisoft+ plan: Premium",
                HiddenBySelectionTag
            }
            : new[]
        {
            GetSubscriptionTag(providerName),
            ActiveAccessTag,
            UnverifiedAccessTag,
            RemovedAccessTag,
            NotSelectedAccessTag,
            NotSelectedPlanTag,
            NotSelectedPlatformTag,
            ExcludedFreeToPlayTag,
            LeavingTag,
            RecentlyAddedTag,
            $"Left {providerName}",
            "Subscription: PC Game Pass",
            "Left PC Game Pass",
            PcOnlyMembershipTag,
            "Game Pass: Xbox only",
            PcAndXboxMembershipTag,
            "Game Pass console: Xbox One",
            "Game Pass console: Xbox Series X|S",
            "Game Pass console: Xbox One + Series X|S",
            "Game Pass plan: PC Game Pass",
            "Game Pass plan: Xbox Game Pass for Console (legacy)",
            "Game Pass plan: Xbox Game Pass Essential",
            "Game Pass plan: Xbox Game Pass Premium",
            "Game Pass plan: Xbox Game Pass Ultimate",
            HiddenBySelectionTag
        };

    private static void AddMembershipTag(
        HashSet<string> tags,
        SubscriptionPlatforms platforms)
    {
        if (platforms == (SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole))
        {
            tags.Add(PcAndXboxMembershipTag);
        }
        else if (platforms == SubscriptionPlatforms.WindowsPc)
        {
            tags.Add(PcOnlyMembershipTag);
        }
        else if (platforms == SubscriptionPlatforms.XboxConsole)
        {
            tags.Add("Game Pass: Xbox only");
        }
    }

    private static void AddConsoleGenerationTag(
        HashSet<string> tags, SubscriptionGame game)
    {
        if ((game.AccessPlatforms & SubscriptionPlatforms.XboxConsole) == 0)
        {
            return;
        }

        switch (game.XboxGenerations)
        {
            case XboxConsoleGenerations.XboxOne:
                tags.Add("Game Pass console: Xbox One");
                break;
            case XboxConsoleGenerations.SeriesXorS:
                tags.Add("Game Pass console: Xbox Series X|S");
                break;
            case XboxConsoleGenerations.Both:
                tags.Add("Game Pass console: Xbox One + Series X|S");
                break;
        }
    }

    internal static HashSet<MetadataProperty> GetPlatformProperties(SubscriptionGame game)
    {
        var platforms = new HashSet<MetadataProperty>();
        if ((game.AccessPlatforms & SubscriptionPlatforms.WindowsPc) != 0)
        {
            platforms.Add(new MetadataSpecProperty("pc_windows"));
        }

        if ((game.AccessPlatforms & SubscriptionPlatforms.XboxConsole) != 0)
        {
            if ((game.XboxGenerations & XboxConsoleGenerations.XboxOne) != 0)
            {
                platforms.Add(new MetadataNameProperty("Xbox One"));
            }
            if ((game.XboxGenerations & XboxConsoleGenerations.SeriesXorS) != 0)
            {
                platforms.Add(new MetadataNameProperty("Xbox Series X|S"));
            }
            if (game.XboxGenerations == XboxConsoleGenerations.None)
            {
                platforms.Add(new MetadataNameProperty("Xbox console"));
            }
        }

        return platforms;
    }

    private static string GetSubscriptionTag(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("A provider name is required.", nameof(providerName));
        }

        return $"Subscription: {providerName.Trim()}";
    }

    private static HashSet<MetadataProperty>? ToMetadataProperties(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var properties = value!
            .Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => (MetadataProperty)new MetadataNameProperty(name))
            .ToHashSet();
        return properties.Count == 0 ? null : properties;
    }
}
