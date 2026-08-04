using Application.Sync;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Web.Api.Features.ItemSync.TriggerItemSync;

/// <summary>
/// Manually syncs a single variant on demand: reconciles its linked SkuLabs pair, then dispatches
/// whatever is pending on both sides — synchronously, in this request. Manual triggers bypass the
/// <c>ShopifyAutoDispatch</c>/<c>SkulabsAutoDispatch</c> flags (the point of the button is to push
/// one item even while automatic dispatch is off); the dispatchers' <c>ShopifyWriteBack</c> /
/// <c>SkulabsWriteBack</c> kill switches still apply, so with a kill switch off the item stays
/// pending rather than pushed.
/// </summary>
public class TriggerItemSyncEndpoint(
    IReconciler reconciler,
    IShopifyDispatcher shopifyDispatcher,
    ISkulabsDispatcher skulabsDispatcher)
    : EndpointWithoutRequest<TriggerItemSyncResponse>
{
    public override void Configure()
    {
        Post("item-sync/{id}/sync");
        Options(endpoint => endpoint.RequireRateLimiting(ProductSyncRateLimitingExtensions.PolicyName));
        Summary(summary =>
        {
            summary.Summary = "Manually sync a single item";
            summary.Description =
                "Reconciles one variant against its linked SkuLabs item and dispatches the pending "
                + "changes to Shopify and SkuLabs immediately, bypassing the automatic-dispatch flags. "
                + "The ShopifyWriteBack / SkulabsWriteBack kill switches still apply. "
                + "Rate limited to one request per 30 seconds per client.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var variantId = Route<Guid>("id", isRequired: true);
        Guid[] scope = [variantId];

        await reconciler.ReconcileVariants(scope, cancellationToken);
        var shopify = await shopifyDispatcher.DispatchVariants(scope, cancellationToken);
        var skulabs = await skulabsDispatcher.DispatchVariants(scope, cancellationToken);

        if (skulabs.RateLimited)
        {
            HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(skulabs.RetryAfter!.Value.TotalSeconds)).ToString();
            await Send.ResponseAsync(
                new TriggerItemSyncResponse(shopify.Pushed, shopify.Failed, SkulabsPushed: 0, SkulabsFailed: 1),
                StatusCodes.Status429TooManyRequests,
                cancellationToken);
            return;
        }

        await Send.OkAsync(
            new TriggerItemSyncResponse(shopify.Pushed, shopify.Failed, skulabs.Pushed, skulabs.Failed),
            cancellationToken);
    }
}

/// <param name="ShopifyPushed">Variants written to Shopify (0 or 1).</param>
/// <param name="ShopifyFailed">Shopify writes that failed (0 or 1).</param>
/// <param name="SkulabsPushed">SkuLabs items written (0 or 1).</param>
/// <param name="SkulabsFailed">SkuLabs writes that failed (0 or 1).</param>
public readonly record struct TriggerItemSyncResponse(
    int ShopifyPushed,
    int ShopifyFailed,
    int SkulabsPushed,
    int SkulabsFailed);
