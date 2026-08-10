using System.Buffers.Binary;
using System.Text;
using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Links;
using Microsoft.Win32.SafeHandles;

namespace FileLinkManager.Infrastructure.Tests.Links;

public sealed class ReparsePointReaderTests
{
    private const uint OpenReparsePoint = 0x00200000;
    private const uint BackupSemantics = 0x02000000;

    [Fact]
    public void Read_RealRelativeFileSymbolicLink_ReturnsLinkEntryMetadataAndAbsoluteTarget()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "file-link.txt");

        try
        {
            File.WriteAllText(sourcePath, "source");
            File.CreateSymbolicLink(linkPath, Path.GetFileName(sourcePath));

            var result = Read(linkPath);

            Assert.Equal(0xA000000Cu, result.Tag);
            Assert.Equal(FileLinkType.FileSymbolicLink, result.FileLinkType);
            Assert.True(result.IsSymbolicLink);
            Assert.False(result.IsMountPoint);
            Assert.True(result.IsRelative);
            Assert.Equal(sourcePath, result.TargetPath);
        }
        finally
        {
            DeleteFileIfPresent(linkPath);
            DeleteFileIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Read_RealDirectorySymbolicLink_OpensLinkEntryWithoutFollowingTarget()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source-directory");
        var linkPath = Path.Combine(directory, "directory-link");

        try
        {
            Directory.CreateDirectory(sourcePath);
            Directory.CreateSymbolicLink(linkPath, Path.GetFileName(sourcePath));

            var result = Read(linkPath);

            Assert.Equal(0xA000000Cu, result.Tag);
            Assert.Equal(FileLinkType.DirectorySymbolicLink, result.FileLinkType);
            Assert.Equal(sourcePath, result.TargetPath);
            Assert.True(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteDirectoryIfPresent(linkPath);
            DeleteDirectoryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(FileAttributes.Directory, FileLinkType.DirectorySymbolicLink)]
    [InlineData(FileAttributes.Normal, FileLinkType.FileSymbolicLink)]
    public void Read_UsesOpenedHandleMetadataToClassifyEntry(
        FileAttributes handleAttributes,
        FileLinkType expectedType)
    {
        var native = new FakeReparsePointNative(
            handleAttributes,
            CreateSymbolicLinkBuffer(@"\??\C:\source"));

        var result = new ReparsePointReader(native).Read(@"C:\links\link");

        Assert.Equal(expectedType, result.FileLinkType);
        Assert.Equal(OpenReparsePoint | BackupSemantics, native.OpenFlags);
    }

    [Fact]
    public void Read_WhenNativeByteCountExceedsAllocatedBuffer_ThrowsClearMalformedOutputError()
    {
        var native = new FakeReparsePointNative(
            FileAttributes.Normal,
            CreateSymbolicLinkBuffer(@"\??\C:\source"),
            bytesReturned: uint.MaxValue);

        var exception = Assert.Throws<FileLinkException>(
            () => new ReparsePointReader(native).Read(@"C:\links\link"));

        Assert.Equal("FSCTL_GET_REPARSE_POINT", exception.Operation);
        Assert.Contains("output byte count", exception.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static ReparsePointInfo Read(string linkPath) => new ReparsePointReader().Read(linkPath);

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"FileLinkManager.ReparseTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
    }

    private static byte[] CreateSymbolicLinkBuffer(string substituteName)
    {
        var substituteBytes = Encoding.Unicode.GetBytes(substituteName);
        var buffer = new byte[20 + substituteBytes.Length * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 0xA000000C);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), checked((ushort)(12 + substituteBytes.Length * 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), checked((ushort)substituteBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), checked((ushort)substituteBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14), checked((ushort)substituteBytes.Length));
        substituteBytes.CopyTo(buffer, 20);
        substituteBytes.CopyTo(buffer, 20 + substituteBytes.Length);
        return buffer;
    }

    private sealed class FakeReparsePointNative(
        FileAttributes handleAttributes,
        byte[] reparseData,
        uint? bytesReturned = null) : IReparsePointNative
    {
        public uint OpenFlags { get; private set; }

        public SafeFileHandle Open(string path, uint flags)
        {
            OpenFlags = flags;
            return new SafeFileHandle(new nint(1), ownsHandle: false);
        }

        public FileAttributes GetHandleAttributes(SafeFileHandle handle, string path) => handleAttributes;

        public uint GetReparsePoint(SafeFileHandle handle, byte[] buffer, string path)
        {
            reparseData.CopyTo(buffer, 0);
            return bytesReturned ?? (uint)reparseData.Length;
        }
    }
}
