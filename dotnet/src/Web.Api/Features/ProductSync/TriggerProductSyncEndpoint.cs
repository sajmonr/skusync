using Application.Jobs;
using FastEndpoints;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;

namespace Web.Api.Features.ProductSync;

/// <summary>
/// Schedules a full sync as a background job and returns immediately with its id. AppServer's
/// Hangfire server picks the job up and runs <see cref="FullSyncOrchestrator.RunFullSync"/> — the
/// Shopify product import followed by the SkuLabs item, SKU/barcode, and title passes, each gated by
/// its own feature flag — to completion; the client polls <c>GET /jobs/{id}</c> for the outcome. If a
/// sync is already queued or running, its existing id is returned instead of stacking a second run.
/// </summary>
public class TriggerProductSyncEndpoint(IBackgroundJobClient jobClient, JobStorage jobStorage)
    : EndpointWithoutRequest<TriggerProductSyncResponse>
{
    public override void Configure()
    {
        Post("product-sync");
        Options(endpoint => endpoint.RequireRateLimiting(ProductSyncRateLimitingExtensions.PolicyName));
        Summary(summary =>
        {
            summary.Summary = "Trigger a full sync";
            summary.Description =
                "Enqueues a background full sync — Shopify product import and deduplication, then the "
                + "SkuLabs item, SKU/barcode, and title passes (each honouring its feature flag) — and "
                + "returns its job id. Returns the existing job when one is already queued or running. "
                + "Rate limited to one request per 30 seconds per client.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var existingJobId = FindActiveFullSyncJobId();
        if (existingJobId is not null)
        {
            await Send.OkAsync(new TriggerProductSyncResponse(existingJobId, AlreadyRunning: true), cancellationToken);
            return;
        }

        var jobId = jobClient.Enqueue<FullSyncOrchestrator>(orchestrator =>
            orchestrator.RunFullSync(CancellationToken.None));
        await Send.OkAsync(new TriggerProductSyncResponse(jobId, AlreadyRunning: false), cancellationToken);
    }

    /// <summary>
    /// Returns the id of a full-sync job that is already enqueued or processing, or null when none is
    /// active. This is a best-effort single-flight guard: the check and the subsequent enqueue are not
    /// atomic, so a narrow race remains under simultaneous requests (acceptable for a human-driven
    /// button). The orchestrator's own <c>DisableConcurrentExecution</c> lock closes it for stricter needs.
    /// </summary>
    private string? FindActiveFullSyncJobId()
    {
        var monitoringApi = jobStorage.GetMonitoringApi();

        foreach (var (jobId, dto) in monitoringApi.ProcessingJobs(0, int.MaxValue))
        {
            if (IsFullSync(dto.Job))
            {
                return jobId;
            }
        }

        foreach (var (jobId, dto) in monitoringApi.EnqueuedJobs(EnqueuedState.DefaultQueue, 0, int.MaxValue))
        {
            if (IsFullSync(dto.Job))
            {
                return jobId;
            }
        }

        return null;
    }

    private static bool IsFullSync(Job? job) =>
        job?.Type == typeof(FullSyncOrchestrator) && job.Method.Name == nameof(FullSyncOrchestrator.RunFullSync);
}

/// <param name="JobId">The background job's id — poll <c>GET /jobs/{id}</c> for its state.</param>
/// <param name="AlreadyRunning">True when this id refers to a sync that was already in flight.</param>
public readonly record struct TriggerProductSyncResponse(string JobId, bool AlreadyRunning);
