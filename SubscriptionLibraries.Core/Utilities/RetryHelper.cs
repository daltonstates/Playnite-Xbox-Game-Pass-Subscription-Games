using System;
using System.Net.Http;

namespace SubscriptionLibraries.Core.Utilities;

public sealed class RetryOptions
{
    public int MaxAttempts { get; set; } = 3;

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaximumDelay { get; set; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (MaxAttempts < 1 || MaxAttempts > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
        }

        if (InitialDelay < TimeSpan.Zero || MaximumDelay < InitialDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialDelay));
        }
    }
}
public static class RetryHelper
{
    public static bool IsTransientStatusCode(int statusCode) =>
        statusCode == 408 || statusCode == 429 || statusCode >= 500;

    public static TimeSpan GetDelay(
        int completedAttemptCount,
        HttpResponseMessage? response,
        RetryOptions options,
        DateTimeOffset now)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return Min(delta, options.MaximumDelay);
        }

        if (retryAfter?.Date is DateTimeOffset date && date > now)
        {
            return Min(date - now, options.MaximumDelay);
        }

        var exponent = Math.Max(0, completedAttemptCount - 1);
        var milliseconds = options.InitialDelay.TotalMilliseconds * Math.Pow(2, exponent);
        return Min(TimeSpan.FromMilliseconds(milliseconds), options.MaximumDelay);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
}
