using System.Net;
using System.Net.Http.Headers;
using SubscriptionLibraries.Core.Services;
using SubscriptionLibraries.Core.Utilities;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class HttpClientServiceTests
{
    [Fact]
    public async Task RetriesTransientServerFailureThenReturnsContent()
    {
        var handler = new SequenceHttpMessageHandler(
            () => SequenceHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "temporary"),
            () => SequenceHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true}"));
        using var httpClient = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        var service = new HttpClientService(
            httpClient,
            retryOptions: new RetryOptions { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(10) },
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var content = await service.GetStringAsync(new Uri("https://example.test/catalog"));

        Assert.Equal("{\"ok\":true}", content);
        Assert.Equal(2, handler.CallCount);
        Assert.Single(delays);
    }

    [Fact]
    public async Task HonorsRetryAfterForRateLimit()
    {
        var handler = new SequenceHttpMessageHandler(
            () =>
            {
                var response = SequenceHttpMessageHandler.Json((HttpStatusCode)429, "rate limited");
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return response;
            },
            () => SequenceHttpMessageHandler.Json(HttpStatusCode.OK, "ok"));
        using var httpClient = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        var service = new HttpClientService(
            httpClient,
            retryOptions: new RetryOptions { MaximumDelay = TimeSpan.FromSeconds(30) },
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        await service.GetStringAsync(new Uri("https://example.test/catalog"));

        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(delays));
    }

    [Fact]
    public async Task DoesNotRetryPermanentClientError()
    {
        var handler = new SequenceHttpMessageHandler(
            () => SequenceHttpMessageHandler.Json(HttpStatusCode.BadRequest, "bad request"));
        using var httpClient = new HttpClient(handler);
        var service = new HttpClientService(
            httpClient,
            delayAsync: (_, _) => Task.CompletedTask);

        var exception = await Assert.ThrowsAsync<CatalogHttpException>(() =>
            service.GetStringAsync(new Uri("https://example.test/catalog")));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }
}
