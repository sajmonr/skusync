using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Sync;

public class ShopifyDispatchTrigger(
    IShopifyDispatcher dispatcher,
    IFeatureManager featureManager,
    ILogger<ShopifyDispatchTrigger> logger) : IShopifyDispatchTrigger
{
    public async Task TryDispatch(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        if (variantIds.Count == 0)
        {
            return;
        }

        if (!await featureManager.IsEnabledAsync(FeatureFlags.ShopifyAutoDispatch))
        {
            logger.LogDebug(
                "{Flag} is disabled. Skipping immediate dispatch for {Count} variant(s); they stay pending.",
                FeatureFlags.ShopifyAutoDispatch, variantIds.Count);
            return;
        }

        try
        {
            await dispatcher.DispatchVariants(variantIds, cancellationToken);
        }
        catch (Exception exception)
        {
            // Best-effort: the rows are already committed as pending, so the scheduled dispatch
            // run retries them. Never let a Shopify outage fail the ingest that triggered us —
            // for webhooks that would cause pointless SQS redeliveries of an already-absorbed message.
            logger.LogError(
                exception,
                "Immediate Shopify dispatch failed for {Count} variant(s). They stay pending; the scheduled dispatch will retry.",
                variantIds.Count);
        }
    }
}
