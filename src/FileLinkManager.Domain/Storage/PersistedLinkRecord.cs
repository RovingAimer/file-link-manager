using FileLinkManager.Domain.Links;

namespace FileLinkManager.Domain.Storage;

public sealed record PersistedLinkRecord(
    Guid Id,
    string DeviceKey,
    string DeviceDisplayNameAtCreation,
    LinkRecord LinkRecord,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastCheckedUtc,
    DateTimeOffset? DeletedUtc,
    LinkStatus Status,
    string? StatusReason)
{
    public string? CustomName { get; init; }
    public Guid? GroupId { get; init; }
    public int SortOrder { get; init; }
}
