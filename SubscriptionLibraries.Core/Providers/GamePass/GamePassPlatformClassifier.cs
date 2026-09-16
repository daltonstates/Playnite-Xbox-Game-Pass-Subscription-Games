using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public sealed class GamePassPlatformClassifier
{
    private static readonly HashSet<string> ActionableAvailabilityActions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Fulfill",
            "License",
            "Purchase",
            "Redeem"
        };

    public ProductPlatformClassificationResult Classify(MicrosoftStoreProduct product)
    {
        if (product is null)
        {
            throw new ArgumentNullException(nameof(product));
        }

        var packagePlatforms = GetPackagePlatforms(product);
        if (packagePlatforms.Contains(GamePassConstants.WindowsDesktopPlatform))
        {
            return Pc("Microsoft Store package targets Windows.Desktop.");
        }

        if (packagePlatforms.Contains(GamePassConstants.LegacyWindowsDesktopPlatform))
        {
            return Pc("Microsoft Store package targets the legacy Windows desktop platform.");
        }

        var actionablePlatforms = GetActionableAvailabilityPlatforms(product);
        if (actionablePlatforms.Contains(GamePassConstants.WindowsDesktopPlatform))
        {
            return Pc("An actionable Microsoft Store availability explicitly allows Windows.Desktop.");
        }

        var explicitPlatforms = new HashSet<string>(packagePlatforms, StringComparer.OrdinalIgnoreCase);
        explicitPlatforms.UnionWith(actionablePlatforms);
        if (explicitPlatforms.Count > 0)
        {
            return new ProductPlatformClassificationResult
            {
                Classification = ProductPlatformClassification.NonPc,
                Evidence = "Explicit package/availability platforms do not include Windows PC: " +
                    string.Join(", ", explicitPlatforms.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            };
        }

        return new ProductPlatformClassificationResult
        {
            Classification = ProductPlatformClassification.Unknown,
            Evidence = "No explicit package or actionable availability platform metadata was present."
        };
    }

    private static HashSet<string> GetPackagePlatforms(MicrosoftStoreProduct product)
    {
        var platforms = product.DisplaySkuAvailabilities?
            .Where(display => display.Sku?.Properties?.Packages is not null)
            .SelectMany(display => display.Sku!.Properties!.Packages!)
            .Where(package => package.PlatformDependencies is not null)
            .SelectMany(package => package.PlatformDependencies!)
            .Select(platform => platform.PlatformName?.Trim())
            .Where(platform => !string.IsNullOrWhiteSpace(platform))
            .Cast<string>() ?? Enumerable.Empty<string>();
        return new HashSet<string>(platforms, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetActionableAvailabilityPlatforms(MicrosoftStoreProduct product)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var availabilities = product.DisplaySkuAvailabilities?
            .Where(display => display.Availabilities is not null)
            .SelectMany(display => display.Availabilities!) ??
            Enumerable.Empty<StoreAvailability>();

        foreach (var availability in availabilities)
        {
            if (availability.Actions is null ||
                !availability.Actions.Any(ActionableAvailabilityActions.Contains) ||
                IsSentinelAvailability(availability.Conditions?.StartDate))
            {
                continue;
            }

            var platforms = availability.Conditions?.ClientConditions?.AllowedPlatforms;
            if (platforms is null)
            {
                continue;
            }

            foreach (var platform in platforms)
            {
                if (!string.IsNullOrWhiteSpace(platform.PlatformName))
                {
                    result.Add(platform.PlatformName!.Trim());
                }
            }
        }

        return result;
    }

    private static bool IsSentinelAvailability(string? startDate)
    {
        if (string.IsNullOrWhiteSpace(startDate))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
                   startDate,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal,
                   out var parsed) &&
               parsed.Year <= 1900;
    }

    private static ProductPlatformClassificationResult Pc(string evidence) => new()
    {
        Classification = ProductPlatformClassification.WindowsPc,
        Evidence = evidence
    };
}
