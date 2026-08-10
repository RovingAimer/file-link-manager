using System.Buffers.Binary;
using System.Text;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Native;

namespace FileLinkManager.Infrastructure.Links;

public sealed record ReparsePointInfo(
    FileLinkType FileLinkType,
    uint Tag,
    string TargetPath,
    bool IsRelative)
{
    public bool IsSymbolicLink => Tag == NativeConstants.IoReparseTagSymbolicLink;

    public bool IsMountPoint => Tag == NativeConstants.IoReparseTagMountPoint;
}

public static class ReparsePointBuffer
{
    private const int HeaderLength = 8;
    private const int SymbolicLinkPayloadHeaderLength = 12;
    private const int MountPointPayloadHeaderLength = 8;

    public static byte[] CreateMountPointBuffer(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("A mount-point target path is required.", nameof(targetPath));
        }

        string normalizedTarget;
        try
        {
            normalizedTarget = NormalizeTarget(targetPath.Trim(), targetPath, isRelative: false);
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException("The mount-point target path must be absolute and valid.", nameof(targetPath), exception);
        }

        var substituteNameBytes = Encoding.Unicode.GetBytes(ToNtNamespacePath(normalizedTarget));
        var printNameBytes = Encoding.Unicode.GetBytes(normalizedTarget);
        var printNameOffset = checked(substituteNameBytes.Length + sizeof(char));
        var pathBufferLength = checked(printNameOffset + printNameBytes.Length + sizeof(char));
        var reparseDataLength = checked(MountPointPayloadHeaderLength + pathBufferLength);
        var totalLength = checked(HeaderLength + reparseDataLength);
        if (totalLength > NativeConstants.MaximumReparseDataBufferSize || reparseDataLength > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetPath),
                "The mount-point reparse buffer exceeds the maximum supported size.");
        }

        var buffer = new byte[totalLength];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, NativeConstants.IoReparseTagMountPoint);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), checked((ushort)reparseDataLength));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), checked((ushort)substituteNameBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), checked((ushort)printNameOffset));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14), checked((ushort)printNameBytes.Length));
        substituteNameBytes.CopyTo(buffer, HeaderLength + MountPointPayloadHeaderLength);
        printNameBytes.CopyTo(buffer, HeaderLength + MountPointPayloadHeaderLength + printNameOffset);
        return buffer;
    }

    public static ReparsePointInfo Parse(
        byte[] buffer,
        int outputByteCount,
        string linkPath,
        bool isDirectory)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);

        if (outputByteCount < 0 || outputByteCount > buffer.Length)
        {
            throw new InvalidDataException("The output byte count is outside the supplied buffer bounds.");
        }

        if (outputByteCount < HeaderLength)
        {
            throw new InvalidDataException("The reparse-point header is truncated.");
        }

        var data = buffer.AsSpan(0, outputByteCount);
        var tag = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var reparseDataLength = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        var dataEnd = checked(HeaderLength + reparseDataLength);
        if (dataEnd > outputByteCount)
        {
            throw new InvalidDataException("ReparseDataLength exceeds the returned output byte count.");
        }

        return tag switch
        {
            NativeConstants.IoReparseTagSymbolicLink => ParseSymbolicLink(data[..dataEnd], linkPath, isDirectory),
            NativeConstants.IoReparseTagMountPoint => ParseMountPoint(data[..dataEnd], linkPath, isDirectory),
            _ => throw new InvalidDataException($"Unsupported reparse tag 0x{tag:X8}.")
        };
    }

    private static ReparsePointInfo ParseSymbolicLink(
        ReadOnlySpan<byte> data,
        string linkPath,
        bool isDirectory)
    {
        var pathBufferOffset = HeaderLength + SymbolicLinkPayloadHeaderLength;
        if (data.Length < pathBufferOffset)
        {
            throw new InvalidDataException("The symbolic-link payload is truncated.");
        }

        var substituteNameOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        var substituteNameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        var printNameOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        var printNameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);

        if ((flags & ~NativeConstants.SymbolicLinkFlagRelative) != 0)
        {
            throw new InvalidDataException($"Unsupported symbolic-link flags 0x{flags:X8}.");
        }

        ValidateUtf16Range(data.Length, pathBufferOffset, substituteNameOffset, substituteNameLength, "SubstituteName");
        ValidateUtf16Range(data.Length, pathBufferOffset, printNameOffset, printNameLength, "PrintName");

        var substituteName = ReadUtf16(data, pathBufferOffset, substituteNameOffset, substituteNameLength);
        var isRelative = (flags & NativeConstants.SymbolicLinkFlagRelative) != 0;
        var targetPath = NormalizeTarget(substituteName, linkPath, isRelative);
        return new ReparsePointInfo(
            isDirectory ? FileLinkType.DirectorySymbolicLink : FileLinkType.FileSymbolicLink,
            NativeConstants.IoReparseTagSymbolicLink,
            targetPath,
            isRelative);
    }

    private static ReparsePointInfo ParseMountPoint(
        ReadOnlySpan<byte> data,
        string linkPath,
        bool isDirectory)
    {
        var pathBufferOffset = HeaderLength + MountPointPayloadHeaderLength;
        if (data.Length < pathBufferOffset)
        {
            throw new InvalidDataException("The mount-point payload is truncated.");
        }

        if (!isDirectory)
        {
            throw new InvalidDataException("A mount-point reparse point must be a directory.");
        }

        var substituteNameOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        var substituteNameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        var printNameOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        var printNameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);

        ValidateUtf16Range(data.Length, pathBufferOffset, substituteNameOffset, substituteNameLength, "SubstituteName");
        ValidateUtf16Range(data.Length, pathBufferOffset, printNameOffset, printNameLength, "PrintName");

        var substituteName = ReadUtf16(data, pathBufferOffset, substituteNameOffset, substituteNameLength);
        var targetPath = NormalizeTarget(substituteName, linkPath, isRelative: false);
        return new ReparsePointInfo(
            FileLinkType.DirectoryJunction,
            NativeConstants.IoReparseTagMountPoint,
            targetPath,
            IsRelative: false);
    }

    private static void ValidateUtf16Range(
        int dataLength,
        int pathBufferOffset,
        ushort nameOffset,
        ushort nameLength,
        string fieldName)
    {
        if ((nameOffset & 1) != 0 || (nameLength & 1) != 0)
        {
            throw new InvalidDataException($"{fieldName} offset and length must use even UTF-16 byte boundaries.");
        }

        var availablePathBytes = dataLength - pathBufferOffset;
        if (nameOffset > availablePathBytes || nameLength > availablePathBytes - nameOffset)
        {
            throw new InvalidDataException($"{fieldName} range is outside the reparse payload bounds.");
        }
    }

    private static string ReadUtf16(
        ReadOnlySpan<byte> data,
        int pathBufferOffset,
        ushort nameOffset,
        ushort nameLength) =>
        Encoding.Unicode.GetString(data.Slice(pathBufferOffset + nameOffset, nameLength));

    private static string NormalizeTarget(string substituteName, string linkPath, bool isRelative)
    {
        if (string.IsNullOrEmpty(substituteName))
        {
            throw new InvalidDataException("The reparse-point substitute name is empty.");
        }

        if (isRelative && (HasNamespacePrefix(substituteName) || Path.IsPathRooted(substituteName)))
        {
            throw new InvalidDataException(
                "A relative symbolic-link target must not be rooted, UNC, or namespace-prefixed.");
        }

        var normalizedTarget = RemoveNamespacePrefix(substituteName);
        try
        {
            if (isRelative)
            {
                if (Path.IsPathRooted(normalizedTarget))
                {
                    throw new InvalidDataException("A relative symbolic-link target must not be rooted.");
                }

                var normalizedLinkPath = Path.GetFullPath(RemoveNamespacePrefix(linkPath));
                var parentDirectory = Path.GetDirectoryName(normalizedLinkPath);
                if (string.IsNullOrEmpty(parentDirectory))
                {
                    throw new InvalidDataException("The relative symbolic-link target has no link parent directory.");
                }

                return Path.GetFullPath(normalizedTarget, parentDirectory);
            }

            if (!Path.IsPathFullyQualified(normalizedTarget))
            {
                throw new InvalidDataException("An absolute reparse-point target was expected.");
            }

            return Path.GetFullPath(normalizedTarget);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            throw new InvalidDataException("The reparse-point target path is invalid.", exception);
        }
    }

    private static bool HasNamespacePrefix(string path) =>
        path.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase);

    private static string RemoveNamespacePrefix(string path)
    {
        if (path.StartsWith(@"\??\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        if (path.StartsWith(@"\??\Volume{", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\?\" + path[4..];
        }

        if (path.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
        {
            return path[4..];
        }

        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        if (path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            ? path[4..]
            : path;
    }

    private static string ToNtNamespacePath(string absolutePath)
    {
        if (absolutePath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\??\" + absolutePath[4..];
        }

        if (absolutePath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\??\UNC\" + absolutePath[2..];
        }

        return @"\??\" + absolutePath;
    }
}
