using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SubscriptionLibraries.Core.Models;
using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Utilities;

namespace SubscriptionLibraries.Core.Services;

public sealed class CatalogCacheEnvelope
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string ProviderId { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string? CatalogConfigurationKey { get; set; }

    public DateTimeOffset CachedAtUtc { get; set; }

    public List<string> ProductIds { get; set; } = new();

    public List<SubscriptionGame> Games { get; set; } = new();

    public List<SubscriptionGame> RemovedGames { get; set; } = new();

    public CatalogDiagnostics? Diagnostics { get; set; }
}

public sealed class CatalogCacheReadResult
{
    public CatalogCacheReadResult(CatalogCacheEnvelope envelope, bool isFresh)
    {
        Envelope = envelope;
        IsFresh = isFresh;
    }

    public CatalogCacheEnvelope Envelope { get; }

    public bool IsFresh { get; }
}

public sealed class CatalogCacheService
{
    private static readonly Regex UnsafeFileNameCharacters =
        new("[^A-Za-z0-9._-]+", RegexOptions.Compiled);

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        NullValueHandling = NullValueHandling.Include
    };

    private readonly string cacheDirectory;
    private readonly ISubscriptionLogger logger;
    private readonly IClock clock;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public CatalogCacheService(
        string cacheDirectory,
        ISubscriptionLogger? logger = null,
        IClock? clock = null)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new ArgumentException("A cache directory is required.", nameof(cacheDirectory));
        }

        this.cacheDirectory = Path.GetFullPath(cacheDirectory);
        this.logger = logger ?? NullSubscriptionLogger.Instance;
        this.clock = clock ?? SystemClock.Instance;
    }

    public CatalogCacheReadResult? TryRead(
        string providerId,
        string region,
        string language,
        TimeSpan maximumAge,
        string? catalogConfigurationKey = null)
    {
        if (maximumAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        var path = GetCachePath(providerId, region, language);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var envelope = SafeJson.Deserialize<CatalogCacheEnvelope>(json, JsonSettings);
            if (envelope is null ||
                envelope.SchemaVersion != CatalogCacheEnvelope.CurrentSchemaVersion ||
                !string.Equals(envelope.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(envelope.Region, region, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(envelope.Language, language, StringComparison.OrdinalIgnoreCase) ||
                envelope.CachedAtUtc == default ||
                envelope.Games is null ||
                envelope.ProductIds is null ||
                envelope.Games.Count == 0 ||
                envelope.ProductIds.Count == 0 ||
                envelope.Games.Any(game =>
                    game is null ||
                    string.IsNullOrWhiteSpace(game.ProviderGameId) ||
                    !string.Equals(
                        game.ProviderId,
                        providerId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                logger.Warn($"Ignoring invalid catalog cache at {path}.");
                return null;
            }

            if (!string.Equals(envelope.CatalogConfigurationKey, catalogConfigurationKey,
                    StringComparison.Ordinal))
            {
                logger.Info("Catalog configuration changed; ignoring the previous cache.");
                return null;
            }

            envelope.RemovedGames ??= new List<SubscriptionGame>();
            var age = clock.UtcNow - envelope.CachedAtUtc;
            var isFresh = age >= TimeSpan.Zero && age <= maximumAge;
            return new CatalogCacheReadResult(envelope, isFresh);
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is JsonException)
        {
            logger.Warn(exception, $"Could not read catalog cache at {path}; it will be ignored.");
            return null;
        }
    }

    public async Task WriteAsync(
        CatalogCacheEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        ValidateEnvelope(envelope);
        var path = GetCachePath(envelope.ProviderId, envelope.Region, envelope.Language);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backupPath = path + ".bak";
        var json = JsonConvert.SerializeObject(envelope, JsonSettings);
        var bytes = new UTF8Encoding(false).GetBytes(json);

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           81920,
                           FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                if (File.Exists(path))
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    File.Replace(temporaryPath, path, backupPath, true);
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            writeLock.Release();
        }
    }

    public string GetCachePath(string providerId, string region, string language)
    {
        var safeProvider = MakeSafePathPart(providerId);
        var safeRegion = MakeSafePathPart(region);
        var safeLanguage = MakeSafePathPart(language);
        return Path.Combine(cacheDirectory, $"{safeProvider}-{safeRegion}-{safeLanguage}.json");
    }

    private static string MakeSafePathPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Cache key components cannot be empty.", nameof(value));
        }

        return UnsafeFileNameCharacters.Replace(value.Trim(), "-").Trim('-');
    }

    private static void ValidateEnvelope(CatalogCacheEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.ProviderId) ||
            string.IsNullOrWhiteSpace(envelope.Region) ||
            string.IsNullOrWhiteSpace(envelope.Language) ||
            envelope.CachedAtUtc == default ||
            envelope.Games is null ||
            envelope.ProductIds is null)
        {
            throw new ArgumentException("The catalog cache envelope is incomplete.", nameof(envelope));
        }

        envelope.SchemaVersion = CatalogCacheEnvelope.CurrentSchemaVersion;
        envelope.ProductIds = envelope.ProductIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        envelope.RemovedGames ??= new List<SubscriptionGame>();
    }
}
