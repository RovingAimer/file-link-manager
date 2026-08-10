namespace FileLinkManager.Infrastructure.Native;

internal static class NativeConstants
{
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint Delete = 0x00010000;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint FileShareDelete = 0x00000004;
    internal const uint OpenExisting = 3;
    internal const uint FileAttributeNormal = 0x00000080;
    internal const uint FileFlagOpenReparsePoint = 0x00200000;
    internal const uint FileFlagBackupSemantics = 0x02000000;
    internal const uint FsctlGetReparsePoint = 0x000900A8;
    internal const uint FsctlSetReparsePoint = 0x000900A4;
    internal const int MaximumReparseDataBufferSize = 16 * 1024;
    internal const uint IoReparseTagMountPoint = 0xA0000003;
    internal const uint IoReparseTagSymbolicLink = 0xA000000C;
    internal const uint SymbolicLinkFlagFile = 0x00000000;
    internal const uint SymbolicLinkFlagDirectory = 0x00000001;
    internal const uint SymbolicLinkFlagAllowUnprivilegedCreate = 0x00000002;
    internal const uint SymbolicLinkFlagRelative = 0x00000001;
    internal const int FileBasicInfo = 0;
    internal const int FileDispositionInfo = 4;
    internal const int FileIdInfo = 18;
    internal const uint DriveRemote = 4;
}
