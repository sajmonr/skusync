using Microsoft.Extensions.Hosting;

namespace Web.Api.Shopify.Authentication;

/// <summary>
/// Represents the Shopify app credentials needed to verify session tokens presented by
/// Shopify-hosted surfaces such as admin UI extensions.
/// </summary>
public class ShopifyAppOptions
{
    public const string SectionKey = "Shopify:App";

    /// <summary>
    /// Gets the app's client ID. Shopify puts this value in the <c>aud</c> claim of every session
    /// token it issues for the app, so it is the expected audience.
    /// </summary>
    public string ClientId { get; init; } = "";

    /// <summary>
    /// Gets the app's client secret. Shopify signs session tokens with HS256 using this value, which
    /// makes it the symmetric signing key as well as a secret — it must never be committed.
    /// </summary>
    public string ClientSecret { get; init; } = "";

    /// <summary>
    /// Gets a value indicating whether both credentials are present. When they aren't, the
    /// session-token scheme is still registered but cannot validate anything, so Shopify endpoints
    /// answer 401 instead of failing at request time with an unknown-scheme error.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>
    /// Throws outside Development when the credentials needed to verify a session token are missing.
    /// Development is exempt so the dashboard still runs locally without Shopify app secrets — the
    /// Shopify endpoints simply reject every request until the secrets are supplied.
    /// </summary>
    /// <param name="environment">The host environment.</param>
    /// <param name="shopUrl">The configured shop URL, used as the expected token issuer.</param>
    /// <exception cref="InvalidOperationException">A required value is absent.</exception>
    public void Validate(IHostEnvironment environment, string shopUrl)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                $"{SectionKey}:ClientId and {SectionKey}:ClientSecret must be configured with the "
                + "Shopify app's credentials so session tokens can be verified.");
        }

        if (string.IsNullOrWhiteSpace(shopUrl))
        {
            throw new InvalidOperationException(
                $"{ShopifyOptionsKeys.ShopUrl} must be configured; Shopify session tokens are only "
                + "accepted for the shop this deployment serves.");
        }
    }
}
