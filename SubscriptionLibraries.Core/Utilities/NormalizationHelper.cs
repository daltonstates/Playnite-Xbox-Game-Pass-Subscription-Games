using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SubscriptionLibraries.Core.Utilities;

public static class NormalizationHelper
{
    private static readonly Regex RepeatedWhitespace = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex RepeatedHyphen = new("-+", RegexOptions.Compiled);

    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var decomposed = title!.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSeparator = false;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append(' ');
                lastWasSeparator = true;
            }
        }

        return RepeatedWhitespace.Replace(builder.ToString(), " ").Trim();
    }

    public static string CreateStoreSlug(string? title)
    {
        var normalized = NormalizeTitle(title);
        if (normalized.Length == 0)
        {
            return "game";
        }

        var ascii = new string(normalized.Select(character =>
            character <= 127 && char.IsLetterOrDigit(character) ? character : '-').ToArray());
        return RepeatedHyphen.Replace(ascii.Replace(' ', '-'), "-").Trim('-');
    }

    public static Uri? NormalizeHttpsUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value!.Trim();
        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            normalized = "https:" + normalized;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
            uri = builder.Uri;
        }

        return uri;
    }

    public static double CalculateSimilarity(string? left, string? right)
    {
        var normalizedLeft = NormalizeTitle(left);
        var normalizedRight = NormalizeTitle(right);
        if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
        {
            return 0;
        }

        if (normalizedLeft == normalizedRight)
        {
            return 1;
        }

        var distance = LevenshteinDistance(normalizedLeft, normalizedRight);
        return 1d - (double)distance / Math.Max(normalizedLeft.Length, normalizedRight.Length);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var cost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + cost);
            }

            var swap = previous;
            previous = current;
            current = swap;
        }

        return previous[right.Length];
    }
}
