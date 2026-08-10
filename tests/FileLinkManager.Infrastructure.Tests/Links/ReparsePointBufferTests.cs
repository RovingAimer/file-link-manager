using System.Buffers.Binary;
using System.Text;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Links;

namespace FileLinkManager.Infrastructure.Tests.Links;

public sealed class ReparsePointBufferTests
{
    private const uint SymbolicLinkTag = 0xA000000C;
    private const uint MountPointTag = 0xA0000003;

    [Theory]
    [InlineData(@"\??\C:\source\file.txt", @"C:\source\file.txt")]
    [InlineData(@"\??\UNC\server\share\file.txt", @"\\server\share\file.txt")]
    [InlineData(@"\\?\UNC\server\share\file.txt", @"\\server\share\file.txt")]
    [InlineData(@"\\server\share\file.txt", @"\\server\share\file.txt")]
    [InlineData(@"\\?\C:\source\file.txt", @"C:\source\file.txt")]
    public void Parse_SymbolicLinkSubstituteName_NormalizesComparableAbsoluteTarget(
        string substituteName,
        string expectedTarget)
    {
        var buffer = CreateSymbolicLinkBuffer(substituteName);

        var result = Parse(buffer, buffer.Length, @"C:\links\link.txt", isDirectory: false);

        Assert.Equal(SymbolicLinkTag, result.Tag);
        Assert.Equal(FileLinkType.FileSymbolicLink, result.FileLinkType);
        Assert.True(result.IsSymbolicLink);
        Assert.False(result.IsMountPoint);
        Assert.False(result.IsRelative);
        Assert.Equal(expectedTarget, result.TargetPath);
    }

    [Fact]
    public void Parse_RelativeSymbolicLink_ResolvesAgainstNormalizedLinkParent()
    {
        var buffer = CreateSymbolicLinkBuffer(@"..\source\file.txt", isRelative: true);

        var result = Parse(buffer, buffer.Length, @"\\?\C:\links\nested\link.txt", isDirectory: false);

        Assert.True(result.IsRelative);
        Assert.Equal(@"C:\links\source\file.txt", result.TargetPath);
    }

    [Theory]
    [InlineData(0x00000002u)]
    [InlineData(0x80000001u)]
    public void Parse_WhenSymbolicLinkFlagsContainUnsupportedBits_RejectsFlags(uint flags)
    {
        var buffer = CreateSymbolicLinkBuffer(@"..\source\file.txt", isRelative: true);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), flags);

        var exception = Assert.Throws<InvalidDataException>(
            () => Parse(buffer, buffer.Length, @"C:\links\nested\link.txt", isDirectory: false));

        Assert.Contains("flags", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\source\file.txt")]
    [InlineData(@"\source\file.txt")]
    [InlineData(@"\\server\share\file.txt")]
    public void Parse_WhenRelativeSymbolicLinkTargetIsRooted_RejectsInconsistentMetadata(string substituteName)
    {
        var buffer = CreateSymbolicLinkBuffer(substituteName, isRelative: true);

        var exception = Assert.Throws<InvalidDataException>(
            () => Parse(buffer, buffer.Length, @"C:\links\nested\link.txt", isDirectory: false));

        Assert.Contains("relative", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhenRelativeSymbolicLinkTargetUsesNtNamespace_RejectsBeforePrefixRemoval()
    {
        var buffer = CreateSymbolicLinkBuffer(
            @"\??\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\secret.txt",
            isRelative: true);

        var exception = Assert.Throws<InvalidDataException>(
            () => Parse(buffer, buffer.Length, @"C:\links\nested\link.txt", isDirectory: false));

        Assert.Contains("relative", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("namespace", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_DirectorySymbolicLink_ReturnsDirectorySymbolicLinkType()
    {
        var buffer = CreateSymbolicLinkBuffer(@"\??\C:\source");

        var result = Parse(buffer, buffer.Length, @"C:\links\link", isDirectory: true);

        Assert.Equal(FileLinkType.DirectorySymbolicLink, result.FileLinkType);
    }

    [Fact]
    public void Parse_ExtendedVolumeGuidTarget_PreservesLocalExtendedVolumePath()
    {
        const string target = @"\\?\Volume{01234567-89AB-CDEF-0123-456789ABCDEF}\folder";
        var buffer = CreateSymbolicLinkBuffer(@"\??\Volume{01234567-89AB-CDEF-0123-456789ABCDEF}\folder");

        var result = Parse(buffer, buffer.Length, @"C:\links\link", isDirectory: true);

        Assert.Equal(target, result.TargetPath);
        Assert.StartsWith(@"\\?\Volume{", result.TargetPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MountPoint_ReturnsJunctionAndNormalizedTarget()
    {
        var buffer = CreateMountPointBuffer(@"\??\C:\source");

        var result = Parse(buffer, buffer.Length, @"C:\links\junction", isDirectory: true);

        Assert.Equal(MountPointTag, result.Tag);
        Assert.Equal(FileLinkType.DirectoryJunction, result.FileLinkType);
        Assert.False(result.IsSymbolicLink);
        Assert.True(result.IsMountPoint);
        Assert.False(result.IsRelative);
        Assert.Equal(@"C:\source", result.TargetPath);
    }

    [Fact]
    public void CreateMountPointBuffer_UsesMountPointLayoutAndCanonicalNtTarget()
    {
        var buffer = ReparsePointBuffer.CreateMountPointBuffer(@"\\?\C:\source");

        Assert.Equal(MountPointTag, BinaryPrimitives.ReadUInt32LittleEndian(buffer));
        Assert.Equal(buffer.Length - 8, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(6)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(8)));

        var substituteNameLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(10));
        var printNameOffset = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(12));
        var printNameLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(14));
        var substituteName = Encoding.Unicode.GetString(buffer, 16, substituteNameLength);
        var printName = Encoding.Unicode.GetString(buffer, 16 + printNameOffset, printNameLength);

        Assert.Equal(@"\??\C:\source", substituteName);
        Assert.Equal(@"C:\source", printName);
        Assert.Equal(substituteNameLength + sizeof(char), printNameOffset);
        Assert.Equal(Encoding.Unicode.GetByteCount(@"C:\source"), printNameLength);
        Assert.Equal((byte)0, buffer[16 + substituteNameLength]);
        Assert.Equal((byte)0, buffer[16 + substituteNameLength + 1]);
        Assert.Equal((byte)0, buffer[16 + printNameOffset + printNameLength]);
        Assert.Equal((byte)0, buffer[16 + printNameOffset + printNameLength + 1]);
    }

    [Fact]
    public void CreateMountPointBuffer_ExtendedVolumeGuidTarget_UsesNtNamespaceAndRoundTrips()
    {
        const string target = @"\\?\Volume{01234567-89AB-CDEF-0123-456789ABCDEF}\folder";

        var buffer = ReparsePointBuffer.CreateMountPointBuffer(target);
        var parsed = Parse(buffer, buffer.Length, @"C:\links\junction", isDirectory: true);

        Assert.Equal(@"\??\Volume{01234567-89AB-CDEF-0123-456789ABCDEF}\folder", Encoding.Unicode.GetString(
            buffer,
            16,
            BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(10))));
        Assert.Equal(target, parsed.TargetPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\target")]
    public void CreateMountPointBuffer_WhenTargetIsEmptyOrRelative_RejectsInvalidTarget(string target)
    {
        var exception = Assert.Throws<ArgumentException>(() => ReparsePointBuffer.CreateMountPointBuffer(target));

        Assert.Contains("target", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateMountPointBuffer_WhenPayloadWouldExceedMaximumReparseSize_RejectsOversizedInput()
    {
        var target = @"C:\" + new string('a', 9000);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => ReparsePointBuffer.CreateMountPointBuffer(target));

        Assert.Contains("maximum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhenOutputByteCountIsShorterThanHeader_RejectsTruncation()
    {
        var buffer = CreateSymbolicLinkBuffer(@"\??\C:\source");

        var exception = Assert.Throws<InvalidDataException>(
            () => Parse(buffer, 7, @"C:\links\link", isDirectory: false));

        Assert.Contains("header", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhenOutputByteCountExceedsBuffer_RejectsTruncation()
    {
        var buffer = CreateSymbolicLinkBuffer(@"\??\C:\source");

        var exception = Assert.Throws<InvalidDataException>(
            () => Parse(buffer, buffer.Length + 1, @"C:\links\link", isDirectory: false));

        Assert.Contains("output byte count", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhenReparseDataLengthExceedsOutput_RejectsTruncation()
    {
        var buffer = CreateSymbolicLinkBuffer(@"\??\C:\source");
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), ushort.MaxValue);

        var exception = Assert.Throws<InvalidDataException>(
            () => Parse(buffer, buffer.Length, @"C:\links\link", isDirectory: false));

        Assert.Contains("ReparseDataLength", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenTagSpecificPayloadIsTruncated_RejectsPayload()
    {
        var buffer = CreateSymbolicLinkBuffer(@"\??\C:\source");
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), 0);

        var exception = Assert.Throws<InvalidDataException>(
            () => Parse(buffer, 8, @"C:\links\link", isDirectory: false));

        Assert.Contains("symbolic-link payload", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    public void Parse_WhenSubstituteNameOffsetOrLengthIsOdd_RejectsUtf16Field(int fieldOffset)
    {
        var buffer = CreateSymbolicLinkBuffer(@"\??\C:\source");
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(fieldOffset), 1);

        var exception = Assert.Throws<InvalidDataException>(
            () => Parse(buffer, buffer.Length, @"C:\links\link", isDirectory: false));

        Assert.Contains("UTF-16", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhenSubstituteNameRangeIsOutsidePayload_RejectsBounds()
    {
        var buffer = CreateMountPointBuffer(@"\??\C:\source");
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), ushort.MaxValue - 1);

        var exception = Assert.Throws<InvalidDataException>(
            () => Parse(buffer, buffer.Length, @"C:\links\junction", isDirectory: true));

        Assert.Contains("bounds", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhenTagIsUnknown_RejectsUnsupportedTag()
    {
        var buffer = CreateSymbolicLinkBuffer(@"\??\C:\source");
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 0xDEADBEEF);

        var exception = Assert.Throws<InvalidDataException>(
            () => Parse(buffer, buffer.Length, @"C:\links\link", isDirectory: false));

        Assert.Contains("0xDEADBEEF", exception.Message, StringComparison.Ordinal);
    }

    private static ReparsePointInfo Parse(byte[] buffer, int outputByteCount, string linkPath, bool isDirectory) =>
        ReparsePointBuffer.Parse(buffer, outputByteCount, linkPath, isDirectory);

    private static byte[] CreateSymbolicLinkBuffer(string substituteName, bool isRelative = false) =>
        CreateBuffer(SymbolicLinkTag, substituteName, fixedPayloadLength: 12, isRelative);

    private static byte[] CreateMountPointBuffer(string substituteName) =>
        CreateBuffer(MountPointTag, substituteName, fixedPayloadLength: 8, isRelative: false);

    private static byte[] CreateBuffer(uint tag, string substituteName, int fixedPayloadLength, bool isRelative)
    {
        var substituteBytes = Encoding.Unicode.GetBytes(substituteName);
        var printBytes = Encoding.Unicode.GetBytes(substituteName);
        var pathBytes = substituteBytes.Concat(printBytes).ToArray();
        var buffer = new byte[8 + fixedPayloadLength + pathBytes.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(buffer, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), checked((ushort)(fixedPayloadLength + pathBytes.Length)));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), checked((ushort)substituteBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), checked((ushort)substituteBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14), checked((ushort)printBytes.Length));
        if (tag == SymbolicLinkTag)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), isRelative ? 1u : 0u);
        }

        pathBytes.CopyTo(buffer, 8 + fixedPayloadLength);
        return buffer;
    }
}
