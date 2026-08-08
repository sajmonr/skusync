using Application.Sync;
using Microsoft.Extensions.Logging;

namespace Application.Jobs;

/// <summary>
/// Reconciles one variant and pushes whatever it owes, on demand.
/// <para>
/// A background job rather than work done inside the request because the SkuLabs quota is
/// <em>per account</em>: a push made from the HTTP host spends the same allowance as one made by
/// the background worker, but does so without the drain loop's pacing and without the loop knowing
/// it happened. Routing manual syncs through the same host keeps every SkuLabs request under one
/// component's control.
/// </para>
/// <para>
/// Manual syncs deliberately bypass the automatic-dispatch flags — pushing one item on request is
/// the entire point of the button, even with the cadence turned off — while the write-back kill
/// switches inside the dispatchers still apply.
/// </para>
/// </summary>
public class SingleItemSyncJob(
    IReconciler reconciler,
    IShopifyDispatcher shopifyDispatcher,
    ISkulabsDispatcher skulabsDispatcher,
    ILogger<SingleItemSyncJob> logger)
{
    public async Task Run(Guid variantId, CancellationToken cancellationToken = default)
    {
        Guid[] scope = [variantId];

        await reconciler.ReconcileVariants(scope, cancellationToken: cancellationToken);

        var shopify = await shopifyDispatcher.DispatchVariants(scope, cancellationToken);
        var skulabs = await skulabsDispatcher.DispatchVariants(scope, cancellationToken);

        logger.LogInformation(
            "Manual sync of variant {VariantId}: Shopify pushed {ShopifyPushed}/{ShopifyPending}, "
            + "SkuLabs pushed {SkulabsPushed}/{SkulabsPending}{RateLimited}.",
            variantId,
            shopify.Pushed, shopify.Pending,
            skulabs.Pushed, skulabs.Pending,
            skulabs.RateLimited ? " (SkuLabs rate-limited)" : "");
    }
}
