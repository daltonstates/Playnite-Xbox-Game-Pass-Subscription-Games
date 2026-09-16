using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Providers.GamePass;
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
        Assert.True(live.IsVerified);
        Assert.True(cached.IsVerified);
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
        Assert.False(fallback.IsVerified);
        Assert.Single(fallback.Games);
        Assert.Contains("Microsoft unavailable", fallback.Warning);
    }

    [Fact]
    public async Task ReportsOnlyNewLeavingSoonEntriesAndKeepsTheirStatus()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeProvider();
        provider.Return(FakeProvider.Game("ONE"));
        var leaving = FakeProvider.Game("ONE");
        leaving.Availability = SubscriptionAvailability.LeavingSoon;
        provider.Return(leaving);
        provider.Return(leaving);
        var service = CreateService(directory, clock);
        var options = Options();

        await service.SynchronizeAsync(provider, options);
        clock.UtcNow = clock.UtcNow.AddHours(25);
        var changed = await service.SynchronizeAsync(provider, options);
        clock.UtcNow = clock.UtcNow.AddHours(25);
        var repeated = await service.SynchronizeAsync(provider, options);

        Assert.Equal(SubscriptionAvailability.LeavingSoon, Assert.Single(changed.Games).Availability);
        Assert.Single(changed.NewlyLeavingSoonGames);
        Assert.Empty(repeated.NewlyLeavingSoonGames);
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

    [Fact]
    public async Task IncompleteMetadataDoesNotPreservePcAccessAfterPcCatalogDeparture()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeCatalogProvider();
        var previous = FakeProvider.Game("SHIFT");
        previous.AccessPlatforms = SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole;
        previous.Platform = SubscriptionPlatformNames.Format(previous.AccessPlatforms);
        provider.Return(new[] { previous, FakeProvider.Game("STAYS") },
            new[] { "SHIFT", "STAYS" });
        provider.Return(new[] { FakeProvider.Game("STAYS") },
            new[] { "SHIFT", "STAYS" },
            new[] { "SHIFT" },
            new Dictionary<string, SubscriptionPlatforms>
            {
                ["SHIFT"] = SubscriptionPlatforms.XboxConsole
            });
        var service = CreateService(directory, clock);
        await service.SynchronizeAsync(provider, Options());

        clock.UtcNow = clock.UtcNow.AddHours(25);
        var result = await service.SynchronizeAsync(provider, Options());

        var shifted = Assert.Single(result.Games, game => game.ProviderGameId == "SHIFT");
        Assert.Equal(SubscriptionPlatforms.XboxConsole, shifted.AccessPlatforms);
        Assert.Equal("Xbox console", shifted.Platform);
        Assert.False(GamePassCatalogSelection.PcOnly.Includes(shifted));
        Assert.True(GamePassCatalogSelection.XboxOnly.Includes(shifted));
    }

    [Fact]
    public async Task IncompletePcMetadataRetainsLastVerifiedPcAccessWhenStillListed()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeCatalogProvider();
        var previous = FakeProvider.Game("PARTIAL");
        previous.AccessPlatforms = SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole;
        previous.Platform = SubscriptionPlatformNames.Format(previous.AccessPlatforms);
        provider.Return(new[] { previous }, new[] { "PARTIAL" });
        var partial = FakeProvider.Game("PARTIAL");
        partial.AccessPlatforms = SubscriptionPlatforms.XboxConsole;
        partial.Platform = "Xbox console";
        provider.Return(new[] { partial }, new[] { "PARTIAL" }, new[] { "PARTIAL" },
            new Dictionary<string, SubscriptionPlatforms>
            {
                ["PARTIAL"] = SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole
            });
        var service = CreateService(directory, clock);
        await service.SynchronizeAsync(provider, Options());

        clock.UtcNow = clock.UtcNow.AddHours(25);
        var result = await service.SynchronizeAsync(provider, Options());

        var preserved = Assert.Single(result.Games);
        Assert.Equal(
            SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole,
            preserved.AccessPlatforms);
        Assert.Equal("Windows PC + Xbox console", preserved.Platform);
    }

    [Fact]
    public async Task IncompleteMetadataUpdatesPlanMembershipWithoutInventingAccess()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeCatalogProvider();
        var previous = FakeProvider.Game("SHIFT");
        previous.AccessPlatforms = SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole;
        previous.PlanPlatforms[GamePassPlanSelection.Premium.Key()] =
            SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole;
        provider.Return(new[] { previous, FakeProvider.Game("STAYS") },
            new[] { "SHIFT", "STAYS" });
        provider.Return(new[] { FakeProvider.Game("STAYS") }, new[] { "SHIFT", "STAYS" },
            new[] { "SHIFT" },
            new Dictionary<string, SubscriptionPlatforms>
            {
                ["SHIFT"] = SubscriptionPlatforms.XboxConsole
            },
            new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>
            {
                ["SHIFT"] = new Dictionary<string, SubscriptionPlatforms>
                {
                    [GamePassPlanSelection.Premium.Key()] = SubscriptionPlatforms.XboxConsole
                }
            });
        var service = CreateService(directory, clock);
        await service.SynchronizeAsync(provider, Options());

        clock.UtcNow = clock.UtcNow.AddHours(25);
        var result = await service.SynchronizeAsync(provider, Options());

        var game = Assert.Single(result.Games, candidate => candidate.ProviderGameId == "SHIFT");
        Assert.Equal(SubscriptionPlatforms.XboxConsole, game.AccessPlatforms);
        Assert.Equal(SubscriptionPlatforms.XboxConsole,
            GamePassPlanSelection.Premium.EligiblePlatforms(game));
        Assert.False(GamePassCatalogSelection.PcOnly.Includes(GamePassPlanSelection.Premium, game));
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
