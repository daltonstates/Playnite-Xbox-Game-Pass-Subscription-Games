using System.Net;
using System.Net.Http;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Providers;
using SubscriptionLibraries.Core.Services;
using SubscriptionLibraries.Core.Utilities;

namespace SubscriptionLibraries.Tests;

internal static class Fixture
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}

internal sealed class RecordingTransport : IHttpClientService
{
    private readonly Func<Uri, string> responder;

    public RecordingTransport(Func<Uri, string> responder)
    {
        this.responder = responder;
    }

    public List<Uri> Requests { get; } = new();

    public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(uri);
        return Task.FromResult(responder(uri));
    }
}

internal sealed class SequenceHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> responses;

    public SequenceHttpMessageHandler(params Func<HttpResponseMessage>[] responses)
    {
        this.responses = new Queue<Func<HttpResponseMessage>>(responses);
    }

    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No fake HTTP response remains.");
        }

        return Task.FromResult(responses.Dequeue()());
    }

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json)
    };
}

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "SubscriptionLibraries.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        var fullPath = System.IO.Path.GetFullPath(Path);
        var testRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "SubscriptionLibraries.Tests"));
        if (!fullPath.StartsWith(testRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            fullPath.Length <= testRoot.Length + 1)
        {
            throw new InvalidOperationException("Refusing to remove an unexpected test directory.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, true);
        }
    }
}

internal sealed class FakeProvider : ISubscriptionProvider
{
    private readonly Queue<Func<IReadOnlyCollection<SubscriptionGame>>> results = new();

    public string Id => "fake-provider";

    public string Name => "Fake Provider";

    public int CallCount { get; private set; }

    public void Return(params SubscriptionGame[] games) => results.Enqueue(() => games);

    public void Throw(Exception exception) => results.Enqueue(() => throw exception);

    public Task<IReadOnlyCollection<SubscriptionGame>> GetGamesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        if (results.Count == 0)
        {
            throw new InvalidOperationException("No fake provider result remains.");
        }

        return Task.FromResult(results.Dequeue()());
    }

    public static SubscriptionGame Game(string id, string? name = null) => new()
    {
        ProviderId = "fake-provider",
        ProviderName = "Fake Provider",
        ProviderGameId = id,
        Name = name ?? id,
        Platform = "Windows PC",
        SubscriptionTier = "Fake Tier"
    };
}

internal sealed class FakeCatalogProvider : ISubscriptionCatalogProvider
{
    private readonly Queue<SubscriptionCatalogSnapshot> snapshots = new();

    public string Id => "fake-provider";

    public string Name => "Fake Provider";

    public void Return(
        IReadOnlyCollection<SubscriptionGame> games,
        IReadOnlyCollection<string> productIds,
        IReadOnlyCollection<string>? incompleteProductIds = null,
        IReadOnlyDictionary<string, SubscriptionPlatforms>? declaredPlatformsByProductId = null,
        IReadOnlyDictionary<string, Dictionary<string, SubscriptionPlatforms>>?
            declaredPlanPlatformsByProductId = null,
        IReadOnlyDictionary<string, Dictionary<string, SubscriptionPlatforms>>?
            leavingSoonPlanPlatformsByProductId = null,
        bool rejectMissingPlansForPreviouslyActiveGames = false) =>
        snapshots.Enqueue(new SubscriptionCatalogSnapshot
        {
            Games = games,
            ProductIds = productIds,
            IncompleteProductIds = incompleteProductIds ?? Array.Empty<string>(),
            DeclaredPlatformsByProductId = declaredPlatformsByProductId ??
                new Dictionary<string, SubscriptionPlatforms>(StringComparer.OrdinalIgnoreCase),
            DeclaredPlanPlatformsByProductId = declaredPlanPlatformsByProductId ??
                new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>(
                    StringComparer.OrdinalIgnoreCase),
            LeavingSoonPlanPlatformsByProductId = leavingSoonPlanPlatformsByProductId ??
                new Dictionary<string, Dictionary<string, SubscriptionPlatforms>>(
                    StringComparer.OrdinalIgnoreCase),
            RejectMissingPlansForPreviouslyActiveGames = rejectMissingPlansForPreviouslyActiveGames
        });

    public async Task<IReadOnlyCollection<SubscriptionGame>> GetGamesAsync(
        CancellationToken cancellationToken = default) =>
        (await GetCatalogSnapshotAsync(cancellationToken)).Games;

    public Task<SubscriptionCatalogSnapshot> GetCatalogSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshots.Count == 0)
        {
            throw new InvalidOperationException("No fake catalog snapshot remains.");
        }

        return Task.FromResult(snapshots.Dequeue());
    }
}
