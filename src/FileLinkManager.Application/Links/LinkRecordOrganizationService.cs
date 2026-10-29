using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

public enum MoveDirection { Up, Down }

public sealed class LinkRecordOrganizationService(ILinkRecordStore store)
{
    private readonly ILinkRecordStore store = store ?? throw new ArgumentNullException(nameof(store));

    public Task<LinkDatabase> UpdateMetadataAsync(string deviceKey, Guid recordId,
        LinkRecordMetadata metadata, CancellationToken cancellationToken) => ChangeAsync(database =>
    {
        metadata = ValidateMetadata(database, metadata);
        var record = FindRecord(database, deviceKey, recordId);
        var records = database.Records.ToArray();
        var index = Array.IndexOf(records, record);
        records[index] = record with { CustomName = metadata.CustomName, GroupId = metadata.GroupId };
        if (record.GroupId != metadata.GroupId)
        {
            var destination = OrderedRecords(database, metadata.GroupId).Append(records[index]).ToArray();
            var source = OrderedRecords(database, record.GroupId).Where(item => item != record).ToArray();
            ApplyOrder(records, source);
            ApplyOrder(records, destination);
        }
        return database with { Records = records };
    }, cancellationToken);

    public Task<LinkDatabase> CreateGroupAsync(string name, CancellationToken cancellationToken) => ChangeAsync(database =>
    {
        name = ValidateGroupName(database, name);
        var groups = OrderedGroups(database).Append(new LinkRecordGroup(Guid.NewGuid(), name, 0));
        return database with { Groups = groups.Select((group, index) => group with { SortOrder = index }).ToArray() };
    }, cancellationToken);

    public Task<LinkDatabase> RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken) => ChangeAsync(database =>
    {
        FindGroup(database, groupId);
        name = ValidateGroupName(database, name, groupId);
        return database with { Groups = database.Groups.Select(group => group.Id == groupId ? group with { Name = name } : group).ToArray() };
    }, cancellationToken);

    public Task<LinkDatabase> DissolveGroupAsync(Guid groupId, CancellationToken cancellationToken) => ChangeAsync(database =>
    {
        FindGroup(database, groupId);
        var destination = OrderedRecords(database, null).Concat(OrderedRecords(database, groupId)).ToArray();
        var records = database.Records.Select(record => record.GroupId == groupId ? record with { GroupId = null } : record).ToArray();
        ApplyOrder(records, destination);
        return database with
        {
            Groups = OrderedGroups(database).Where(group => group.Id != groupId)
                .Select((group, index) => group with { SortOrder = index }).ToArray(),
            Records = records
        };
    }, cancellationToken);

    public Task<LinkDatabase> MoveGroupAsync(Guid groupId, MoveDirection direction,
        CancellationToken cancellationToken, LinkRecordFilter? filter = null) => ChangeAsync(database =>
    {
        RequireUnfiltered(filter);
        FindGroup(database, groupId);
        var groups = OrderedGroups(database).ToArray();
        Move(groups, Array.FindIndex(groups, group => group.Id == groupId), direction);
        return database with { Groups = groups.Select((group, index) => group with { SortOrder = index }).ToArray() };
    }, cancellationToken);

    public Task<LinkDatabase> MoveRecordAsync(string deviceKey, Guid recordId, MoveDirection direction,
        CancellationToken cancellationToken, LinkRecordFilter? filter = null) => ChangeAsync(database =>
    {
        RequireUnfiltered(filter);
        var record = FindRecord(database, deviceKey, recordId);
        var ordered = OrderedRecords(database, record.GroupId).ToArray();
        Move(ordered, Array.FindIndex(ordered, item => item.Id == recordId && item.DeviceKey == deviceKey), direction);
        var records = database.Records.ToArray();
        ApplyOrder(records, ordered);
        return database with { Records = records };
    }, cancellationToken);

    internal static LinkRecordMetadata ValidateMetadata(LinkDatabase database, LinkRecordMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        metadata = metadata.Normalize();
        if (metadata.GroupId.HasValue) FindGroup(database, metadata.GroupId.Value);
        return metadata;
    }

    private async Task<LinkDatabase> ChangeAsync(Func<LinkDatabase, LinkDatabase> change, CancellationToken cancellationToken)
    {
        var database = await store.LoadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (database.SchemaVersion > LinkDatabase.CurrentSchemaVersion ||
            store is ILinkRecordStoreDiagnostics { LastLoadDiagnostics.IsReadOnly: true })
        {
            throw new InvalidOperationException("记录存储处于只读模式。");
        }
        var updated = change(database);
        cancellationToken.ThrowIfCancellationRequested();
        await store.SaveAsync(updated, cancellationToken);
        return updated;
    }

    private static PersistedLinkRecord FindRecord(LinkDatabase database, string deviceKey, Guid recordId) =>
        database.Records.SingleOrDefault(record => record.Id == recordId &&
            string.Equals(record.DeviceKey, deviceKey, StringComparison.Ordinal))
        ?? throw new ArgumentException("找不到指定记录。", nameof(recordId));

    private static LinkRecordGroup FindGroup(LinkDatabase database, Guid groupId) =>
        database.Groups.SingleOrDefault(group => group.Id == groupId)
        ?? throw new ArgumentException("所选分组已不存在。", nameof(groupId));

    private static string ValidateGroupName(LinkDatabase database, string name, Guid? exceptId = null)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0 || database.Groups.Any(group => group.Id != exceptId &&
            string.Equals(group.Name, name, StringComparison.Ordinal)))
            throw new ArgumentException("分组名称不能为空或与已有分组重复。", nameof(name));
        return name;
    }

    private static IOrderedEnumerable<LinkRecordGroup> OrderedGroups(LinkDatabase database) =>
        database.Groups.OrderBy(group => group.SortOrder);

    private static IOrderedEnumerable<PersistedLinkRecord> OrderedRecords(LinkDatabase database, Guid? groupId) =>
        database.Records.Where(record => record.GroupId == groupId).OrderBy(record => record.SortOrder);

    private static void ApplyOrder(PersistedLinkRecord[] records, IReadOnlyList<PersistedLinkRecord> ordered)
    {
        var orders = ordered.Select((record, index) => ((record.DeviceKey, record.Id), index))
            .ToDictionary(item => item.Item1, item => item.index);
        for (var index = 0; index < records.Length; index++)
            if (orders.TryGetValue((records[index].DeviceKey, records[index].Id), out var order))
                records[index] = records[index] with { SortOrder = order };
    }

    private static void Move<T>(T[] items, int index, MoveDirection direction)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        var target = index + (direction == MoveDirection.Up ? -1 : 1);
        if (target >= 0 && target < items.Length) (items[index], items[target]) = (items[target], items[index]);
    }

    private static void RequireUnfiltered(LinkRecordFilter? filter)
    {
        if (filter is not null && (!string.IsNullOrWhiteSpace(filter.DeviceKey) || filter.Status.HasValue ||
            !string.IsNullOrWhiteSpace(filter.SearchText) || filter.GroupId.HasValue || filter.UngroupedOnly))
            throw new InvalidOperationException("请先清除筛选，再调整顺序。");
    }
}
