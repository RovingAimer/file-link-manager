namespace FileLinkManager.Domain.Links;

public sealed record LinkFingerprint(
    uint? ReparseTag,
    string? NormalizedTargetPath,
    ulong? VolumeSerialNumber,
    string? FileIdHex,
    string? NormalizedLinkPath = null);
