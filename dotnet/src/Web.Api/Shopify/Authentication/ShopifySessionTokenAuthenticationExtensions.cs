using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Web.Api.Shopify.Authentication;

public static class ShopifySessionTokenAuthenticationExtensions
{
    extension(AuthenticationBuilder builder)
    {
        /// <summary>
        /// Registers the authentication scheme that validates Shopify session tokens presented as
        /// bearer tokens by Shopify-hosted surfaces.
        /// </summary>
        /// <param name="appOptions">The app's client ID and secret.</param>
        /// <param name="shopUrl">
        /// The shop this deployment serves. Tokens issued for any other shop are rejected.
        /// </param>
        /// <returns>The builder instance for further chaining.</returns>
        public AuthenticationBuilder AddShopifySessionToken(ShopifyAppOptions appOptions, string shopUrl)
        {
            return builder.AddJwtBearer(ShopifyAuthenticationDefaults.AuthenticationScheme, options =>
            {
                // Shopify session tokens carry non-standard claims we read by name ("dest"), so
                // keep the payload verbatim instead of letting the handler rewrite claim types.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = CreateTokenValidationParameters(appOptions, shopUrl);
                options.Events = CreateEvents(shopUrl);
            });
        }
    }

    private static TokenValidationParameters CreateTokenValidationParameters(
        ShopifyAppOptions appOptions,
        string shopUrl) =>
        new()
        {
            // Shopify signs session tokens with HS256 using the app's client secret. Pinning the
            // algorithm matters: without it a token could nominate its own, and "none" or an
            // asymmetric algorithm would sidestep the shared secret entirely.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = CreateSigningKey(appOptions),
            ValidateIssuerSigningKey = true,
            ValidAudience = appOptions.ClientId,
            ValidateAudience = true,
            ValidIssuer = ShopifyShopDomain.CreateIssuer(shopUrl),
            ValidateIssuer = true,
            ValidateLifetime = true,
            // Session tokens live for a minute, so the default five-minute clock skew would more
            // than double their usable lifetime.
            ClockSkew = TimeSpan.FromSeconds(5)
        };

    /// <summary>
    /// Returns the HS256 key Shopify signs session tokens with. When the client secret is absent —
    /// only possible in Development, see <see cref="ShopifyAppOptions.Validate"/> — this falls back
    /// to random bytes so every token fails its signature check and the endpoints answer 401,
    /// rather than leaving the scheme unregistered and failing requests with a 500.
    /// </summary>
    private static SymmetricSecurityKey CreateSigningKey(ShopifyAppOptions appOptions) =>
        new(appOptions.IsConfigured
            ? Encoding.UTF8.GetBytes(appOptions.ClientSecret)
            : RandomNumberGenerator.GetBytes(32));

    private static JwtBearerEvents CreateEvents(string shopUrl) => new()
    {
        OnTokenValidated = context => ValidateDestination(context, shopUrl),
        OnAuthenticationFailed = context =>
        {
            CreateLogger(context.HttpContext).LogWarning(
                context.Exception,
                "Rejected a Shopify session token for {Path}.",
                context.HttpContext.Request.Path);

            return Task.CompletedTask;
        }
    };

    /// <summary>
    /// Confirms the token was issued for the shop this deployment serves and republishes the shop as
    /// a claim the Shopify authorization policy can require.
    /// </summary>
    private static Task ValidateDestination(TokenValidatedContext context, string shopUrl)
    {
        var destination = context.Principal?.FindFirst(
            ShopifyAuthenticationDefaults.DestinationClaimType)?.Value;

        if (!ShopifyShopDomain.Matches(destination, shopUrl))
        {
            CreateLogger(context.HttpContext).LogWarning(
                "Rejected a Shopify session token issued for {Destination}; this deployment serves {ShopUrl}.",
                destination ?? "<no dest claim>",
                shopUrl);

            context.Fail("The session token was issued for a different shop.");

            return Task.CompletedTask;
        }

        context.Principal?.AddIdentity(new ClaimsIdentity(
            [new Claim(ShopifyAuthenticationDefaults.ShopClaimType, ShopifyShopDomain.Normalize(destination))],
            ShopifyAuthenticationDefaults.AuthenticationScheme));

        return Task.CompletedTask;
    }

    private static ILogger CreateLogger(HttpContext context) =>
        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger($"Web.Api.Shopify.Authentication.{ShopifyAuthenticationDefaults.AuthenticationScheme}");
}
