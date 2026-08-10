using System.Runtime.InteropServices;
using FileLinkManager.Domain.Errors;
using FileLinkManager.Infrastructure.Native;
using Microsoft.Win32.SafeHandles;

namespace FileLinkManager.Infrastructure.Links;

public sealed class ReparsePointReader
{
    private readonly IReparsePointNative native;

    public ReparsePointReader()
        : this(new WindowsReparsePointNative())
    {
    }

    internal ReparsePointReader(IReparsePointNative native)
    {
        this.native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public ReparsePointInfo Read(string linkPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);

        using var handle = native.Open(
            linkPath,
            NativeConstants.FileFlagOpenReparsePoint | NativeConstants.FileFlagBackupSemantics);
        return Read(handle, linkPath);
    }

    internal ReparsePointInfo Read(SafeFileHandle handle, string linkPath)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);

        var attributes = native.GetHandleAttributes(handle, linkPath);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;

        var buffer = new byte[NativeConstants.MaximumReparseDataBufferSize];
        var bytesReturned = native.GetReparsePoint(handle, buffer, linkPath);
        if (bytesReturned > (uint)buffer.Length)
        {
            throw new FileLinkException(
                "FSCTL_GET_REPARSE_POINT",
                linkPath,
                "The native output byte count exceeds the allocated reparse-point buffer.");
        }

        try
        {
            return ReparsePointBuffer.Parse(buffer, (int)bytesReturned, linkPath, isDirectory);
        }
        catch (InvalidDataException exception)
        {
            throw new FileLinkException("FSCTL_GET_REPARSE_POINT", linkPath, exception.Message);
        }
    }

    internal FileAttributes GetHandleAttributes(SafeFileHandle handle, string linkPath)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        return native.GetHandleAttributes(handle, linkPath);
    }
}

internal interface IReparsePointNative
{
    SafeFileHandle Open(string path, uint flags);

    FileAttributes GetHandleAttributes(SafeFileHandle handle, string path);

    uint GetReparsePoint(SafeFileHandle handle, byte[] buffer, string path);
}

internal sealed class WindowsReparsePointNative : IReparsePointNative
{
    public SafeFileHandle Open(string path, uint flags)
    {
        var handle = NativeMethods.CreateFile(
            path,
            desiredAccess: 0,
            NativeConstants.FileShareRead | NativeConstants.FileShareWrite | NativeConstants.FileShareDelete,
            securityAttributes: 0,
            NativeConstants.OpenExisting,
            flags,
            templateFile: 0);

        if (handle.IsInvalid)
        {
            var exception = WindowsFileIdentityReader.CreateWin32Exception("CreateFileW", path);
            handle.Dispose();
            throw exception;
        }

        return handle;
    }

    public FileAttributes GetHandleAttributes(SafeFileHandle handle, string path)
    {
        if (!NativeMethods.GetFileBasicInformationByHandleEx(
                handle,
                NativeConstants.FileBasicInfo,
                out FileBasicInfo basicInfo,
                (uint)Marshal.SizeOf<FileBasicInfo>()))
        {
            throw WindowsFileIdentityReader.CreateWin32Exception("GetFileInformationByHandleEx", path);
        }

        return (FileAttributes)basicInfo.FileAttributes;
    }

    public uint GetReparsePoint(SafeFileHandle handle, byte[] buffer, string path)
    {
        if (!NativeMethods.DeviceIoControl(
                handle,
                NativeConstants.FsctlGetReparsePoint,
                inputBuffer: 0,
                inputBufferSize: 0,
                buffer,
                (uint)buffer.Length,
                out var bytesReturned,
                overlapped: 0))
        {
            throw WindowsFileIdentityReader.CreateWin32Exception("FSCTL_GET_REPARSE_POINT", path);
        }

        return bytesReturned;
    }
}
