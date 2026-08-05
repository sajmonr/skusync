using FastEndpoints;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Web.Api.Shopify.Features.GetVariantInformation;

/// <summary>
/// Resolves a Shopify product variant to its linked SkuLabs item so an admin UI extension can deep
/// link a merchant straight from the variant they are looking at to the matching SkuLabs item.
/// </summary>
public class GetVariantInformationEndpoint(
    ApplicationDbContext dbContext,
    ILogger<GetVariantInformationEndpoint> logger)
    : Endpoint<GetVariantInformationRequest, GetVariantInformationResponse>, IShopifyEndpoint
{
    public override void Configure()
    {
        Get("variant-information");
        Group<ShopifyEndpointGroup>();
        Summary(summary =>
        {
            summary.Summary = "Get SkuLabs information for a Shopify variant";
            summary.Description =
                "Returns the SkuLabs admin URL for the variant's linked SkuLabs item. Responds with "
                + "404 when the variant is unknown to SkuSync or has no linked SkuLabs item.";
        });
    }

    public override async Task HandleAsync(
        GetVariantInformationRequest request,
        CancellationToken cancellationToken)
    {
        // The validator has already rejected anything unparseable, so this cannot fail here.
        ShopifyGlobalId.TryParseVariantId(request.VariantId, out var variantId);

        var skulabsItemId = await FindLinkedSkulabsItemId(variantId, cancellationToken);

        if (string.IsNullOrWhiteSpace(skulabsItemId))
        {
            logger.LogInformation(
                "No linked SkuLabs item for Shopify variant {VariantId}.",
                variantId);

            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        logger.LogDebug(
            "Resolved Shopify variant {VariantId} to SkuLabs item {SkulabsItemId}.",
            variantId,
            skulabsItemId);

        await Send.OkAsync(
            new GetVariantInformationResponse(
                variantId,
                skulabsItemId,
                ExternalItemUrls.CreateSkulabsItemUrl(skulabsItemId)),
            cancellationToken);
    }

    /// <summary>
    /// Deleted variants are excluded because their Shopify counterpart is gone, but deactivated ones
    /// are not: a variant is deactivated after repeated failed pushes, which is exactly when a
    /// merchant most wants the SkuLabs link to go and look at it.
    /// </summary>
    private Task<string?> FindLinkedSkulabsItemId(long variantId, CancellationToken cancellationToken) =>
        dbContext.ShopifyProductVariants
            .AsNoTracking()
            .Where(entity => entity.VariantId == variantId && !entity.IsDeleted)
            .Select(entity => entity.SkulabsItem == null
                ? null
                : entity.SkulabsItem.SkulabsSourceItemId)
            .FirstOrDefaultAsync(cancellationToken);
}
