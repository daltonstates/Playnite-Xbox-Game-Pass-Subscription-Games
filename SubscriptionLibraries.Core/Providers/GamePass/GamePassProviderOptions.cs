using System;
using System.Text.RegularExpressions;

namespace SubscriptionLibraries.Core.Providers.GamePass;

public sealed class GamePassProviderOptions
{
    private static readonly Regex RegionPattern = new("^[A-Za-z]{2}$", RegexOptions.Compiled);
    private static readonly Regex LanguagePattern = new(
        "^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$",
        RegexOptions.Compiled);

    public string Region { get; set; } = "US";

    public string Language { get; set; } = "en-US";

    public string SiglId { get; set; } = GamePassConstants.PcCatalogSiglId;

    public Uri CatalogEndpoint { get; set; } = GamePassConstants.CatalogEndpoint;

    public Uri DisplayCatalogEndpoint { get; set; } = GamePassConstants.DisplayCatalogEndpoint;

    public int ProductBatchSize { get; set; } = GamePassConstants.ProductBatchSize;

    public GamePassProviderOptions NormalizeAndValidate()
    {
        Region = (Region ?? string.Empty).Trim().ToUpperInvariant();
        Language = (Language ?? string.Empty).Trim();
        SiglId = (SiglId ?? string.Empty).Trim();

        if (!RegionPattern.IsMatch(Region))
        {
            throw new ArgumentException("Region must be a two-letter market code.", nameof(Region));
        }

        if (!LanguagePattern.IsMatch(Language))
        {
            throw new ArgumentException("Language must be a valid language tag such as en-US.", nameof(Language));
        }

        if (string.IsNullOrWhiteSpace(SiglId))
        {
            throw new ArgumentException("A Game Pass SIGL identifier is required.", nameof(SiglId));
        }

        if (CatalogEndpoint is null || !CatalogEndpoint.IsAbsoluteUri || CatalogEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The catalog endpoint must be an absolute HTTPS URI.", nameof(CatalogEndpoint));
        }

        if (DisplayCatalogEndpoint is null ||
            !DisplayCatalogEndpoint.IsAbsoluteUri ||
            DisplayCatalogEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "The display catalog endpoint must be an absolute HTTPS URI.",
                nameof(DisplayCatalogEndpoint));
        }

        if (ProductBatchSize < 1 || ProductBatchSize > GamePassConstants.ProductBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ProductBatchSize),
                $"Product batch size must be between 1 and {GamePassConstants.ProductBatchSize}.");
        }

        return this;
    }
}
