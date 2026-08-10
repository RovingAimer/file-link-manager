using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Paths;

namespace FileLinkManager.Infrastructure.Tests.Paths;

public sealed class WindowsPathServiceTests
{
    [Theory]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\Temp\Folder\", @"C:\Temp\Folder")]
    [InlineData(@"\\server\share\", @"\\server\share\")]
    [InlineData(@"\\server\share\folder\", @"\\server\share\folder")]
    [InlineData(@"\\?\C:\Temp\Folder\", @"C:\Temp\Folder")]
    [InlineData(@"\\?\UNC\server\share\folder\", @"\\server\share\folder")]
    public void Normalize_ReturnsDisplayFriendlyAbsolutePath(string input, string expected)
    {
        var result = new WindowsPathService().Normalize(input);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Path!.DisplayPath);
        Assert.Equal(expected, result.Path.ComparisonPath);
    }

    [Fact]
    public void Normalize_PathCaseVariants_ProduceCaseInsensitiveEquivalentComparisonPaths()
    {
        var service = new WindowsPathService();
        var first = service.Normalize(@"C:\Data\File.txt").Path!;
        var second = service.Normalize(@"c:\data\file.txt").Path!;

        Assert.Equal(first.ComparisonPath, second.ComparisonPath, ignoreCase: true);
    }

    [Fact]
    public void Normalize_WhenPathUsesExtendedVolumeGuid_PreservesItsAbsolutePrefix()
    {
        const string volumeGuidPath = @"\\?\Volume{01234567-89AB-CDEF-0123-456789ABCDEF}\folder\";

        var result = new WindowsPathService().Normalize(volumeGuidPath);

        Assert.True(result.IsValid);
        Assert.Equal(volumeGuidPath.TrimEnd(Path.DirectorySeparatorChar), result.Path!.DisplayPath);
        Assert.StartsWith(@"\\?\Volume{", result.Path.ComparisonPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenSourceIsMissing_ReturnsTypedError()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var result = new WindowsPathService().Validate(new LinkRequest(
                Path.Combine(directory, "missing.txt"),
                Path.Combine(directory, "link.txt"),
                FileLinkType.FileHardLink));

            Assert.Contains(result.Errors, error => error.Code == PathValidationErrorCode.SourceDoesNotExist);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Validate_WhenLinkAlreadyExists_ReturnsTypedError()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.txt");
            var link = Path.Combine(directory, "link.txt");
            File.WriteAllText(source, "source");
            File.WriteAllText(link, "occupied");

            var result = new WindowsPathService().Validate(new LinkRequest(source, link, FileLinkType.FileHardLink));

            Assert.Contains(result.Errors, error => error.Code == PathValidationErrorCode.LinkPathAlreadyExists);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Validate_WhenLinkPathIsDriveRoot_ReturnsOccupiedErrorWithoutMissingParentError()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.txt");
            File.WriteAllText(source, "source");
            var root = Path.GetPathRoot(source)!;

            var result = new WindowsPathService().Validate(new LinkRequest(source, root, FileLinkType.FileHardLink));

            Assert.Contains(result.Errors, error => error.Code == PathValidationErrorCode.LinkPathAlreadyExists);
            Assert.DoesNotContain(result.Errors, error => error.Code == PathValidationErrorCode.LinkParentDoesNotExist);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Validate_WhenLinkParentIsMissing_ReturnsTypedError()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.txt");
            File.WriteAllText(source, "source");

            var result = new WindowsPathService().Validate(new LinkRequest(
                source,
                Path.Combine(directory, "missing", "link.txt"),
                FileLinkType.FileHardLink));

            Assert.Contains(result.Errors, error => error.Code == PathValidationErrorCode.LinkParentDoesNotExist);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Validate_WhenFileLinkRequiresDirectorySource_ReturnsTypedError()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var result = new WindowsPathService().Validate(new LinkRequest(
                directory,
                Path.Combine(directory, "link.txt"),
                FileLinkType.FileHardLink));

            Assert.Contains(result.Errors, error => error.Code == PathValidationErrorCode.SourceTypeMismatch);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Validate_WhenHardLinkPathsHaveDifferentVolumeIdentities_ReturnsTypedError()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.txt");
            var link = Path.Combine(directory, "link.txt");
            File.WriteAllText(source, "source");
            var service = new WindowsPathService(path => path == source
                ? new VolumeIdentity("source-volume", false)
                : new VolumeIdentity("link-volume", false));

            var result = service.Validate(new LinkRequest(source, link, FileLinkType.FileHardLink));

            Assert.Contains(result.Errors, error => error.Code == PathValidationErrorCode.DifferentVolume);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Validate_WhenHardLinkPathsAreOnDifferentLocalVolumes_ReturnsTypedError()
    {
        var volumes = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Take(2)
            .ToArray();
        if (volumes.Length < 2)
        {
            return;
        }

        var sourceDirectory = CreateTemporaryDirectory(volumes[0].RootDirectory.FullName);
        var linkParent = CreateTemporaryDirectory(volumes[1].RootDirectory.FullName);
        try
        {
            var source = Path.Combine(sourceDirectory, "source.txt");
            File.WriteAllText(source, "source");

            var result = new WindowsPathService().Validate(new LinkRequest(
                source,
                Path.Combine(linkParent, "link.txt"),
                FileLinkType.FileHardLink));

            Assert.Contains(result.Errors, error => error.Code == PathValidationErrorCode.DifferentVolume);
        }
        finally
        {
            Directory.Delete(sourceDirectory, true);
            Directory.Delete(linkParent, true);
        }
    }

    [Fact]
    public void Validate_WhenJunctionUsesMappedNetworkPath_ReturnsTypedError()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source");
            var link = Path.Combine(directory, "link");
            Directory.CreateDirectory(source);
            var service = new WindowsPathService(path => path == source
                ? new VolumeIdentity("network-volume", true)
                : new VolumeIdentity("local-volume", false));

            var result = service.Validate(new LinkRequest(source, link, FileLinkType.DirectoryJunction));

            Assert.Contains(result.Errors, error => error.Code == PathValidationErrorCode.NetworkPathNotSupported);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Validate_WhenJunctionUsesUncPath_ReturnsTypedError()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var result = new WindowsPathService().Validate(new LinkRequest(
                @"\\server\share\source",
                Path.Combine(directory, "link"),
                FileLinkType.DirectoryJunction));

            Assert.Contains(result.Errors, error => error.Code == PathValidationErrorCode.NetworkPathNotSupported);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Validate_WhenJunctionLinkUsesExtendedVolumeGuid_DoesNotClassifyItAsNetworkPath()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source");
            Directory.CreateDirectory(source);

            var result = new WindowsPathService(_ => new VolumeIdentity("local-volume", false)).Validate(new LinkRequest(
                source,
                @"\\?\Volume{01234567-89AB-CDEF-0123-456789ABCDEF}\link",
                FileLinkType.DirectoryJunction));

            Assert.DoesNotContain(result.Errors, error => error.Code == PathValidationErrorCode.NetworkPathNotSupported);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTemporaryDirectory(string? root = null)
    {
        var directory = Path.Combine(root ?? Path.GetTempPath(), $"FileLinkManager.PathTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
