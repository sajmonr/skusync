using FastEndpoints;

namespace Web.Api.Features.Status;

public class GetApiStatusEndpoint : EndpointWithoutRequest<ApiStatusResponse>
{
    public override void Configure()
    {
        Get("status");
        Summary(summary =>
        {
            summary.Summary = "Get API status";
            summary.Description = "Confirms that the HTTP API is available.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        await Send.OkAsync(new ApiStatusResponse("ok", DateTime.UtcNow), cancellationToken);
    }
}

public readonly record struct ApiStatusResponse(string Status, DateTime UtcNow);
