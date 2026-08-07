using FastEndpoints;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Web.Api.Shopify.Features.GetProductInformation;

/// <summary>
/// Resolves a Shopify product to every variant SkuSync knows about, each with its SkuLabs link where
/// there is one. It exists because the variant lookup cannot serve the product page: a single-variant
/// product has no variant page to render a block on, and a multi-variant product would otherwise cost
/// the merchant one page visit per variant.
/// </summary>
public class GetProductInformationEndpoint(
    ApplicationDbContext dbContext,
    ILogger<GetProductInformationEndpoint> logger)
    : Endpoint<GetProductInformationRequest, GetProductInformationResponse>, IShopifyEndpoint
{
    public override void Configure()
    {
        Get("product-information");
        Group<ShopifyEndpointGroup>();
        Summary(summary =>
        {
            summary.Summary = "Get SkuLabs information for every variant of a Shopify product";
            summary.Description =
                "Returns one entry per variant SkuSync holds for the product, with the SkuLabs admin "
                + "URL where the variant is linked and null where it is not. Responds with 404 when "
                + "there is nothing to return for the product at all, without distinguishing between "
                + "the possible reasons — callers get no signal about the product's internal sync "
                + "state.";
        });
    }

    public override async Task HandleAsync(
        GetProductInformationRequest request,
        CancellationToken cancellationToken)
    {
        // The validator has already rejected anything unparseable, so this cannot fail here.
        ShopifyGlobalId.TryParseProductId(request.ProductId, out var productId);

        var variants = await FindVariants(productId, cancellationToken);

        if (variants.Count == 0)
        {
            logger.LogDebug(
                "No SkuLabs information to return for Shopify product {ProductId}.",
                productId);

            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        logger.LogDebug(
            "Resolved Shopify product {ProductId} to {VariantCount} variant(s).",
            productId,
            variants.Count);

        await Send.OkAsync(new GetProductInformationResponse(productId, variants), cancellationToken);
    }

    /// <summary>
    /// Unlinked variants are returned alongside linked ones so the merchant sees the whole variant set
    /// rather than silently losing the rows that have no SkuLabs item yet. Deleted variants are
    /// excluded because their Shopify counterpart is gone, but deactivated ones are not: a variant is
    /// deactivated after repeated failed pushes, which is exactly when a merchant most wants to go and
    /// look at it.
    /// </summary>
    private async Task<List<ProductVariantInformation>> FindVariants(
        long productId,
        CancellationToken cancellationToken)
    {
        var variants = await dbContext.ShopifyProductVariants
            .AsNoTracking()
            .Where(entity => entity.ProductId == productId && !entity.IsDeleted)
            .OrderBy(entity => entity.Sku)
            .ThenBy(entity => entity.VariantId)
            .WithResolvedSkulabsItem()
            .Select(entity => new
            {
                entity.Variant.VariantId,
                entity.Variant.Sku,
                entity.Variant.DisplayName,
                SkulabsItemId = entity.SkulabsItem == null
                    ? null
                    : entity.SkulabsItem.SkulabsSourceItemId
            })
            .ToListAsync(cancellationToken);

        return variants
            .Select(variant => new ProductVariantInformation(
                variant.VariantId,
                variant.Sku,
                variant.DisplayName,
                string.IsNullOrWhiteSpace(variant.SkulabsItemId)
                    ? null
                    : ExternalItemUrls.CreateSkulabsItemUrl(variant.SkulabsItemId)))
            .ToList();
    }
}
