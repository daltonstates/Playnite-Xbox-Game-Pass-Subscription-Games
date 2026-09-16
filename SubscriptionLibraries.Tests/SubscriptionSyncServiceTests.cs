using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Services;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class SubscriptionSyncServiceTests
{
    [Fact]
    public async Task UsesFreshCacheWithoutCallingProviderAgain()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeProvider();
        provider.Return(FakeProvider.Game("ONE"));
        provider.Throw(new InvalidOperationException("Should not be called"));
        var service = CreateService(directory, clock);
        var options = Options();

        var live = await service.SynchronizeAsync(provider, options);
        var cached = await service.SynchronizeAsync(provider, options);

        Assert.Equal(SubscriptionCatalogSource.Live, live.Source);
        Assert.Equal(SubscriptionCatalogSource.FreshCache, cached.Source);
        Assert.Equal(1, provider.CallCount);
        Assert.False(cached.UsedFallback);
    }

    [Fact]
    public async Task FallsBackToStaleCacheWhenProviderFails()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeProvider();
        provider.Return(FakeProvider.Game("ONE"));
        provider.Throw(new HttpRequestException("Microsoft unavailable"));
        var service = CreateService(directory, clock);
        var options = Options();
        await service.SynchronizeAsync(provider, options);

        clock.UtcNow = clock.UtcNow.AddHours(25);
        var fallback = await service.SynchronizeAsync(provider, options);

        Assert.Equal(SubscriptionCatalogSource.StaleCache, fallback.Source);
        Assert.True(fallback.UsedFallback);
        Assert.Single(fallback.Games);
        Assert.Contains("Microsoft unavailable", fallback.Warning);
    }

    [Fact]
    public async Task RecordsRemovedSubscriptionGamesWithoutReturningThemAsActive()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeProvider();
        provider.Return(FakeProvider.Game("STAYS"), FakeProvider.Game("LEAVES"));
        provider.Return(FakeProvider.Game("STAYS"));
        var cache = new CatalogCacheService(directory.Path, clock: clock);
        var service = new SubscriptionSyncService(cache, clock: clock);
        var options = Options();
        await service.SynchronizeAsync(provider, options);

        clock.UtcNow = clock.UtcNow.AddHours(25);
        var refreshed = await service.SynchronizeAsync(provider, options);
        var stored = cache.TryRead("fake-provider", "US", "en-US", TimeSpan.FromHours(24));

        Assert.Single(refreshed.Games);
        Assert.Equal("STAYS", refreshed.Games[0].ProviderGameId);
        var removed = Assert.Single(stored!.Envelope.RemovedGames);
        Assert.Equal("LEAVES", removed.ProviderGameId);
        Assert.Equal(SubscriptionAvailability.Removed, removed.Availability);
        Assert.Equal(clock.UtcNow, removed.LeavingDate);
    }

    [Fact]
    public async Task PreservesPreviouslyActiveGameWhenOnlyCurrentMetadataIsIncomplete()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeCatalogProvider();
        provider.Return(
            new[]
            {
                FakeProvider.Game("STAYS"),
                FakeProvider.Game("INCOMPLETE"),
                FakeProvider.Game("LEAVES")
            },
            new[] { "STAYS", "INCOMPLETE", "LEAVES" });
        provider.Return(
            new[] { FakeProvider.Game("STAYS") },
            new[] { "STAYS", "INCOMPLETE" },
            new[] { "INCOMPLETE" });
        var cache = new CatalogCacheService(directory.Path, clock: clock);
        var service = new SubscriptionSyncService(cache, clock: clock);
        var options = Options();
        await service.SynchronizeAsync(provider, options);

        clock.UtcNow = clock.UtcNow.AddHours(25);
        var refreshed = await service.SynchronizeAsync(provider, options);
        var stored = cache.TryRead("fake-provider", "US", "en-US", TimeSpan.FromHours(24));

        Assert.Equal(new[] { "INCOMPLETE", "STAYS" },
            refreshed.Games.Select(game => game.ProviderGameId).OrderBy(id => id).ToArray());
        Assert.Equal(new[] { "INCOMPLETE", "STAYS" },
            stored!.Envelope.ProductIds.OrderBy(id => id).ToArray());
        Assert.Equal("LEAVES", Assert.Single(stored.Envelope.RemovedGames).ProviderGameId);
    }

    private static SubscriptionSyncService CreateService(TemporaryDirectory directory, FakeClock clock) =>
        new(new CatalogCacheService(directory.Path, clock: clock), clock: clock);

    private static SubscriptionSyncOptions Options() => new()
    {
        Region = "US",
        Language = "en-US",
        CacheLifetime = TimeSpan.FromHours(24),
        RefreshFromNetwork = true
    };
}
