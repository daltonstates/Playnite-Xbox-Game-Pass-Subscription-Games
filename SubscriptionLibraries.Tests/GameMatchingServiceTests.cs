using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Services;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class GameMatchingServiceTests
{
    [Fact]
    public void PrioritizesStableIdsAndNeverMarksTitleOnlyMatchesSafeForMerge()
    {
        var subscription = new SubscriptionGame
        {
            ProviderGameId = "MS-PRODUCT",
            MicrosoftProductId = "MS-PRODUCT",
            Name = "DOOM Eternal"
        };
        var candidates = new[]
        {
            new MatchableGame
            {
                GameId = "steam-1",
                Name = "Unrelated",
                ExternalIdentifiers = new Dictionary<string, string>
                {
                    ["MicrosoftProductId"] = "MS-PRODUCT"
                }
            },
            new MatchableGame { GameId = "steam-2", Name = "DOOM® Eternal" },
            new MatchableGame { GameId = "steam-3", Name = "DOOM Eternaal" }
        };

        var results = new GameMatchingService().FindLikelyRelationships(subscription, candidates);

        Assert.Equal(GameMatchKind.KnownIdentifier, results[0].Kind);
        Assert.True(results[0].IsSafeForAutomaticRelationship);
        Assert.Equal(GameMatchKind.ExactNormalizedTitle, results[1].Kind);
        Assert.False(results[1].IsSafeForAutomaticRelationship);
        Assert.Equal(GameMatchKind.FuzzyTitle, results[2].Kind);
        Assert.False(results[2].IsSafeForAutomaticRelationship);
    }
}
