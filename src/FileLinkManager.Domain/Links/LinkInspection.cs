namespace FileLinkManager.Domain.Links;

public sealed record LinkInspection(
    string LinkPath,
    FileLinkType? Type,
    LinkStatus Status,
    LinkFingerprint? Fingerprint,
    string? Error);
