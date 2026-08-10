using FileLinkManager.Domain.Links;

namespace FileLinkManager.Application.Links;

internal static class LinkFingerprintReconciliation
{
    internal static (LinkStatus Status, string? Reason) Reconcile(LinkRecord record, LinkInspection inspection)
    {
        if (inspection.Status != LinkStatus.Valid)
        {
            return (inspection.Status, inspection.Error);
        }

        if (inspection.Type != record.Request.Type)
        {
            return (LinkStatus.Conflict, "The inspected link type does not match the persisted link type.");
        }

        if (!HasCompatibleFingerprint(record.Request.Type, record.CreationFingerprint, inspection.Fingerprint))
        {
            return (LinkStatus.Conflict, GetFingerprintConflictReason(record.Request.Type));
        }

        return (LinkStatus.Valid, inspection.Error);
    }

    private static bool HasCompatibleFingerprint(
        FileLinkType linkType,
        LinkFingerprint creationFingerprint,
        LinkFingerprint? inspectionFingerprint) =>
        linkType switch
        {
            FileLinkType.FileHardLink =>
                creationFingerprint.VolumeSerialNumber.HasValue &&
                inspectionFingerprint?.VolumeSerialNumber == creationFingerprint.VolumeSerialNumber &&
                !string.IsNullOrEmpty(creationFingerprint.FileIdHex) &&
                !string.IsNullOrEmpty(inspectionFingerprint.FileIdHex) &&
                string.Equals(inspectionFingerprint.FileIdHex, creationFingerprint.FileIdHex, StringComparison.OrdinalIgnoreCase),
            FileLinkType.FileSymbolicLink or FileLinkType.DirectorySymbolicLink or FileLinkType.DirectoryJunction =>
                creationFingerprint.ReparseTag.HasValue &&
                inspectionFingerprint?.ReparseTag == creationFingerprint.ReparseTag &&
                !string.IsNullOrEmpty(creationFingerprint.NormalizedTargetPath) &&
                !string.IsNullOrEmpty(inspectionFingerprint.NormalizedTargetPath) &&
                string.Equals(
                    inspectionFingerprint.NormalizedTargetPath,
                    creationFingerprint.NormalizedTargetPath,
                    StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static string GetFingerprintConflictReason(FileLinkType linkType) =>
        linkType == FileLinkType.FileHardLink
            ? "The inspected hard link volume serial number or file ID does not match the creation fingerprint."
            : "The inspected reparse tag or normalized target path does not match the creation fingerprint.";
}
