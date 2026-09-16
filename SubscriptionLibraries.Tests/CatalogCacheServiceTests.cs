using SubscriptionLibraries.Core.Services;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Providers.GamePass;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class CatalogCacheServiceTests
{
    [Fact]
    public void CatalogIdentityTracksBothCollectionIdsWithoutCaseSensitivity()
    {
        var original = new GamePassProviderOptions().NormalizeAndValidate();
        var changedPc = new GamePassProviderOptions
        {
            SiglId = "11111111-1111-1111-1111-111111111111"
        }.NormalizeAndValidate();
        var changedConsole = new GamePassProviderOptions
        {
            ConsoleSiglId = "22222222-2222-2222-2222-222222222222"
        }.NormalizeAndValidate();
        var changedCase = new GamePassProviderOptions
        {
            SiglId = GamePassConstants.PcCatalogSiglId.ToUpperInvariant(),
            ConsoleSiglId = GamePassConstants.ConsoleCatalogSiglId.ToUpperInvariant()
        }.NormalizeAndValidate();

        Assert.NotEqual(original.CatalogConfigurationKey, changedPc.CatalogConfigurationKey);
        Assert.NotEqual(original.CatalogConfigurationKey, changedConsole.CatalogConfigurationKey);
        Assert.Equal(original.CatalogConfigurationKey, changedCase.CatalogConfigurationKey);
    }

    [Fact]
    public async Task WritesAtomicallyAndReportsFreshThenExpiredCache()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var cache = new CatalogCacheService(directory.Path, clock: clock);
        var envelope = CreateEnvelope(clock.UtcNow, "ONE");

        await cache.WriteAsync(envelope);
        var fresh = cache.TryRead("fake-provider", "US", "en-US", TimeSpan.FromHours(24));

        Assert.NotNull(fresh);
        Assert.True(fresh.IsFresh);
        Assert.Single(fresh.Envelope.Games);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));

        envelope.Games.Add(FakeProvider.Game("TWO"));
        envelope.ProductIds.Add("TWO");
        await cache.WriteAsync(envelope);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));

        clock.UtcNow = clock.UtcNow.AddHours(25);
        var expired = cache.TryRead("fake-provider", "US", "en-US", TimeSpan.FromHours(24));
        Assert.NotNull(expired);
        Assert.False(expired.IsFresh);
        Assert.Equal(2, expired.Envelope.Games.Count);
    }

    [Fact]
    public async Task RoundTripsPlanMembershipAndRejectsOldSchema()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var cache = new CatalogCacheService(directory.Path, clock: clock);
        var envelope = CreateEnvelope(clock.UtcNow, "ONE");
        envelope.Games[0].AccessPlatforms = SubscriptionPlatforms.WindowsPc;
        envelope.Games[0].PlanPlatforms[GamePassPlanSelection.PcGamePass.Key()] =
            SubscriptionPlatforms.WindowsPc;
        envelope.Games[0].XboxGenerations = XboxConsoleGenerations.XboxOne;
        envelope.Games[0].PlanXboxGenerations[GamePassPlanSelection.Ultimate.Key()] =
            XboxConsoleGenerations.XboxOne;
        envelope.Games[0].IsConfirmedFreeToPlay = true;
        await cache.WriteAsync(envelope);

        var cached = cache.TryRead("fake-provider", "US", "en-US", TimeSpan.FromHours(24));
        Assert.NotNull(cached);
        Assert.Equal(SubscriptionPlatforms.WindowsPc,
            GamePassPlanSelection.PcGamePass.EligiblePlatforms(Assert.Single(cached.Envelope.Games)));
        Assert.Equal(XboxConsoleGenerations.XboxOne, cached.Envelope.Games[0].XboxGenerations);
        Assert.True(cached.Envelope.Games[0].IsConfirmedFreeToPlay);

        var path = cache.GetCachePath("fake-provider", "US", "en-US");
        var json = File.ReadAllText(path).Replace("\"SchemaVersion\": 4", "\"SchemaVersion\": 3");
        File.WriteAllText(path, json);
        Assert.Null(cache.TryRead("fake-provider", "US", "en-US", TimeSpan.FromHours(24)));
    }

    [Fact]
    public void IgnoresMalformedCache()
    {
        using var directory = new TemporaryDirectory();
        var cache = new CatalogCacheService(directory.Path);
        var path = cache.GetCachePath("fake-provider", "US", "en-US");
        File.WriteAllText(path, "not-json");

        var result = cache.TryRead("fake-provider", "US", "en-US", TimeSpan.FromHours(24));

        Assert.Null(result);
    }

    [Fact]
    public async Task IgnoresStructurallyEmptyCacheThatCouldFalselyRemoveLibraryGames()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var cache = new CatalogCacheService(directory.Path, clock: clock);
        var envelope = CreateEnvelope(clock.UtcNow, "ONE");
        envelope.Games.Clear();

        await cache.WriteAsync(envelope);
        var result = cache.TryRead("fake-provider", "US", "en-US", TimeSpan.FromHours(24));

        Assert.Null(result);
    }

    private static CatalogCacheEnvelope CreateEnvelope(DateTimeOffset timestamp, string id) => new()
    {
        ProviderId = "fake-provider",
        Region = "US",
        Language = "en-US",
        CachedAtUtc = timestamp,
        ProductIds = new List<string> { id },
        Games = new List<Core.Models.SubscriptionGame> { FakeProvider.Game(id) }
    };
}
