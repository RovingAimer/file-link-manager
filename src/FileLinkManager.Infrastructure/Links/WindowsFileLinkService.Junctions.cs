using System.Runtime.InteropServices;
using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Native;
using Microsoft.Win32.SafeHandles;

namespace FileLinkManager.Infrastructure.Links;

public sealed partial class WindowsFileLinkService
{
    private Task<LinkInspection> CreateDirectoryJunctionAsync(
        string normalizedSourcePath,
        string normalizedSourceComparisonPath,
        string normalizedLinkPath)
    {
        var placeholderCreated = false;
        SafeFileHandle? placeholderHandle = null;
        try
        {
            if (!NativeMethods.CreateDirectory(normalizedLinkPath, securityAttributes: 0))
            {
                throw WindowsFileIdentityReader.CreateWin32Exception("CreateDirectoryW", normalizedLinkPath);
            }

            placeholderCreated = true;
            placeholderHandle = OpenDirectoryHandle(
                normalizedLinkPath,
                NativeConstants.GenericWrite | NativeConstants.Delete);
            var buffer = ReparsePointBuffer.CreateMountPointBuffer(normalizedSourcePath);
            if (setMountPointReparseData is null)
            {
                SetMountPointReparseData(placeholderHandle, normalizedLinkPath, buffer);
            }
            else
            {
                setMountPointReparseData(normalizedLinkPath, buffer);
            }

            ReparsePointInfo reparsePoint;
            try
            {
                reparsePoint = reparsePointReader.Read(placeholderHandle, normalizedLinkPath);
            }
            catch (FileLinkException exception)
            {
                throw new FileLinkException(
                    "FSCTL_SET_REPARSE_POINT",
                    normalizedLinkPath,
                    $"The directory junction was created but could not be verified. {exception.Message}");
            }

            if (reparsePoint.Tag != NativeConstants.IoReparseTagMountPoint ||
                reparsePoint.FileLinkType != FileLinkType.DirectoryJunction ||
                !string.Equals(
                    reparsePoint.TargetPath,
                    normalizedSourceComparisonPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new FileLinkException(
                    "FSCTL_SET_REPARSE_POINT",
                    normalizedLinkPath,
                    "The directory junction was created but remains unverified: the reparse tag, link type, or normalized target does not match the validated request.");
            }

            return Task.FromResult(CreateReparseLinkInspection(
                normalizedLinkPath,
                reparsePoint,
                LinkStatus.Valid,
                null,
                normalizedLinkPath));
        }
        catch
        {
            if (placeholderCreated && placeholderHandle is not null)
            {
                TryRemoveEmptyOrdinaryDirectory(placeholderHandle, normalizedLinkPath);
            }

            throw;
        }
        finally
        {
            placeholderHandle?.Dispose();
        }
    }

    private Task DeleteDirectoryJunctionAsync(LinkRecord record)
    {
        var linkPath = record.Request.LinkPath;
        var expectedFingerprint = record.CreationFingerprint;
        if (expectedFingerprint.ReparseTag != NativeConstants.IoReparseTagMountPoint ||
            string.IsNullOrEmpty(expectedFingerprint.NormalizedTargetPath))
        {
            throw new FileLinkException(
                "SetFileInformationByHandle",
                linkPath,
                "Conflict: the persisted directory-junction fingerprint has an invalid tag or target.");
        }

        using var handle = TryOpenDirectoryHandleForDelete(linkPath);
        if (handle is null)
        {
            return Task.CompletedTask;
        }

        FileAttributes attributes;
        try
        {
            attributes = reparsePointReader.GetHandleAttributes(handle, linkPath);
        }
        catch (FileLinkException exception)
        {
            throw new FileLinkException(
                "SetFileInformationByHandle",
                linkPath,
                $"Conflict: the link entry could not be verified as the persisted directory junction. {exception.Message}");
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new FileLinkException(
                "SetFileInformationByHandle",
                linkPath,
                "Conflict: the link path has the wrong type; expected a directory junction, but found a file.");
        }

        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            throw new FileLinkException(
                "SetFileInformationByHandle",
                linkPath,
                "Conflict: the link path is an ordinary directory, not a directory junction.");
        }

        ReparsePointInfo current;
        try
        {
            current = reparsePointReader.Read(handle, linkPath);
        }
        catch (FileLinkException exception)
        {
            throw new FileLinkException(
                "SetFileInformationByHandle",
                linkPath,
                $"Conflict: the link entry could not be verified as the persisted directory junction. {exception.Message}");
        }

        if (current.FileLinkType != FileLinkType.DirectoryJunction ||
            current.Tag != expectedFingerprint.ReparseTag)
        {
            throw new FileLinkException(
                "SetFileInformationByHandle",
                linkPath,
                "Conflict: the reparse tag or link type does not match the persisted directory-junction fingerprint.");
        }

        if (!string.Equals(
                current.TargetPath,
                expectedFingerprint.NormalizedTargetPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FileLinkException(
                "SetFileInformationByHandle",
                linkPath,
                "Conflict: the directory-junction target does not match the persisted fingerprint.");
        }

        beforeJunctionDisposition?.Invoke();

        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        if (!NativeMethods.SetFileInformationByHandle(
                handle,
                NativeConstants.FileDispositionInfo,
                ref disposition,
                (uint)Marshal.SizeOf<FileDispositionInfo>()))
        {
            throw WindowsFileIdentityReader.CreateWin32Exception("SetFileInformationByHandle", linkPath);
        }

        return Task.CompletedTask;
    }

    private static void SetMountPointReparseData(SafeFileHandle handle, string linkPath, byte[] buffer)
    {
        var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            if (!NativeMethods.DeviceIoControl(
                    handle,
                    NativeConstants.FsctlSetReparsePoint,
                    pinnedBuffer.AddrOfPinnedObject(),
                    checked((uint)buffer.Length),
                    outputBuffer: null,
                    outputBufferSize: 0,
                    out _,
                    overlapped: 0))
            {
                throw WindowsFileIdentityReader.CreateWin32Exception("FSCTL_SET_REPARSE_POINT", linkPath);
            }
        }
        finally
        {
            pinnedBuffer.Free();
        }
    }

    private void TryRemoveEmptyOrdinaryDirectory(SafeFileHandle handle, string path)
    {
        try
        {
            var attributes = reparsePointReader.GetHandleAttributes(handle, path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            {
                return;
            }

            var disposition = new FileDispositionInfo { DeleteFile = 1 };
            _ = NativeMethods.SetFileInformationByHandle(
                handle,
                NativeConstants.FileDispositionInfo,
                ref disposition,
                (uint)Marshal.SizeOf<FileDispositionInfo>());
        }
        catch (FileLinkException)
        {
            // Preserve the original creation failure when rollback cannot safely inspect the placeholder.
        }
        catch (IOException)
        {
            // A concurrently populated or removed placeholder is deliberately preserved.
        }
        catch (UnauthorizedAccessException)
        {
            // A placeholder that cannot be inspected must not be removed.
        }
    }

    private static SafeFileHandle OpenDirectoryHandle(string path, uint desiredAccess)
    {
        var handle = NativeMethods.CreateFile(
            path,
            desiredAccess,
            NativeConstants.FileShareRead | NativeConstants.FileShareWrite | NativeConstants.FileShareDelete,
            securityAttributes: 0,
            NativeConstants.OpenExisting,
            NativeConstants.FileFlagOpenReparsePoint | NativeConstants.FileFlagBackupSemantics,
            templateFile: 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw WindowsFileIdentityReader.CreateWin32Exception("CreateFileW", path);
        }

        return handle;
    }

    private static SafeFileHandle? TryOpenDirectoryHandleForDelete(string path)
    {
        var handle = NativeMethods.CreateFile(
            path,
            NativeConstants.Delete,
            NativeConstants.FileShareRead | NativeConstants.FileShareWrite | NativeConstants.FileShareDelete,
            securityAttributes: 0,
            NativeConstants.OpenExisting,
            NativeConstants.FileFlagOpenReparsePoint | NativeConstants.FileFlagBackupSemantics,
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

        throw WindowsFileIdentityReader.CreateWin32Exception("CreateFileW", path);
    }
}
