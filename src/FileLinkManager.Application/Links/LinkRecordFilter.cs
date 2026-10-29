using FileLinkManager.Domain.Links;

namespace FileLinkManager.Application.Links;

public sealed record LinkRecordFilter(
    string? DeviceKey = null,
    LinkStatus? Status = null,
    string? SearchText = null,
    Guid? GroupId = null,
    bool UngroupedOnly = false,
    LinkRecordSearchScope SearchScope = LinkRecordSearchScope.Paths);

public enum LinkRecordSearchScope
{
    Paths,
    Name,
    SourcePath,
    LinkPath
}
