using System.Runtime.InteropServices;
using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Native;
using Microsoft.Win32.SafeHandles;

namespace FileLinkManager.Infrastructure.Links;

public sealed partial class WindowsFileLinkService
{
    private Task<LinkInspection> CreateHardLinkAsync(
        string normalizedSourcePath,
        string normalizedSourceComparisonPath,
        string normalizedLinkPath,
        CancellationToken cancellationToken)
    {
        if (!NativeMethods.CreateHardLink(normalizedLinkPath, normalizedSourcePath, 0))
        {
            throw WindowsFileIdentityReader.CreateWin32Exception("CreateHardLinkW", normalizedLinkPath);
        }

        return CreateHardLinkInspectionAsync(
            normalizedSourceComparisonPath,
            normalizedLinkPath,
            cancellationToken);
    }

    private Task DeleteHardLinkAsync(LinkRecord record, CancellationToken cancellationToken)
    {
        var request = record.Request;
        using var linkHandle = TryOpenHardLinkHandleForDelete(request.LinkPath);
        if (linkHandle is null)
        {
            return Task.CompletedTask;
        }

        var linkAttributes = reparsePointReader.GetHandleAttributes(linkHandle, request.LinkPath);
        if ((linkAttributes & FileAttributes.Directory) != 0)
        {
            throw new FileLinkException("DeleteFileW", request.LinkPath, "Conflict: the link path is not a file.");
        }

        if ((linkAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new FileLinkException(
                "DeleteFileW",
                request.LinkPath,
                "Conflict: the link path is a reparse point, not the persisted hard link.");
        }

        var currentFingerprint = identityReader.Read(linkHandle, request.LinkPath);
        if (!HasSameFileIdentity(record.CreationFingerprint, currentFingerprint))
        {
            throw new FileLinkException(
                "DeleteFileW",
                request.LinkPath,
                "Conflict: the file identity does not match the persisted link fingerprint.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var sourceHandle = OpenHardLinkSourceHandle(request.SourcePath);
        var sourceAttributes = reparsePointReader.GetHandleAttributes(sourceHandle, request.SourcePath);
        if ((sourceAttributes & FileAttributes.Directory) != 0 ||
            (sourceAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new FileLinkException(
                "DeleteFileW",
                request.SourcePath,
                "Conflict: the hard-link source path is not an ordinary file entry.");
        }

        var sourceFingerprint = identityReader.Read(sourceHandle, request.SourcePath);
        if (!HasSameFileIdentity(record.CreationFingerprint, sourceFingerprint) ||
            !HasSameFileIdentity(currentFingerprint, sourceFingerprint))
        {
            throw new FileLinkException(
                "DeleteFileW",
                request.SourcePath,
                "Conflict: the source file identity does not match the persisted hard-link fingerprint.");
        }

        beforeHardLinkDisposition?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();

        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        if (!NativeMethods.SetFileInformationByHandle(
                linkHandle,
                NativeConstants.FileDispositionInfo,
                ref disposition,
                (uint)Marshal.SizeOf<FileDispositionInfo>()))
        {
            throw WindowsFileIdentityReader.CreateWin32Exception("SetFileInformationByHandle", request.LinkPath);
        }

        return Task.CompletedTask;
    }

    private async Task<LinkInspection> CreateHardLinkInspectionAsync(
        string normalizedSourceComparisonPath,
        string normalizedLinkPath,
        CancellationToken cancellationToken)
    {
        var inspection = await InspectAsync(normalizedLinkPath, cancellationToken);
        var fingerprint = inspection.Fingerprint;
        return fingerprint is null
            ? inspection
            : inspection with
            {
                LinkPath = normalizedLinkPath,
                Fingerprint = fingerprint with
                {
                    NormalizedTargetPath = normalizedSourceComparisonPath,
                    NormalizedLinkPath = normalizedLinkPath
                }
            };
    }

    private static bool HasSameFileIdentity(LinkFingerprint expected, LinkFingerprint actual)
    {
        return expected.VolumeSerialNumber.HasValue
            && actual.VolumeSerialNumber.HasValue
            && expected.VolumeSerialNumber == actual.VolumeSerialNumber
            && !string.IsNullOrEmpty(expected.FileIdHex)
            && string.Equals(expected.FileIdHex, actual.FileIdHex, StringComparison.Ordinal);
    }

    private static SafeFileHandle? TryOpenHardLinkHandleForDelete(string path)
    {
        var handle = NativeMethods.CreateFile(
            path,
            NativeConstants.Delete,
            NativeConstants.FileShareRead | NativeConstants.FileShareWrite | NativeConstants.FileShareDelete,
            securityAttributes: 0,
            NativeConstants.OpenExisting,
            NativeConstants.FileFlagOpenReparsePoint | NativeConstants.FileAttributeNormal,
            templateFile: 0);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        var errorCode = Marshal.GetLastWin32Error();
        handle.Dispose();
        if (errorCode is 2 or 3)
        {
            return null;
        }

        throw new FileLinkException(
            "DeleteFileW",
            path,
            $"Conflict: the link entry could not be opened for safe deletion. Win32 error {errorCode}.");
    }

    private static SafeFileHandle OpenHardLinkSourceHandle(string path)
    {
        var handle = NativeMethods.CreateFile(
            path,
            NativeConstants.GenericRead,
            NativeConstants.FileShareRead | NativeConstants.FileShareWrite,
            securityAttributes: 0,
            NativeConstants.OpenExisting,
            NativeConstants.FileFlagOpenReparsePoint | NativeConstants.FileAttributeNormal,
            templateFile: 0);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        var errorCode = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new FileLinkException(
            "DeleteFileW",
            path,
            $"Conflict: the hard-link source entry could not be opened and held for safe deletion. Win32 error {errorCode}.");
    }
}
