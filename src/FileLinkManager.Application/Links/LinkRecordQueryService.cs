using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

public sealed class LinkRecordQueryService
{
    public IReadOnlyList<PersistedLinkRecord> Query(LinkDatabase database, LinkRecordFilter? filter)
    {
        ArgumentNullException.ThrowIfNull(database);

        filter ??= new LinkRecordFilter();

        var groupOrder = database.Groups.OrderBy(group => group.SortOrder)
            .Select((group, index) => (group.Id, index)).ToDictionary(item => item.Id, item => item.index);
        var records = database.Records
            .OrderBy(record => record.GroupId.HasValue ? groupOrder.GetValueOrDefault(record.GroupId.Value, int.MaxValue) : int.MaxValue)
            .ThenBy(record => record.SortOrder)
            .Where(record => Matches(record, filter))
            .ToArray();

        return Array.AsReadOnly(records);
    }

    public LinkStatistics GetStatistics(LinkDatabase database, string? deviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        var historicalTotal = 0;
        var currentValid = 0;
        foreach (var record in database.Records)
        {
            if (!MatchesDevice(record, deviceKey))
            {
                continue;
            }

            historicalTotal++;
            if (record.Status == LinkStatus.Valid && record.DeletedUtc is null)
            {
                currentValid++;
            }
        }

        return new LinkStatistics(historicalTotal, currentValid);
    }

    private static bool Matches(PersistedLinkRecord record, LinkRecordFilter filter) =>
        MatchesDevice(record, filter.DeviceKey) &&
        MatchesStatus(record, filter.Status) &&
        (!filter.GroupId.HasValue || record.GroupId == filter.GroupId) &&
        (!filter.UngroupedOnly || record.GroupId is null) &&
        MatchesSearch(record, filter.SearchText, filter.SearchScope);

    private static bool MatchesDevice(PersistedLinkRecord record, string? deviceKey) =>
        string.IsNullOrWhiteSpace(deviceKey) ||
        string.Equals(record.DeviceKey, deviceKey, StringComparison.Ordinal);

    private static bool MatchesStatus(PersistedLinkRecord record, LinkStatus? status) =>
        !status.HasValue
            ? true
            : status.Value == LinkStatus.Deleted
                ? IsDeleted(record)
                : !IsDeleted(record) && record.Status == status.Value;

    private static bool IsDeleted(PersistedLinkRecord record) =>
        record.Status == LinkStatus.Deleted || record.DeletedUtc is not null;

    private static bool MatchesSearch(PersistedLinkRecord record, string? searchText, LinkRecordSearchScope scope)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return true;
        return scope switch
        {
            LinkRecordSearchScope.Name => record.CustomName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true,
            LinkRecordSearchScope.SourcePath => record.LinkRecord.Request.SourcePath.Contains(searchText, StringComparison.OrdinalIgnoreCase),
            LinkRecordSearchScope.LinkPath => record.LinkRecord.Request.LinkPath.Contains(searchText, StringComparison.OrdinalIgnoreCase),
            _ => record.LinkRecord.Request.SourcePath.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                 record.LinkRecord.Request.LinkPath.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        };
    }
}
