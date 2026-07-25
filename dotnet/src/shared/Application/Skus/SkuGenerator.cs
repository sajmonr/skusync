using System.Text;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Skus;

/// <summary>
/// Builds a SKU of the shape <c>{prefix}-{productAbbrev}-{variantPart1}-{variantPart2}…[-{n}]</c>,
/// guarantees uniqueness against the local database and any caller-supplied in-batch
/// reservations, and trims the product abbreviation as needed to stay within the
/// configured <see cref="SkuGeneratorOptions.MaxLength"/>. The numeric suffix is only
/// appended when the otherwise-shorter base SKU would collide.
/// </summary>
/// <remarks>
/// Uniqueness is checked at the application level rather than enforced by a database
/// constraint; a sibling deduplication job exists as the safety net for the small race
/// window between the existence check here and the eventual SaveChanges call.
/// </remarks>
public class SkuGenerator(
    ApplicationDbContext dbContext,
    IOptions<SkuGeneratorOptions> options,
    ILogger<SkuGenerator> logger
) : ISkuGenerator
{
    private const int MaxSuffixAttempts = 10_000;

    /// <summary>
    /// Safe product segment used when neither the product title nor the caller-supplied
    /// fallback yields any alphanumeric characters to abbreviate.
    /// </summary>
    private const string SafeProductSegment = "Variant";

    /// <inheritdoc/>
    public async Task<string> Generate(
        string productTitle,
        string? variantTitle,
        ISet<string>? reservedInBatch = null,
        string? fallbackSegment = null,
        CancellationToken cancellationToken = default
    )
    {
        var settings = options.Value;
        var productAbbrev = ResolveProductAbbreviation(productTitle, fallbackSegment);
        var variantAbbrevs = SkuAbbreviator.AbbreviateVariantTitle(variantTitle);
        var fixedSegmentsLength = ComputeFixedSegmentsLength(settings, variantAbbrevs);

        for (var suffix = 0; suffix < MaxSuffixAttempts; suffix++)
        {
            var candidate = ComposeCandidate(
                settings,
                productAbbrev,
                variantAbbrevs,
                fixedSegmentsLength,
                suffix,
                productTitle,
                variantTitle
            );

            if (await IsAvailable(candidate, reservedInBatch, cancellationToken))
            {
                LogGenerated(candidate, productTitle, variantTitle, suffix);
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not generate a unique SKU after {MaxSuffixAttempts} attempts for product "
                + $"'{productTitle}', variant '{variantTitle}'."
        );
    }

    /// <summary>
    /// Resolves the product portion of the SKU, falling back to the caller-supplied
    /// <paramref name="fallbackSegment"/> — and finally a safe constant — when the product
    /// title contains no alphanumeric characters to abbreviate.
    /// </summary>
    private string ResolveProductAbbreviation(string productTitle, string? fallbackSegment)
    {
        var productAbbrev = SkuAbbreviator.AbbreviateProductTitle(productTitle);
        if (productAbbrev.Length > 0)
        {
            return productAbbrev;
        }

        var fallback = SkuAbbreviator.SanitizeSegment(fallbackSegment);
        if (fallback.Length > 0)
        {
            logger.LogWarning(
                "Product title '{ProductTitle}' produced an empty abbreviation; falling back "
                    + "to segment '{Fallback}'.",
                productTitle,
                fallback
            );
            return fallback;
        }

        logger.LogWarning(
            "Product title '{ProductTitle}' and fallback segment both produced empty "
                + "abbreviations; using safe segment '{SafeSegment}'.",
            productTitle,
            SafeProductSegment
        );
        return SafeProductSegment;
    }

    /// <summary>
    /// Builds the candidate SKU for a given suffix attempt, truncating the product
    /// abbreviation as needed to stay within <see cref="SkuGeneratorOptions.MaxLength"/>.
    /// </summary>
    private static string ComposeCandidate(
        SkuGeneratorOptions settings,
        string productAbbrev,
        IReadOnlyList<string> variantAbbrevs,
        int fixedSegmentsLength,
        int suffix,
        string productTitle,
        string? variantTitle
    )
    {
        var suffixPart = suffix == 0 ? string.Empty : settings.Delimiter + suffix.ToString();

        var availableForProduct = settings.MaxLength - fixedSegmentsLength - suffixPart.Length;
        if (availableForProduct < 1)
        {
            throw new InvalidOperationException(
                $"SKU generator cannot fit any product abbreviation within MaxLength={settings.MaxLength} "
                    + $"for product '{productTitle}', variant '{variantTitle}' (suffix attempt {suffix}). "
                    + $"Consider increasing MaxLength or shortening the variant title."
            );
        }

        var truncatedProduct =
            productAbbrev.Length <= availableForProduct
                ? productAbbrev
                : productAbbrev[..availableForProduct];

        return Compose(settings, truncatedProduct, variantAbbrevs, suffixPart);
    }

    /// <summary>
    /// Returns <c>true</c> when the candidate is neither reserved in the current batch nor
    /// already present in the database.
    /// </summary>
    private async Task<bool> IsAvailable(
        string candidate,
        ISet<string>? reservedInBatch,
        CancellationToken cancellationToken
    )
    {
        if (reservedInBatch is not null && reservedInBatch.Contains(candidate))
        {
            return false;
        }

        var exists = await dbContext
            .ShopifyProductVariants.AsNoTracking()
            .AnyAsync(v => v.Sku == candidate, cancellationToken);

        return !exists;
    }

    private void LogGenerated(
        string candidate,
        string productTitle,
        string? variantTitle,
        int suffix
    )
    {
        if (suffix == 0)
        {
            logger.LogDebug(
                "Generated SKU '{Sku}' for product '{ProductTitle}' / variant '{VariantTitle}'.",
                candidate,
                productTitle,
                variantTitle
            );
        }
        else
        {
            logger.LogInformation(
                "Generated SKU '{Sku}' for product '{ProductTitle}' / variant '{VariantTitle}' after "
                    + "{Collisions} collision(s) — base candidate already in use.",
                candidate,
                productTitle,
                variantTitle,
                suffix
            );
        }
    }

    private static int ComputeFixedSegmentsLength(
        SkuGeneratorOptions settings,
        IReadOnlyList<string> variantAbbrevs
    )
    {
        // {prefix}{delim}{productAbbrev}[{delim}{variantPart}]…[{delim}{suffix}]
        var length = settings.Prefix.Length + settings.Delimiter.Length;
        foreach (var part in variantAbbrevs)
        {
            length += settings.Delimiter.Length + part.Length;
        }
        return length;
    }

    private static string Compose(
        SkuGeneratorOptions settings,
        string productAbbrev,
        IReadOnlyList<string> variantAbbrevs,
        string suffixPart
    )
    {
        var sb = new StringBuilder();
        sb.Append(settings.Prefix);
        sb.Append(settings.Delimiter);
        sb.Append(productAbbrev);
        foreach (var part in variantAbbrevs)
        {
            sb.Append(settings.Delimiter);
            sb.Append(part);
        }
        sb.Append(suffixPart);
        return sb.ToString();
    }
}
