using System.ComponentModel.DataAnnotations;

namespace Application.Skus;

/// <summary>
/// Configuration values controlling how human-friendly SKUs are constructed by
/// <see cref="SkuGenerator"/>. Bound from the <c>SkuGenerator</c> configuration section.
/// </summary>
public class SkuGeneratorOptions
{
    public const string SectionKey = "SkuGenerator";

    /// <summary>
    /// Leading identifier prepended to every generated SKU (e.g. "BW"). The prefix is
    /// never truncated when fitting the SKU into <see cref="MaxLength"/>.
    /// </summary>
    [Required]
    [StringLength(10, MinimumLength = 1)]
    public string Prefix { get; init; } = "BW";

    /// <summary>
    /// Maximum allowed length of a generated SKU. Only the product-title abbreviation is
    /// shortened to fit; the prefix, variant abbreviations, and collision suffix are
    /// preserved verbatim.
    /// </summary>
    [Range(8, 100)]
    public int MaxLength { get; init; } = 25;

    /// <summary>Character placed between the SKU's segments (prefix, product, variant parts, suffix).</summary>
    [Required]
    [StringLength(3, MinimumLength = 1)]
    public string Delimiter { get; init; } = "-";
}
