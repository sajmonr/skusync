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
    ILogger<SkuGenerator> logger) : ISkuGenerator
{
    private const int MaxSuffixAttempts = 10_000;

    /// <inheritdoc/>
    public async Task<string> Generate(
        string productTitle,
        string? variantTitle,
        ISet<string>? reservedInBatch = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var productAbbrev = SkuAbbreviator.AbbreviateProductTitle(productTitle);
        var variantAbbrevs = SkuAbbreviator.AbbreviateVariantTitle(variantTitle);

        if (productAbbrev.Length == 0)
        {
            throw new InvalidOperationException(
                $"Cannot generate a SKU: product title '{productTitle}' produced an empty abbreviation.");
        }

        var fixedSegmentsLength = ComputeFixedSegmentsLength(settings, variantAbbrevs);

        for (var suffix = 0; suffix < MaxSuffixAttempts; suffix++)
        {
            var suffixPart = suffix == 0
                ? string.Empty
                : settings.Delimiter + suffix.ToString();

            var availableForProduct = settings.MaxLength - fixedSegmentsLength - suffixPart.Length;
            if (availableForProduct < 1)
            {
                throw new InvalidOperationException(
                    $"SKU generator cannot fit any product abbreviation within MaxLength={settings.MaxLength} " +
                    $"for product '{productTitle}', variant '{variantTitle}' (suffix attempt {suffix}). " +
                    $"Consider increasing MaxLength or shortening the variant title.");
            }

            var truncatedProduct = productAbbrev.Length <= availableForProduct
                ? productAbbrev
                : productAbbrev[..availableForProduct];

            var candidate = Compose(settings, truncatedProduct, variantAbbrevs, suffixPart);

            if (reservedInBatch is not null && reservedInBatch.Contains(candidate))
            {
                continue;
            }

            var exists = await dbContext.ShopifyProductVariants
                .AsNoTracking()
                .AnyAsync(v => v.Sku == candidate, cancellationToken);

            if (!exists)
            {
                if (suffix == 0)
                {
                    logger.LogDebug(
                        "Generated SKU '{Sku}' for product '{ProductTitle}' / variant '{VariantTitle}'.",
                        candidate, productTitle, variantTitle);
                }
                else
                {
                    logger.LogInformation(
                        "Generated SKU '{Sku}' for product '{ProductTitle}' / variant '{VariantTitle}' after " +
                        "{Collisions} collision(s) — base candidate already in use.",
                        candidate, productTitle, variantTitle, suffix);
                }

                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not generate a unique SKU after {MaxSuffixAttempts} attempts for product " +
            $"'{productTitle}', variant '{variantTitle}'.");
    }

    private static int ComputeFixedSegmentsLength(SkuGeneratorOptions settings, IReadOnlyList<string> variantAbbrevs)
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
        string suffixPart)
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
