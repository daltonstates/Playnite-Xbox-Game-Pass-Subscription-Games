using System;
using System.Collections.Generic;
using System.Linq;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Utilities;

namespace SubscriptionLibraries.Core.Services;

public enum GameMatchKind
{
    None,
    KnownIdentifier,
    MetadataIdentifier,
    ExactNormalizedTitle,
    FuzzyTitle
}

public sealed class MatchableGame
{
    public string GameId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Dictionary<string, string> ExternalIdentifiers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> MetadataIdentifiers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GameMatchResult
{
    public MatchableGame? Candidate { get; set; }

    public GameMatchKind Kind { get; set; }

    public double Confidence { get; set; }

    public bool IsSafeForAutomaticRelationship { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class GameMatchingService
{
    private const double ConservativeFuzzyThreshold = 0.92;

    public IReadOnlyList<GameMatchResult> FindLikelyRelationships(
        SubscriptionGame subscriptionGame,
        IEnumerable<MatchableGame> candidates)
    {
        if (subscriptionGame is null)
        {
            throw new ArgumentNullException(nameof(subscriptionGame));
        }

        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        var sourceIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(sourceIdentifiers, subscriptionGame.ProviderGameId);
        Add(sourceIdentifiers, subscriptionGame.MicrosoftProductId);
        foreach (var value in subscriptionGame.RawSourceIdentifiers?.Values ?? Enumerable.Empty<string>())
        {
            Add(sourceIdentifiers, value);
        }

        var results = new List<GameMatchResult>();
        foreach (var candidate in candidates)
        {
            var identifierMatch = FindIdentifierMatch(sourceIdentifiers, candidate.ExternalIdentifiers);
            if (identifierMatch is not null || sourceIdentifiers.Contains(candidate.GameId))
            {
                results.Add(new GameMatchResult
                {
                    Candidate = candidate,
                    Kind = GameMatchKind.KnownIdentifier,
                    Confidence = 1,
                    IsSafeForAutomaticRelationship = true,
                    Reason = identifierMatch is null
                        ? "The library GameId matches a stable source identifier."
                        : $"Stable identifier match: {identifierMatch}."
                });
                continue;
            }

            var metadataMatch = FindIdentifierMatch(sourceIdentifiers, candidate.MetadataIdentifiers);
            if (metadataMatch is not null)
            {
                results.Add(new GameMatchResult
                {
                    Candidate = candidate,
                    Kind = GameMatchKind.MetadataIdentifier,
                    Confidence = 1,
                    IsSafeForAutomaticRelationship = true,
                    Reason = $"Metadata identifier match: {metadataMatch}."
                });
                continue;
            }

            var normalizedSubscriptionTitle = NormalizationHelper.NormalizeTitle(subscriptionGame.Name);
            var normalizedCandidateTitle = NormalizationHelper.NormalizeTitle(candidate.Name);
            if (normalizedSubscriptionTitle.Length > 0 &&
                normalizedSubscriptionTitle == normalizedCandidateTitle)
            {
                results.Add(new GameMatchResult
                {
                    Candidate = candidate,
                    Kind = GameMatchKind.ExactNormalizedTitle,
                    Confidence = 0.95,
                    IsSafeForAutomaticRelationship = false,
                    Reason = "Titles are equal after conservative normalization."
                });
                continue;
            }

            var similarity = NormalizationHelper.CalculateSimilarity(
                normalizedSubscriptionTitle,
                normalizedCandidateTitle);
            if (Math.Min(normalizedSubscriptionTitle.Length, normalizedCandidateTitle.Length) >= 5 &&
                similarity >= ConservativeFuzzyThreshold)
            {
                results.Add(new GameMatchResult
                {
                    Candidate = candidate,
                    Kind = GameMatchKind.FuzzyTitle,
                    Confidence = similarity,
                    IsSafeForAutomaticRelationship = false,
                    Reason = "Titles are a conservative fuzzy match; no automatic merge is permitted."
                });
            }
        }

        return results
            .OrderBy(result => Priority(result.Kind))
            .ThenByDescending(result => result.Confidence)
            .ThenBy(result => result.Candidate?.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? FindIdentifierMatch(
        HashSet<string> sourceIdentifiers,
        IReadOnlyDictionary<string, string> candidateIdentifiers)
    {
        foreach (var identifier in candidateIdentifiers)
        {
            if (!string.IsNullOrWhiteSpace(identifier.Value) &&
                sourceIdentifiers.Contains(identifier.Value.Trim()))
            {
                return $"{identifier.Key}={identifier.Value}";
            }
        }

        return null;
    }

    private static void Add(ISet<string> identifiers, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            identifiers.Add(value!.Trim());
        }
    }

    private static int Priority(GameMatchKind kind) => kind switch
    {
        GameMatchKind.KnownIdentifier => 0,
        GameMatchKind.MetadataIdentifier => 1,
        GameMatchKind.ExactNormalizedTitle => 2,
        GameMatchKind.FuzzyTitle => 3,
        _ => 4
    };
}
