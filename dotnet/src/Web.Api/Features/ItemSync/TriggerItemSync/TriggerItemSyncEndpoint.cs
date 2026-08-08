using Application.Jobs;
using FastEndpoints;
using Hangfire;

namespace Web.Api.Features.ItemSync.TriggerItemSync;

/// <summary>
/// Manually syncs a single variant on demand by enqueueing the work for the processing host, and
/// returns the job id so the caller can poll <c>jobs/{id}</c> for its outcome.
/// <para>
/// Enqueued rather than run in the request because the SkuLabs quota is <em>per account</em>: a push
/// made from here spends the same allowance as one made by the background worker, but escapes the
/// drain loop's pacing and is invisible to it. Keeping every SkuLabs request inside one host is what
/// makes that pacing mean anything.
/// </para>
/// <para>
/// Manual syncs still bypass the <c>ShopifyAutoDispatch</c>/<c>SkulabsAutoDispatch</c> flags — the
/// point of the button is to push one item even while the automatic cadence is off — and the
/// <c>ShopifyWriteBack</c> / <c>SkulabsWriteBack</c> kill switches still apply.
/// </para>
/// </summary>
public class TriggerItemSyncEndpoint(IBackgroundJobClient jobClient)
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
                "Enqueues a reconcile-and-dispatch for one variant on the processing host and returns "
                + "the job id; poll jobs/{id} for the outcome. Bypasses the automatic-dispatch flags. "
                + "The ShopifyWriteBack / SkulabsWriteBack kill switches still apply. "
                + "Rate limited to one request per 30 seconds per client.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var variantId = Route<Guid>("id", isRequired: true);

        var jobId = jobClient.Enqueue<SingleItemSyncJob>(job =>
            job.Run(variantId, CancellationToken.None));

        await Send.OkAsync(new TriggerItemSyncResponse(jobId), cancellationToken);
    }
}

/// <param name="JobId">Hangfire job id; poll <c>jobs/{id}</c> for its state.</param>
public readonly record struct TriggerItemSyncResponse(string JobId);
