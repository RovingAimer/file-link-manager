using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

public sealed record InspectLinksResult(LinkDatabase Database, int InspectedCount);

public sealed class InspectLinksUseCase
{
    private readonly IFileLinkService fileLinkService;
    private readonly ILinkRecordStore recordStore;
    private readonly IDeviceIdentityProvider deviceIdentityProvider;
    private readonly TimeProvider timeProvider;

    public InspectLinksUseCase(
        IFileLinkService fileLinkService,
        ILinkRecordStore recordStore,
        IDeviceIdentityProvider deviceIdentityProvider,
        TimeProvider? timeProvider = null)
    {
        this.fileLinkService = fileLinkService ?? throw new ArgumentNullException(nameof(fileLinkService));
        this.recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        this.deviceIdentityProvider = deviceIdentityProvider ?? throw new ArgumentNullException(nameof(deviceIdentityProvider));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<InspectLinksResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = await recordStore.LoadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var currentDevice = deviceIdentityProvider.GetCurrent();
        cancellationToken.ThrowIfCancellationRequested();
        var checkedAt = timeProvider.GetUtcNow().ToUniversalTime();
        var records = database.Records.ToArray();
        var inspectedCount = 0;

        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            if (!IsEligible(record, currentDevice))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var inspection = await fileLinkService.InspectAsync(record.LinkRecord.Request.LinkPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var (status, reason) = LinkFingerprintReconciliation.Reconcile(record.LinkRecord, inspection);
                records[index] = record with
                {
                    LastCheckedUtc = checkedAt,
                    Status = status,
                    StatusReason = reason
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                records[index] = record with
                {
                    LastCheckedUtc = checkedAt,
                    Status = LinkStatus.Unknown,
                    StatusReason = string.IsNullOrWhiteSpace(exception.Message)
                        ? "The link inspection failed."
                        : exception.Message
                };
            }

            inspectedCount++;
        }

        var updatedDatabase = database with
        {
            Devices = DeviceIdentityCollection.Merge(database.Devices, currentDevice),
            Records = records
        };

        cancellationToken.ThrowIfCancellationRequested();
        await recordStore.SaveAsync(updatedDatabase, cancellationToken);
        return new InspectLinksResult(updatedDatabase, inspectedCount);
    }

    private static bool IsEligible(PersistedLinkRecord record, DeviceIdentity currentDevice) =>
        record.DeletedUtc is null &&
        record.Status != LinkStatus.Deleted &&
        string.Equals(record.DeviceKey, currentDevice.DeviceKey, StringComparison.Ordinal);

}
