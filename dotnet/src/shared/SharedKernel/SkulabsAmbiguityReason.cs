namespace SharedKernel;

/// <summary>
/// Why a SkuLabs item cannot be cleanly mapped to exactly one Shopify variant, and so is
/// quarantined for review rather than synced. Values are stable and persisted (mirrored by the
/// <c>SkulabsAmbiguityReasons</c> lookup table), so append new members — never renumber existing ones.
/// </summary>
public enum SkulabsAmbiguityReason
{
    /// <summary>The item has no channel listings at all, so there is nothing to map.</summary>
    NoListings = 1,

    /// <summary>The item has more than one listing, so the target Shopify variant is ambiguous.</summary>
    MultipleListings = 2,

    /// <summary>The item has a single listing, but it is not a Shopify variant (non-numeric variant id).</summary>
    ListingNotInShopify = 3
}
