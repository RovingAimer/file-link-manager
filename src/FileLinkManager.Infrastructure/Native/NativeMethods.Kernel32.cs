using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FileLinkManager.Infrastructure.Native;

internal static class NativeMethods
{
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateHardLink(
        string fileName,
        string existingFileName,
        nint securityAttributes);

    [DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool CreateSymbolicLink(
        string symbolicLinkFileName,
        string targetFileName,
        uint flags);

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateDirectory(string pathName, nint securityAttributes);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        nint inputBuffer,
        uint inputBufferSize,
        [Out] byte[]? outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileIdInfo fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileBasicInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileBasicInfo fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        ref FileDispositionInfo fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "DeleteFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteFile(string fileName);

    [DllImport("kernel32.dll", EntryPoint = "RemoveDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveDirectory(string pathName);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumePathName(string fileName, StringBuilder volumePathName, uint bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        uint fileSystemNameSize);

    [DllImport("kernel32.dll", EntryPoint = "GetDriveTypeW", CharSet = CharSet.Unicode)]
    internal static extern uint GetDriveType(string rootPathName);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FileIdInfo
{
    internal ulong VolumeSerialNumber;
    internal FileId128 FileId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FileBasicInfo
{
    internal long CreationTime;
    internal long LastAccessTime;
    internal long LastWriteTime;
    internal long ChangeTime;
    internal uint FileAttributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FileDispositionInfo
{
    internal byte DeleteFile;
}

[InlineArray(16)]
internal struct FileId128
{
    private byte element0;
}
