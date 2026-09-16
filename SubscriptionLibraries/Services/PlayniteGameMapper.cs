using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;
using SubscriptionLibraries.Core.Models;

namespace SubscriptionLibraries.Services;

internal static class PlayniteGameMapper
{
    private const string ActiveAccessTag = "Access: Subscription";
    private const string RemovedAccessTag = "Access: Removed";
    private const string LeavingTag = "Leaving Game Pass";
    private const string RecentlyAddedTag = "Recently Added to Game Pass";

    public static GameMetadata Map(SubscriptionGame game, DateTimeOffset now)
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
            Platforms = new HashSet<MetadataProperty>
            {
                new MetadataSpecProperty("pc_windows")
            },
            Tags = GetActiveTagNames(game, now)
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
                new("Microsoft Store", game.StoreUri.AbsoluteUri)
            };
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

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            GetSubscriptionTag(game.ProviderName),
            ActiveAccessTag
        };

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

    public static IReadOnlyCollection<string> GetRemovedTagNames(string providerName) =>
        new[]
        {
            GetSubscriptionTag(providerName),
            RemovedAccessTag,
            $"Left {providerName}"
        };

    public static IReadOnlyCollection<string> GetManagedTagNames(string providerName) =>
        new[]
        {
            GetSubscriptionTag(providerName),
            ActiveAccessTag,
            RemovedAccessTag,
            LeavingTag,
            RecentlyAddedTag,
            $"Left {providerName}"
        };

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
