using Application.Jobs.Maintenance;
using Application.Skulabs.Services;
using Microsoft.Extensions.Logging;

namespace Application.Skulabs.Maintenance;

/// <summary>
/// Maintenance task that reconciles every linked SkuLabs item's title with the authoritative
/// variant display name held locally. The outbound SkuLabs write inside the service is gated
/// by the <c>SkulabsWriteBack</c> feature flag; this task itself runs unconditionally so the
/// next sweep keeps surfacing drift even when external writes are off. Invoked by
/// <see cref="ProductMaintenanceJob"/>; not registered as a standalone Quartz job.
/// </summary>
[TaskOrder(3)]
public class SkulabsTitleSyncTask(
    ISkulabsTitleSyncService syncService,
    ILogger<SkulabsTitleSyncTask> logger
) : IMaintenanceTask
{
    public string Name => nameof(SkulabsTitleSyncTask);

    public async Task Execute(CancellationToken cancellationToken)
    {
        var result = await syncService.SyncAll(cancellationToken);

        logger.LogInformation(
            "SkulabsTitleSyncTask reconciliation: Checked: {Checked}, Drifted: {Drifted}, Corrected: {Corrected}, Failed: {Failed}.",
            result.Checked,
            result.Drifted,
            result.Corrected,
            result.Failed
        );
    }
}
