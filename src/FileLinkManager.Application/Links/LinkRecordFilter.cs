using FileLinkManager.Domain.Links;

namespace FileLinkManager.Application.Links;

public sealed record LinkRecordFilter(
    string? DeviceKey = null,
    LinkStatus? Status = null,
    string? SearchText = null);
