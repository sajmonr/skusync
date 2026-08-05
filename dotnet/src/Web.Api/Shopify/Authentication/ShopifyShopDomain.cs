namespace Web.Api.Shopify.Authentication;

/// <summary>
/// Normalises Shopify shop URLs so a value read from configuration can be compared with one read
/// from a session-token claim without tripping over a trailing slash or letter casing.
/// </summary>
public static class ShopifyShopDomain
{
    /// <summary>
    /// Returns <paramref name="shopUrl"/> trimmed of surrounding whitespace and any trailing
    /// slash, lower-cased. Returns an empty string for null, empty or whitespace input.
    /// </summary>
    public static string Normalize(string? shopUrl) =>
        string.IsNullOrWhiteSpace(shopUrl)
            ? ""
            : shopUrl.Trim().TrimEnd('/').ToLowerInvariant();

    /// <summary>
    /// Returns <c>true</c> when <paramref name="candidate"/> names the same shop as
    /// <paramref name="expected"/>. An empty value on either side never matches, so a token with no
    /// <c>dest</c> claim is rejected rather than accepted against unconfigured options.
    /// </summary>
    public static bool Matches(string? candidate, string? expected)
    {
        var normalizedCandidate = Normalize(candidate);
        var normalizedExpected = Normalize(expected);

        return normalizedCandidate.Length > 0 &&
               normalizedExpected.Length > 0 &&
               normalizedCandidate == normalizedExpected;
    }

    /// <summary>
    /// Returns the issuer Shopify stamps on session tokens for <paramref name="shopUrl"/>, which is
    /// the shop's admin URL rather than the shop URL itself.
    /// </summary>
    public static string CreateIssuer(string? shopUrl) => $"{Normalize(shopUrl)}/admin";
}
