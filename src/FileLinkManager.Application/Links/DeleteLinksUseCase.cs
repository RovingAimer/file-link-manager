using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

public sealed class DeleteLinksUseCase
{
    private readonly IFileLinkService fileLinkService;
    private readonly ILinkRecordStore recordStore;
    private readonly IDeviceIdentityProvider deviceIdentityProvider;
    private readonly TimeProvider timeProvider;
    private readonly Func<string, bool> sourcePathExists;

    public DeleteLinksUseCase(
        IFileLinkService fileLinkService,
        ILinkRecordStore recordStore,
        IDeviceIdentityProvider deviceIdentityProvider,
        TimeProvider? timeProvider = null,
        Func<string, bool>? sourcePathExists = null)
    {
        this.fileLinkService = fileLinkService ?? throw new ArgumentNullException(nameof(fileLinkService));
        this.recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        this.deviceIdentityProvider = deviceIdentityProvider ?? throw new ArgumentNullException(nameof(deviceIdentityProvider));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.sourcePathExists = sourcePathExists ?? DefaultSourcePathExists;
    }

    public async Task<DeleteLinksOutcome> ExecuteAsync(
        Guid recordId,
        bool confirmMissing,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = await recordStore.LoadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var currentDevice = deviceIdentityProvider.GetCurrent();
        cancellationToken.ThrowIfCancellationRequested();
        var recordIndex = FindCurrentActiveRecord(database.Records, recordId, currentDevice.DeviceKey);
        if (recordIndex < 0)
        {
            return new DeleteLinkRejected("The link record was not found for the current device or is already deleted.");
        }

        var record = database.Records[recordIndex];
        var timestamp = timeProvider.GetUtcNow().ToUniversalTime();
        cancellationToken.ThrowIfCancellationRequested();
        var linkInspection = await fileLinkService.InspectAsync(record.LinkRecord.Request.LinkPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var (status, reason) = LinkFingerprintReconciliation.Reconcile(record.LinkRecord, linkInspection);
        var inspectedRecord = record with
        {
            LastCheckedUtc = timestamp,
            Status = status,
            StatusReason = reason
        };

        if (status == LinkStatus.Missing)
        {
            if (!confirmMissing)
            {
                await SaveAsync(database, recordIndex, inspectedRecord, cancellationToken);
                return new MissingConfirmationRequired(inspectedRecord);
            }

            var confirmedDeleted = inspectedRecord with
            {
                Status = LinkStatus.Deleted,
                DeletedUtc = timestamp
            };
            await SaveAsync(database, recordIndex, confirmedDeleted, cancellationToken);
            return new DeletedAndRegistered(confirmedDeleted, null);
        }

        if (status != LinkStatus.Valid)
        {
            await SaveAsync(database, recordIndex, inspectedRecord, cancellationToken);
            return new DeleteLinkRejected(reason ?? "The link cannot be deleted because its current state is not valid.");
        }

        if (record.LinkRecord.Request.Type == FileLinkType.FileHardLink)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceInspection = await fileLinkService.InspectAsync(
                record.LinkRecord.Request.SourcePath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var (sourceStatus, sourceReason) = LinkFingerprintReconciliation.Reconcile(
                record.LinkRecord,
                sourceInspection);
            if (sourceStatus != LinkStatus.Valid)
            {
                var sourceConflictReason =
                    $"The hard-link source could not be verified before deletion. {sourceReason ?? "The source identity is not valid."}";
                var conflictedRecord = inspectedRecord with
                {
                    Status = LinkStatus.Conflict,
                    StatusReason = sourceConflictReason
                };
                await SaveAsync(database, recordIndex, conflictedRecord, cancellationToken);
                return new DeleteLinkRejected(sourceConflictReason);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await fileLinkService.DeleteAsync(record.LinkRecord, cancellationToken);

        var sourceExists = sourcePathExists(record.LinkRecord.Request.SourcePath);
        var sourceCheckReason = GetSourceCheckReason(sourceExists);
        var deletedRecord = inspectedRecord with
        {
            Status = LinkStatus.Deleted,
            StatusReason = sourceCheckReason,
            DeletedUtc = timestamp,
            LastCheckedUtc = timestamp
        };

        try
        {
            await SaveAsync(database, recordIndex, deletedRecord, CancellationToken.None);
            return new DeletedAndRegistered(deletedRecord, sourceCheckReason);
        }
        catch (Exception exception) when (exception is LinkRecordStoreException or OperationCanceledException)
        {
            return new DeletedButUnregistered(deletedRecord, exception.Message, sourceCheckReason);
        }
    }

    private static int FindCurrentActiveRecord(
        IReadOnlyList<PersistedLinkRecord> records,
        Guid recordId,
        string deviceKey)
    {
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.Id == recordId &&
                record.DeletedUtc is null &&
                record.Status != LinkStatus.Deleted &&
                string.Equals(record.DeviceKey, deviceKey, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private async Task SaveAsync(
        LinkDatabase database,
        int recordIndex,
        PersistedLinkRecord updatedRecord,
        CancellationToken cancellationToken)
    {
        var records = database.Records.ToArray();
        records[recordIndex] = updatedRecord;
        var updatedDatabase = database with { Records = records };
        cancellationToken.ThrowIfCancellationRequested();
        await recordStore.SaveAsync(updatedDatabase, cancellationToken);
    }

    private static string? GetSourceCheckReason(bool sourceExists) =>
        sourceExists
            ? null
            : "Source verification after deleting the link was not clean: the source path could not be found or accessed.";

    private static bool DefaultSourcePathExists(string path) =>
        File.Exists(path) || Directory.Exists(path);
}
