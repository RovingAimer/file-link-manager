using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

public sealed class LinkRecordQueryService
{
    public IReadOnlyList<PersistedLinkRecord> Query(LinkDatabase database, LinkRecordFilter? filter)
    {
        ArgumentNullException.ThrowIfNull(database);

        filter ??= new LinkRecordFilter();

        var records = database.Records
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
        MatchesSearch(record, filter.SearchText);

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

    private static bool MatchesSearch(PersistedLinkRecord record, string? searchText) =>
        string.IsNullOrWhiteSpace(searchText) ||
        record.LinkRecord.Request.SourcePath.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
        record.LinkRecord.Request.LinkPath.Contains(searchText, StringComparison.OrdinalIgnoreCase);
}
