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
    public async Task ChangingCatalogConfigurationBypassesFreshCache()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeProvider();
        provider.Return(FakeProvider.Game("OLD"));
        provider.Return(FakeProvider.Game("NEW"));
        var service = CreateService(directory, clock);
        var options = Options();
        options.CatalogConfigurationKey = "first collection";

        await service.SynchronizeAsync(provider, options);
        options.CatalogConfigurationKey = "second collection";
        var refreshed = await service.SynchronizeAsync(provider, options);

        Assert.Equal(2, provider.CallCount);
        Assert.Equal(SubscriptionCatalogSource.Live, refreshed.Source);
        Assert.Equal("NEW", Assert.Single(refreshed.Games).ProviderGameId);
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
    public async Task MissingPlanForPreviouslyActiveProductCannotTriggerCleanup()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeCatalogProvider();
        provider.Return(new[] { FakeProvider.Game("ONE") }, new[] { "ONE" });
        provider.Return(new[] { FakeProvider.Game("TWO") },
            new[] { "ONE", "TWO" }, new[] { "ONE" },
            rejectMissingPlansForPreviouslyActiveGames: true);
        var service = CreateService(directory, clock);
        var options = Options();
        await service.SynchronizeAsync(provider, options);

        clock.UtcNow = clock.UtcNow.AddHours(25);
        var fallback = await service.SynchronizeAsync(provider, options);

        Assert.False(fallback.IsVerified);
        Assert.True(fallback.UsedFallback);
        Assert.Equal("ONE", Assert.Single(fallback.Games).ProviderGameId);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteMetadataUsesCurrentVerifiedLeavingSoonMembership(
        bool stillLeaving)
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeCatalogProvider();
        var game = FakeProvider.Game("INCOMPLETE");
        game.AccessPlatforms = SubscriptionPlatforms.WindowsPc;
        game.PlanPlatforms[GamePassPlanSelection.PcGamePass.Key()] =
            SubscriptionPlatforms.WindowsPc;
        game.Availability = SubscriptionAvailability.LeavingSoon;
        game.LeavingSoonPlanPlatforms[GamePassPlanSelection.PcGamePass.Key()] =
            SubscriptionPlatforms.WindowsPc;
        provider.Return(new[] { game, FakeProvider.Game("STAYS") },
            new[] { "INCOMPLETE", "STAYS" });
        var currentLeaving = new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>(
            StringComparer.OrdinalIgnoreCase);
        if (stillLeaving)
        {
            currentLeaving["INCOMPLETE"] = new Dictionary<string, SubscriptionPlatforms>
            {
                [GamePassPlanSelection.PcGamePass.Key()] = SubscriptionPlatforms.WindowsPc
            };
        }
        provider.Return(new[] { FakeProvider.Game("STAYS") },
            new[] { "INCOMPLETE", "STAYS" },
            new[] { "INCOMPLETE" },
            new Dictionary<string, SubscriptionPlatforms>
            {
                ["INCOMPLETE"] = SubscriptionPlatforms.WindowsPc
            },
            new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>
            {
                ["INCOMPLETE"] = new Dictionary<string, SubscriptionPlatforms>
                {
                    [GamePassPlanSelection.PcGamePass.Key()] = SubscriptionPlatforms.WindowsPc
                }
            },
            currentLeaving);
        var service = CreateService(directory, clock);
        await service.SynchronizeAsync(provider, Options());

        clock.UtcNow = clock.UtcNow.AddHours(25);
        var result = await service.SynchronizeAsync(provider, Options());
        var preserved = Assert.Single(result.Games,
            item => item.ProviderGameId == "INCOMPLETE");

        Assert.Equal(stillLeaving ? SubscriptionAvailability.LeavingSoon
            : SubscriptionAvailability.Active, preserved.Availability);
        Assert.Equal(preserved.Availability,
            GamePassPlanSelection.PcGamePass.Project(
                GamePassCatalogSelection.PcOnly, GamePassConsoleSelection.Both,
                preserved).Availability);
        Assert.Equal(stillLeaving, preserved.LeavingSoonPlanPlatforms.Count > 0);
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

    [Fact]
    public async Task IncompleteMetadataClearsGenerationNoLongerInDeclaredCatalog()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeCatalogProvider();
        var previous = FakeProvider.Game("SHIFT");
        previous.AccessPlatforms = SubscriptionPlatforms.XboxConsole;
        previous.PlanPlatforms[GamePassPlanSelection.Ultimate.Key()] =
            SubscriptionPlatforms.XboxConsole;
        previous.XboxGenerations = XboxConsoleGenerations.XboxOne;
        previous.PlanXboxGenerations[GamePassPlanSelection.Ultimate.Key()] =
            XboxConsoleGenerations.XboxOne;
        provider.Return(new[] { previous, FakeProvider.Game("STAYS") },
            new[] { "SHIFT", "STAYS" });
        provider.Return(new[] { FakeProvider.Game("STAYS") },
            new[] { "SHIFT", "STAYS" },
            new[] { "SHIFT" },
            new Dictionary<string, SubscriptionPlatforms>
            {
                ["SHIFT"] = SubscriptionPlatforms.XboxConsole
            },
            new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>
            {
                ["SHIFT"] = new Dictionary<string, SubscriptionPlatforms>
                {
                    [GamePassPlanSelection.Ultimate.Key()] = SubscriptionPlatforms.XboxConsole
                }
            });
        var service = CreateService(directory, clock);
        var options = Options();

        await service.SynchronizeAsync(provider, options);
        clock.UtcNow = clock.UtcNow.AddHours(25);
        var result = await service.SynchronizeAsync(provider, options);

        var game = Assert.Single(result.Games, candidate => candidate.ProviderGameId == "SHIFT");
        Assert.Equal(SubscriptionCatalogSource.Live, result.Source);
        Assert.Equal(XboxConsoleGenerations.None, game.XboxGenerations);
        Assert.Empty(game.PlanXboxGenerations);
    }

    [Fact]
    public async Task PartialPlanResponseFallsBackInsteadOfAcceptingMassMembershipLoss()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeCatalogProvider();
        var ids = Enumerable.Range(0, 24).Select(index => $"GAME{index}").ToArray();
        var previous = ids.Select(id =>
        {
            var game = FakeProvider.Game(id);
            game.AccessPlatforms = SubscriptionPlatforms.WindowsPc;
            game.PlanPlatforms[GamePassPlanSelection.PcGamePass.Key()] =
                SubscriptionPlatforms.WindowsPc;
            return game;
        }).ToArray();
        provider.Return(previous, ids);
        var current = ids.Select((id, index) =>
        {
            var game = FakeProvider.Game(id);
            game.AccessPlatforms = SubscriptionPlatforms.WindowsPc;
            game.PlanPlatforms[(index < 8
                ? GamePassPlanSelection.PcGamePass
                : GamePassPlanSelection.Ultimate).Key()] =
                SubscriptionPlatforms.WindowsPc;
            return game;
        }).ToArray();
        provider.Return(current, ids);
        var service = CreateService(directory, clock);
        var options = Options();

        await service.SynchronizeAsync(provider, options);
        clock.UtcNow = clock.UtcNow.AddHours(25);
        var fallback = await service.SynchronizeAsync(provider, options);

        Assert.True(fallback.UsedFallback);
        Assert.False(fallback.IsVerified);
        Assert.Contains("pc-game-pass PC membership unexpectedly dropped from 24 to 8",
            fallback.Warning);
        Assert.Equal(24, fallback.Games.Count);
        Assert.All(fallback.Games, game => Assert.True(
            GamePassCatalogSelection.PcOnly.Includes(GamePassPlanSelection.PcGamePass, game)));
    }

    [Fact]
    public async Task PartialGenerationResponseFallsBackWhileOverallPlanCountStaysStable()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeCatalogProvider();
        var ids = Enumerable.Range(0, 6).Select(index => $"GAME{index}").ToArray();
        var previous = ids.Select(id =>
        {
            var game = FakeProvider.Game(id);
            game.AccessPlatforms = SubscriptionPlatforms.XboxConsole;
            game.PlanPlatforms[GamePassPlanSelection.Ultimate.Key()] =
                SubscriptionPlatforms.XboxConsole;
            game.PlanXboxGenerations[GamePassPlanSelection.Ultimate.Key()] =
                XboxConsoleGenerations.Both;
            return game;
        }).ToArray();
        provider.Return(previous, ids);
        var current = ids.Select(id =>
        {
            var game = FakeProvider.Game(id);
            game.AccessPlatforms = SubscriptionPlatforms.XboxConsole;
            game.PlanPlatforms[GamePassPlanSelection.Ultimate.Key()] =
                SubscriptionPlatforms.XboxConsole;
            game.PlanXboxGenerations[GamePassPlanSelection.Ultimate.Key()] =
                XboxConsoleGenerations.SeriesXorS;
            return game;
        }).ToArray();
        provider.Return(current, ids);
        var service = CreateService(directory, clock);
        var options = Options();

        await service.SynchronizeAsync(provider, options);
        clock.UtcNow = clock.UtcNow.AddHours(25);
        var fallback = await service.SynchronizeAsync(provider, options);

        Assert.True(fallback.UsedFallback);
        Assert.Contains("ultimate Xbox One membership unexpectedly dropped from 6 to 0",
            fallback.Warning);
        Assert.Equal(6, fallback.Games.Count);
    }

    [Fact]
    public async Task BroadPcAccessDropFallsBackForAllCatalogsView()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeCatalogProvider();
        var ids = Enumerable.Range(0, 6).Select(index => $"GAME{index}").ToArray();
        var previous = ids.Select(id =>
        {
            var game = FakeProvider.Game(id);
            game.AccessPlatforms = SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole;
            return game;
        }).ToArray();
        var current = ids.Select(id =>
        {
            var game = FakeProvider.Game(id);
            game.AccessPlatforms = SubscriptionPlatforms.XboxConsole;
            return game;
        }).ToArray();
        provider.Return(previous, ids);
        provider.Return(current, ids);
        var service = CreateService(directory, clock);
        var options = Options();

        await service.SynchronizeAsync(provider, options);
        clock.UtcNow = clock.UtcNow.AddHours(25);
        var fallback = await service.SynchronizeAsync(provider, options);

        Assert.True(fallback.UsedFallback);
        Assert.Contains("all catalogs PC membership unexpectedly dropped from 6 to 0",
            fallback.Warning);
        Assert.All(fallback.Games, game => Assert.True(
            GamePassCatalogSelection.PcOnly.Includes(game)));
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
