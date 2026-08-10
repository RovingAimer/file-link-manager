using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Native;

namespace FileLinkManager.Infrastructure.Links;

public sealed partial class WindowsFileLinkService
{
    private Task<LinkInspection> CreateSymbolicLinkAsync(
        FileLinkType linkType,
        string normalizedSourcePath,
        string normalizedSourceComparisonPath,
        string normalizedLinkPath)
    {
        var symbolicLinkFlags = linkType == FileLinkType.DirectorySymbolicLink
            ? NativeConstants.SymbolicLinkFlagDirectory
            : NativeConstants.SymbolicLinkFlagFile;
        if (!NativeMethods.CreateSymbolicLink(
                normalizedLinkPath,
                normalizedSourcePath,
                symbolicLinkFlags | NativeConstants.SymbolicLinkFlagAllowUnprivilegedCreate))
        {
            throw WindowsFileIdentityReader.CreateWin32Exception("CreateSymbolicLinkW", normalizedLinkPath);
        }

        ReparsePointInfo reparsePoint;
        try
        {
            reparsePoint = reparsePointReader.Read(normalizedLinkPath);
        }
        catch (FileLinkException exception)
        {
            throw new FileLinkException(
                "CreateSymbolicLinkW",
                normalizedLinkPath,
                $"The symbolic link was created but could not be verified. {exception.Message}");
        }

        if (reparsePoint.Tag != NativeConstants.IoReparseTagSymbolicLink ||
            reparsePoint.FileLinkType != linkType ||
            !string.Equals(
                reparsePoint.TargetPath,
                normalizedSourceComparisonPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FileLinkException(
                "CreateSymbolicLinkW",
                normalizedLinkPath,
                "The symbolic link was created but remains unverified: the reparse tag, link type, or normalized target does not match the validated request.");
        }

        return Task.FromResult(CreateReparseLinkInspection(
            normalizedLinkPath,
            reparsePoint,
            LinkStatus.Valid,
            null,
            normalizedLinkPath));
    }

    private Task DeleteFileSymbolicLinkAsync(LinkRecord record)
    {
        var linkPath = record.Request.LinkPath;
        var validation = ValidateSymbolicLinkForDeletion(record, expectDirectory: false, "DeleteFileW");
        if (validation.Status == LinkStatus.Missing)
        {
            return Task.CompletedTask;
        }

        if (!NativeMethods.DeleteFile(linkPath))
        {
            throw WindowsFileIdentityReader.CreateWin32Exception("DeleteFileW", linkPath);
        }

        return Task.CompletedTask;
    }

    private Task DeleteDirectorySymbolicLinkAsync(LinkRecord record)
    {
        var linkPath = record.Request.LinkPath;
        var validation = ValidateSymbolicLinkForDeletion(record, expectDirectory: true, "RemoveDirectoryW");
        if (validation.Status == LinkStatus.Missing)
        {
            return Task.CompletedTask;
        }

        if (!NativeMethods.RemoveDirectory(linkPath))
        {
            throw WindowsFileIdentityReader.CreateWin32Exception("RemoveDirectoryW", linkPath);
        }

        return Task.CompletedTask;
    }

    private LinkInspection ValidateSymbolicLinkForDeletion(
        LinkRecord record,
        bool expectDirectory,
        string operation)
    {
        var linkPath = record.Request.LinkPath;
        var expectedFingerprint = record.CreationFingerprint;
        var expectedType = expectDirectory
            ? FileLinkType.DirectorySymbolicLink
            : FileLinkType.FileSymbolicLink;
        var fingerprintDescription = expectDirectory ? "directory symbolic-link" : "file symbolic-link";
        var linkDescription = expectDirectory ? "directory symbolic link" : "file symbolic link";
        if (expectedFingerprint.ReparseTag != NativeConstants.IoReparseTagSymbolicLink ||
            string.IsNullOrEmpty(expectedFingerprint.NormalizedTargetPath))
        {
            throw new FileLinkException(
                operation,
                linkPath,
                $"Conflict: the persisted {fingerprintDescription} fingerprint has an invalid tag or target.");
        }

        var attributes = GetPathAttributes(linkPath);
        if (!attributes.HasValue)
        {
            return new LinkInspection(linkPath, expectedType, LinkStatus.Missing, null, null);
        }

        if (expectDirectory && (attributes.Value & FileAttributes.Directory) == 0)
        {
            throw new FileLinkException(
                operation,
                linkPath,
                "Conflict: the link path has the wrong type; expected a directory symbolic link, but found a file.");
        }

        if (!expectDirectory && (attributes.Value & FileAttributes.Directory) != 0)
        {
            throw new FileLinkException(
                operation,
                linkPath,
                "Conflict: the link path has the wrong type; expected a file symbolic link, but found a directory.");
        }

        if ((attributes.Value & FileAttributes.ReparsePoint) == 0)
        {
            var entryType = expectDirectory ? "ordinary directory" : "ordinary file";
            throw new FileLinkException(
                operation,
                linkPath,
                $"Conflict: the link path is an {entryType}, not a {linkDescription}.");
        }

        ReparsePointInfo current;
        try
        {
            current = reparsePointReader.Read(linkPath);
        }
        catch (FileLinkException exception)
        {
            throw new FileLinkException(
                operation,
                linkPath,
                $"Conflict: the link entry could not be verified as the persisted {linkDescription}. {exception.Message}");
        }

        if (current.FileLinkType != expectedType ||
            current.Tag != expectedFingerprint.ReparseTag)
        {
            throw new FileLinkException(
                operation,
                linkPath,
                $"Conflict: the reparse tag or link type does not match the persisted {fingerprintDescription} fingerprint.");
        }

        if (!string.Equals(
                current.TargetPath,
                expectedFingerprint.NormalizedTargetPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FileLinkException(
                operation,
                linkPath,
                "Conflict: the symbolic-link target does not match the persisted fingerprint.");
        }

        return CreateReparseLinkInspection(linkPath, current, LinkStatus.Valid, null);
    }
}
