using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

public sealed class RemoveDeletedLinkRecordUseCase
{
    private readonly ILinkRecordStore recordStore;

    public RemoveDeletedLinkRecordUseCase(ILinkRecordStore recordStore)
    {
        this.recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
    }

    public async Task<RemoveDeletedLinkRecordOutcome> ExecuteAsync(
        string deviceKey,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = await recordStore.LoadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var recordIndex = FindExactRecord(database.Records, deviceKey, recordId);
        if (recordIndex < 0)
        {
            return new RemoveDeletedLinkRecordRejected(
                database,
                "找不到与当前设备和记录身份完全匹配的历史记录。");
        }

        var record = database.Records[recordIndex];
        if (!IsDeleted(record))
        {
            return new RemoveDeletedLinkRecordRejected(
                database,
                "只有已删除的历史记录可以删除记录。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var records = database.Records
            .Where((_, index) => index != recordIndex)
            .ToArray();
        var updatedDatabase = database with { Records = records };
        cancellationToken.ThrowIfCancellationRequested();
        await recordStore.SaveAsync(updatedDatabase, cancellationToken);
        return new DeletedLinkRecordRemoved(updatedDatabase, record);
    }

    private static int FindExactRecord(
        IReadOnlyList<PersistedLinkRecord> records,
        string deviceKey,
        Guid recordId)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
        {
            return -1;
        }

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.Id == recordId &&
                string.Equals(record.DeviceKey, deviceKey, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsDeleted(PersistedLinkRecord record) =>
        record.Status == LinkStatus.Deleted || record.DeletedUtc is not null;
}
