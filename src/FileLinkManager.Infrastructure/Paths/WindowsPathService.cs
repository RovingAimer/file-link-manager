using System.Text;
using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Links;
using FileLinkManager.Infrastructure.Native;

namespace FileLinkManager.Infrastructure.Paths;

public sealed record VolumeIdentity(string Value, bool IsNetwork);

public sealed class WindowsPathService
{
    private readonly Func<string, VolumeIdentity> getVolumeIdentity;

    public WindowsPathService()
        : this(new WindowsVolumeIdentityProvider().GetVolumeIdentity)
    {
    }

    public WindowsPathService(Func<string, VolumeIdentity> getVolumeIdentity)
    {
        this.getVolumeIdentity = getVolumeIdentity ?? throw new ArgumentNullException(nameof(getVolumeIdentity));
    }

    internal PathNormalizationResult Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new PathNormalizationResult(null, new PathValidationError(
                PathValidationErrorCode.InvalidSourcePath,
                "Enter a valid absolute path."));
        }

        try
        {
            var absolutePath = Path.GetFullPath(RemoveLongPathPrefix(input.Trim()));
            var displayPath = TrimTrailingSeparatorsExceptRoot(absolutePath);
            return new PathNormalizationResult(new NormalizedPath(displayPath, displayPath), null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return new PathNormalizationResult(null, new PathValidationError(
                PathValidationErrorCode.InvalidSourcePath,
                "Enter a valid absolute path."));
        }
    }

    internal PathValidationResult Validate(LinkRequest request)
    {
        if (request is null)
        {
            return new PathValidationResult(null, null, new[]
            {
                new PathValidationError(PathValidationErrorCode.RequestRequired, "A link request is required.")
            });
        }

        var errors = new List<PathValidationError>();
        var sourceResult = Normalize(request.SourcePath);
        var linkResult = Normalize(request.LinkPath);
        var sourcePath = sourceResult.Path;
        var linkPath = linkResult.Path;

        AddNormalizationError(errors, sourceResult.Error, PathValidationErrorCode.InvalidSourcePath, "Enter a valid source path.");
        AddNormalizationError(errors, linkResult.Error, PathValidationErrorCode.InvalidLinkPath, "Enter a valid link path.");

        var sourceIsFile = sourcePath is not null && File.Exists(sourcePath.DisplayPath);
        var sourceIsDirectory = sourcePath is not null && Directory.Exists(sourcePath.DisplayPath);
        if (sourcePath is not null && !sourceIsFile && !sourceIsDirectory)
        {
            errors.Add(new PathValidationError(PathValidationErrorCode.SourceDoesNotExist, "The source path does not exist."));
        }

        var linkExists = linkPath is not null && (File.Exists(linkPath.DisplayPath) || Directory.Exists(linkPath.DisplayPath));
        if (linkExists)
        {
            errors.Add(new PathValidationError(PathValidationErrorCode.LinkPathAlreadyExists, "The link path already exists."));
        }

        var linkParent = linkPath is null ? null : Path.GetDirectoryName(linkPath.DisplayPath) ?? linkPath.DisplayPath;
        var linkParentExists = !string.IsNullOrEmpty(linkParent) && Directory.Exists(linkParent);
        if (linkPath is not null && !linkParentExists)
        {
            errors.Add(new PathValidationError(PathValidationErrorCode.LinkParentDoesNotExist, "The link path parent directory does not exist."));
        }

        if (sourcePath is not null && (sourceIsFile || sourceIsDirectory))
        {
            var requiresFile = request.Type is FileLinkType.FileHardLink or FileLinkType.FileSymbolicLink;
            if ((requiresFile && !sourceIsFile) || (!requiresFile && !sourceIsDirectory))
            {
                errors.Add(new PathValidationError(
                    PathValidationErrorCode.SourceTypeMismatch,
                    requiresFile
                        ? "The selected link type requires a file source."
                        : "The selected link type requires a directory source."));
            }
        }

        if (request.Type == FileLinkType.DirectoryJunction &&
            ((sourcePath is not null && IsUncPath(sourcePath.ComparisonPath)) ||
             (linkPath is not null && IsUncPath(linkPath.ComparisonPath))))
        {
            errors.Add(new PathValidationError(
                PathValidationErrorCode.NetworkPathNotSupported,
                "Directory junctions require local paths; network paths are not supported."));
        }

        if (sourcePath is not null && linkParentExists && (sourceIsFile || sourceIsDirectory))
        {
            ValidateVolumeRules(request.Type, sourcePath.DisplayPath, linkParent!, errors);
        }

        return new PathValidationResult(sourcePath, linkPath, errors);
    }

    private void ValidateVolumeRules(
        FileLinkType type,
        string sourcePath,
        string linkParent,
        ICollection<PathValidationError> errors)
    {
        if (type is not FileLinkType.FileHardLink and not FileLinkType.DirectoryJunction)
        {
            return;
        }

        try
        {
            var sourceVolume = getVolumeIdentity(sourcePath);
            var linkVolume = getVolumeIdentity(linkParent);

            if (type == FileLinkType.FileHardLink &&
                !string.Equals(sourceVolume.Value, linkVolume.Value, StringComparison.Ordinal))
            {
                errors.Add(new PathValidationError(
                    PathValidationErrorCode.DifferentVolume,
                    "File hard links must be created on the same volume."));
            }

            if (type == FileLinkType.DirectoryJunction && (sourceVolume.IsNetwork || linkVolume.IsNetwork))
            {
                errors.Add(new PathValidationError(
                    PathValidationErrorCode.NetworkPathNotSupported,
                    "Directory junctions require local paths; mapped network paths are not supported."));
            }
        }
        catch (Exception exception) when (exception is FileLinkException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            errors.Add(new PathValidationError(
                PathValidationErrorCode.VolumeIdentityUnavailable,
                "The source and link locations could not be verified as Windows volumes."));
        }
    }

    private static void AddNormalizationError(
        ICollection<PathValidationError> errors,
        PathValidationError? error,
        PathValidationErrorCode code,
        string message)
    {
        if (error is not null)
        {
            errors.Add(new PathValidationError(code, message));
        }
    }

    private static string RemoveLongPathPrefix(string path)
    {
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

    private static string TrimTrailingSeparatorsExceptRoot(string path)
    {
        if (IsUncPath(path))
        {
            var uncPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segments = uncPath[2..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Length == 2 && segments.All(segment => !string.IsNullOrEmpty(segment)))
            {
                return uncPath + Path.DirectorySeparatorChar;
            }
        }

        var root = Path.GetPathRoot(path);
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !string.IsNullOrEmpty(root) && trimmed.Length < root.Length
            ? root
            : trimmed;
    }

    private static bool IsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) &&
        !path.StartsWith(@"\\?\", StringComparison.Ordinal);
}

internal sealed class WindowsVolumeIdentityProvider
{
    internal VolumeIdentity GetVolumeIdentity(string existingPath)
    {
        var volumePath = GetVolumePath(existingPath);
        if (!NativeMethods.GetVolumeInformation(
                volumePath,
                null,
                0,
                out var serialNumber,
                out _,
                out _,
                null,
                0))
        {
            throw WindowsFileIdentityReader.CreateWin32Exception("GetVolumeInformationW", existingPath);
        }

        return new VolumeIdentity(
            serialNumber.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
            NativeMethods.GetDriveType(volumePath) == NativeConstants.DriveRemote);
    }

    private static string GetVolumePath(string existingPath)
    {
        var volumePath = new StringBuilder(260);
        if (!NativeMethods.GetVolumePathName(existingPath, volumePath, (uint)volumePath.Capacity))
        {
            throw WindowsFileIdentityReader.CreateWin32Exception("GetVolumePathNameW", existingPath);
        }

        return volumePath.ToString();
    }
}
