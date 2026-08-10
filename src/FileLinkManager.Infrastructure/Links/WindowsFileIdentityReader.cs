using System.ComponentModel;
using System.Runtime.InteropServices;
using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Native;
using Microsoft.Win32.SafeHandles;

namespace FileLinkManager.Infrastructure.Links;

internal sealed class WindowsFileIdentityReader
{
    internal LinkFingerprint Read(string path)
    {
        using var handle = NativeMethods.CreateFile(
            path,
            NativeConstants.GenericRead,
            NativeConstants.FileShareRead | NativeConstants.FileShareWrite | NativeConstants.FileShareDelete,
            0,
            NativeConstants.OpenExisting,
            NativeConstants.FileAttributeNormal,
            0);

        if (handle.IsInvalid)
        {
            throw CreateWin32Exception("CreateFileW", path);
        }

        return Read(handle, path);
    }

    internal LinkFingerprint Read(SafeFileHandle handle, string path)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (handle.IsInvalid || handle.IsClosed)
        {
            throw new FileLinkException(
                "GetFileInformationByHandleEx",
                path,
                "The file handle is invalid or already closed.");
        }

        if (!NativeMethods.GetFileInformationByHandleEx(
                handle,
                NativeConstants.FileIdInfo,
                out var fileIdInfo,
                (uint)Marshal.SizeOf<FileIdInfo>()))
        {
            throw CreateWin32Exception("GetFileInformationByHandleEx", path);
        }

        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref fileIdInfo.FileId, 1));
        return new LinkFingerprint(null, null, fileIdInfo.VolumeSerialNumber, Convert.ToHexString(bytes));
    }

    internal static FileLinkException CreateWin32Exception(string operation, string path)
    {
        var errorCode = Marshal.GetLastWin32Error();
        var reason = new Win32Exception(errorCode).Message;
        return new FileLinkException(operation, path, $"Win32 error {errorCode}: {reason}");
    }
}
