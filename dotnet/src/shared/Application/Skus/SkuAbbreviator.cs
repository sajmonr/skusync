using System.Text;

namespace Application.Skus;

/// <summary>
/// Pure helpers that turn a product title and variant title into the alphanumeric
/// segments used to build a SKU. Has no DB or option dependency so the abbreviation
/// rules can be unit tested in isolation.
/// </summary>
internal static class SkuAbbreviator
{
    /// <summary>
    /// Shopify's sentinel variant title for products that have no option-defined variants.
    /// Variants carrying this title contribute no variant segment to the SKU.
    /// </summary>
    private const string DefaultVariantTitle = "Default Title";

    private const int ProductWordTakeChars = 3;
    private const int VariantWordTakeChars = 2;

    /// <summary>
    /// Standard apparel-size abbreviations. Lookup is case-insensitive. Sizes the table
    /// doesn't know about fall through to the generic two-character rule.
    /// </summary>
    private static readonly Dictionary<string, string> SizeAbbreviations =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["XS"] = "XS",   ["EXTRA SMALL"] = "XS",
            ["S"] = "SM",    ["SMALL"] = "SM",
            ["M"] = "MD",    ["MEDIUM"] = "MD",
            ["L"] = "LG",    ["LARGE"] = "LG",
            ["XL"] = "XL",   ["EXTRA LARGE"] = "XL",
            ["XXL"] = "2XL", ["2XL"] = "2XL", ["DOUBLE XL"] = "2XL",
            ["XXXL"] = "3XL", ["3XL"] = "3XL", ["TRIPLE XL"] = "3XL",
            ["XXXXL"] = "4XL", ["4XL"] = "4XL",
        };

    /// <summary>
    /// Returns the alphanumeric abbreviation of a product title by taking the first
    /// <see cref="ProductWordTakeChars"/> characters of each whitespace-delimited word,
    /// with punctuation stripped. The first character of each word is upper-cased; the
    /// remaining characters preserve their original casing. Variant segments (handled
    /// separately) are upper-cased in full.
    /// </summary>
    public static string AbbreviateProductTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var word in title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = StripNonAlphanumeric(word);
            if (clean.Length == 0)
            {
                continue;
            }

            var take = Math.Min(ProductWordTakeChars, clean.Length);
            builder.Append(char.ToUpperInvariant(clean[0]));
            if (take > 1)
            {
                builder.Append(clean.AsSpan(1, take - 1));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits a slash-delimited variant title (e.g. "Small / Green") into per-option
    /// abbreviations. Each part is matched against <see cref="SizeAbbreviations"/>;
    /// otherwise the first <see cref="VariantWordTakeChars"/> alphanumeric characters
    /// are taken. Returns an empty list when there is no meaningful variant title
    /// ("Default Title", empty, or whitespace).
    /// </summary>
    public static IReadOnlyList<string> AbbreviateVariantTitle(string? variantTitle)
    {
        if (string.IsNullOrWhiteSpace(variantTitle) ||
            string.Equals(variantTitle, DefaultVariantTitle, StringComparison.Ordinal))
        {
            return [];
        }

        var parts = variantTitle.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return [];
        }

        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (SizeAbbreviations.TryGetValue(part, out var sizeAbbrev))
            {
                result.Add(sizeAbbrev);
                continue;
            }

            var clean = StripNonAlphanumeric(part);
            if (clean.Length == 0)
            {
                continue;
            }

            var take = Math.Min(VariantWordTakeChars, clean.Length);
            result.Add(clean[..take].ToUpperInvariant());
        }

        return result;
    }

    private static string StripNonAlphanumeric(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
