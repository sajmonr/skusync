using FastEndpoints;
using Hangfire;
using Hangfire.Storage;

namespace Web.Api.Features.Jobs;

/// <summary>
/// Returns the current state of a background job so the client can poll a triggered job to
/// completion. State is one of Hangfire's state names — <c>Enqueued</c>, <c>Processing</c>,
/// <c>Succeeded</c>, <c>Failed</c>, <c>Scheduled</c>, <c>Deleted</c>.
/// </summary>
public class GetJobStatusEndpoint(JobStorage jobStorage) : EndpointWithoutRequest<JobStatusResponse>
{
    public override void Configure()
    {
        Get("jobs/{id}");
        Summary(summary =>
        {
            summary.Summary = "Get background job status";
            summary.Description = "Returns the current state of a background job by id (404 if unknown).";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<string>("id");

        using var connection = jobStorage.GetConnection();
        var jobData = connection.GetJobData(id!);
        if (jobData is null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(new JobStatusResponse(id!, jobData.State ?? "Unknown"), cancellationToken);
    }
}

/// <param name="Id">The background job id.</param>
/// <param name="State">The Hangfire state name (Enqueued/Processing/Succeeded/Failed/…).</param>
public readonly record struct JobStatusResponse(string Id, string State);
