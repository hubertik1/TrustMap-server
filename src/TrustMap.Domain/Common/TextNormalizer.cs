using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TrustMap.Domain.Common;

public static partial class TextNormalizer
{
    private static readonly Regex MultiWhitespaceRegex = MultiWhitespace();
    private static readonly Regex InvalidHandleCharacterRegex = InvalidHandleCharacter();
    private static readonly Regex MultiSeparatorRegex = MultiSeparator();

    public static string NormalizeForLookup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        normalized = builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        normalized = MultiWhitespaceRegex.Replace(normalized, " ");

        return normalized.Trim();
    }

    public static string NormalizeHandle(string value)
    {
        var normalized = NormalizeForLookup(value);
        normalized = InvalidHandleCharacterRegex.Replace(normalized, "-");
        normalized = MultiSeparatorRegex.Replace(normalized, "-").Trim('-');

        return normalized;
    }

    public static string SlugifyOrFallback(string? value, string fallback)
    {
        var handle = NormalizeHandle(value ?? string.Empty);
        return string.IsNullOrWhiteSpace(handle) ? fallback : handle;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiWhitespace();

    [GeneratedRegex(@"[^a-z0-9._-]+")]
    private static partial Regex InvalidHandleCharacter();

    [GeneratedRegex(@"[-._]{2,}")]
    private static partial Regex MultiSeparator();
}
