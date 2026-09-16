using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SubscriptionLibraries.Core.Utilities;

namespace SubscriptionLibraries.Core.Services;

public sealed class HttpClientService : IHttpClientService
{
    private readonly HttpClient httpClient;
    private readonly ISubscriptionLogger logger;
    private readonly RetryOptions retryOptions;
    private readonly IClock clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;

    public HttpClientService(
        HttpClient httpClient,
        ISubscriptionLogger? logger = null,
        RetryOptions? retryOptions = null,
        IClock? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.logger = logger ?? NullSubscriptionLogger.Instance;
        this.retryOptions = retryOptions ?? new RetryOptions();
        this.clock = clock ?? SystemClock.Instance;
        this.delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
        this.retryOptions.Validate();
    }

    public async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (uri is null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Catalog requests require an absolute HTTPS URI.", nameof(uri));
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= retryOptions.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return content;
                }

                var statusCode = (int)response.StatusCode;
                var message = BuildStatusMessage(uri, statusCode, response.ReasonPhrase, content);
                if (!RetryHelper.IsTransientStatusCode(statusCode))
                {
                    throw new CatalogHttpException(message, response.StatusCode);
                }

                lastException = new CatalogHttpException(message, response.StatusCode);
                if (attempt == retryOptions.MaxAttempts)
                {
                    break;
                }

                var delay = RetryHelper.GetDelay(attempt, response, retryOptions, clock.UtcNow);
                logger.Warn(
                    $"Catalog request returned HTTP {statusCode}; retrying in {delay.TotalSeconds:0.##} seconds " +
                    $"(attempt {attempt + 1}/{retryOptions.MaxAttempts}).");
                await delayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new CatalogHttpException(
                    $"Catalog request to {GetSafeEndpoint(uri)} timed out.");
                if (attempt == retryOptions.MaxAttempts)
                {
                    break;
                }

                var delay = RetryHelper.GetDelay(attempt, null, retryOptions, clock.UtcNow);
                logger.Warn(
                    $"Catalog request timed out; retrying in {delay.TotalSeconds:0.##} seconds " +
                    $"(attempt {attempt + 1}/{retryOptions.MaxAttempts}).");
                await delayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                lastException = exception;
                if (attempt == retryOptions.MaxAttempts)
                {
                    break;
                }

                var delay = RetryHelper.GetDelay(attempt, null, retryOptions, clock.UtcNow);
                logger.Warn(
                    exception,
                    $"Catalog request failed; retrying in {delay.TotalSeconds:0.##} seconds " +
                    $"(attempt {attempt + 1}/{retryOptions.MaxAttempts}).");
                await delayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastException as CatalogHttpException ?? new CatalogHttpException(
            $"Catalog request to {GetSafeEndpoint(uri)} failed after {retryOptions.MaxAttempts} attempts.",
            null,
            lastException);
    }

    private static string BuildStatusMessage(Uri uri, int statusCode, string? reason, string content)
    {
        var preview = string.IsNullOrWhiteSpace(content)
            ? string.Empty
            : $" Response: {content.Trim().Substring(0, Math.Min(256, content.Trim().Length))}";
        return $"Catalog request to {GetSafeEndpoint(uri)} returned HTTP {statusCode} {reason}.{preview}";
    }

    private static string GetSafeEndpoint(Uri uri) => uri.GetLeftPart(UriPartial.Path);
}
