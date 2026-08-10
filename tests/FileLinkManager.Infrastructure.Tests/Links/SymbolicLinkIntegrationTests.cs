using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Links;

namespace FileLinkManager.Infrastructure.Tests.Links;

public sealed class SymbolicLinkIntegrationTests
{
    private const uint SymbolicLinkTag = 0xA000000C;

    [Fact]
    public async Task CreateAsync_WithFileSourceAndMissingLink_CreatesVerifiedFileSymbolicLink()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "original");
            var request = new LinkRequest(sourcePath, linkPath, FileLinkType.FileSymbolicLink);
            var service = new WindowsFileLinkService();

            Assert.True(service.Validate(request).IsValid);

            var created = await service.CreateAsync(request, CancellationToken.None);

            Assert.Equal(LinkStatus.Valid, created.Status);
            Assert.Equal(FileLinkType.FileSymbolicLink, created.Type);
            Assert.NotNull(created.Fingerprint);
            Assert.Equal(SymbolicLinkTag, created.Fingerprint.ReparseTag);
            Assert.Equal(sourcePath, created.Fingerprint.NormalizedTargetPath);
            Assert.Equal("original", await File.ReadAllTextAsync(linkPath));

            await File.WriteAllTextAsync(linkPath, "changed through link");

            Assert.Equal("changed through link", await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            DeleteFileEntryIfPresent(linkPath);
            DeleteFileEntryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task CreateAsync_WithRelativeSourceAndLinkPaths_UsesCanonicalPathsForCreationAndVerification()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "relative-source.txt");
        var linkPath = Path.Combine(directory, "relative-link.txt");
        var relativeSourcePath = Path.GetRelativePath(Environment.CurrentDirectory, sourcePath);
        var relativeLinkPath = Path.GetRelativePath(Environment.CurrentDirectory, linkPath);

        try
        {
            await File.WriteAllTextAsync(sourcePath, "relative source");
            Assert.False(Path.IsPathFullyQualified(relativeSourcePath));
            Assert.False(Path.IsPathFullyQualified(relativeLinkPath));

            var created = await new WindowsFileLinkService().CreateAsync(
                new LinkRequest(relativeSourcePath, relativeLinkPath, FileLinkType.FileSymbolicLink),
                CancellationToken.None);

            Assert.Equal(linkPath, created.LinkPath);
            Assert.True(File.Exists(linkPath));
            Assert.Equal("relative source", await File.ReadAllTextAsync(linkPath));
            Assert.Equal(sourcePath, created.Fingerprint?.NormalizedTargetPath);

            var reparsePoint = new ReparsePointReader().Read(created.LinkPath);
            Assert.Equal(FileLinkType.FileSymbolicLink, reparsePoint.FileLinkType);
            Assert.Equal(sourcePath, reparsePoint.TargetPath);
        }
        finally
        {
            DeleteFileEntryIfPresent(linkPath);
            DeleteFileEntryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task CreateAsync_WithRelativePaths_UsesCanonicalRecordAndDeletesOriginalAfterWorkingDirectoryChange()
    {
        var directory = CreateTemporaryDirectory();
        var createDirectory = Path.Combine(directory, "create");
        var deleteDirectory = Path.Combine(directory, "delete");
        var sourcePath = Path.Combine(createDirectory, "source.txt");
        var linkPath = Path.Combine(createDirectory, "link.txt");
        var replacementPath = Path.Combine(deleteDirectory, "link.txt");
        var originalWorkingDirectory = Environment.CurrentDirectory;

        try
        {
            Directory.CreateDirectory(createDirectory);
            Directory.CreateDirectory(deleteDirectory);
            await File.WriteAllTextAsync(sourcePath, "source");

            Environment.CurrentDirectory = createDirectory;
            var request = new LinkRequest("source.txt", "link.txt", FileLinkType.FileSymbolicLink);
            var created = await new WindowsFileLinkService().CreateAsync(request, CancellationToken.None);
            var record = new LinkRecord(
                request with
                {
                    SourcePath = created.Fingerprint!.NormalizedTargetPath!,
                    LinkPath = created.Fingerprint.NormalizedLinkPath!
                },
                created.Fingerprint);

            Assert.Equal(linkPath, created.LinkPath);
            Assert.Equal(sourcePath, created.Fingerprint?.NormalizedTargetPath);
            Assert.Equal(linkPath, created.Fingerprint?.NormalizedLinkPath);
            Assert.Equal(sourcePath, record.Request.SourcePath);
            Assert.Equal(linkPath, record.Request.LinkPath);

            Environment.CurrentDirectory = deleteDirectory;
            await File.WriteAllTextAsync(replacementPath, "replacement");

            await new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None);

            Assert.False(Path.Exists(linkPath));
            Assert.Equal("source", await File.ReadAllTextAsync(sourcePath));
            Assert.Equal("replacement", await File.ReadAllTextAsync(replacementPath));
        }
        finally
        {
            Environment.CurrentDirectory = originalWorkingDirectory;
            DeleteFileEntryIfPresent(replacementPath);
            DeleteFileEntryIfPresent(linkPath);
            DeleteFileEntryIfPresent(sourcePath);
            Directory.Delete(deleteDirectory);
            Directory.Delete(createDirectory);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task InspectAsync_FileSymbolicLink_ReturnsReaderTagTypeAndNormalizedTarget()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "source");
            File.CreateSymbolicLink(linkPath, sourcePath);

            var inspection = await new WindowsFileLinkService().InspectAsync(linkPath, CancellationToken.None);

            Assert.Equal(LinkStatus.Valid, inspection.Status);
            Assert.Equal(FileLinkType.FileSymbolicLink, inspection.Type);
            Assert.NotNull(inspection.Fingerprint);
            Assert.Equal(SymbolicLinkTag, inspection.Fingerprint.ReparseTag);
            Assert.Equal(sourcePath, inspection.Fingerprint.NormalizedTargetPath);
        }
        finally
        {
            DeleteFileEntryIfPresent(linkPath);
            DeleteFileEntryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_FileSymbolicLink_DeletesOnlyLinkAndPreservesSourceContent()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "preserve source");
            var request = new LinkRequest(sourcePath, linkPath, FileLinkType.FileSymbolicLink);
            var service = new WindowsFileLinkService();
            var created = await service.CreateAsync(request, CancellationToken.None);

            await service.DeleteAsync(new LinkRecord(request, created.Fingerprint!), CancellationToken.None);

            Assert.False(File.Exists(linkPath));
            Assert.True(File.Exists(sourcePath));
            Assert.Equal("preserve source", await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            DeleteFileEntryIfPresent(linkPath);
            DeleteFileEntryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenSymbolicLinkWasReplacedWithOrdinaryFile_RejectsAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "source");
            var record = await CreateRecordAsync(sourcePath, linkPath);
            File.Delete(linkPath);
            await File.WriteAllTextAsync(linkPath, "ordinary replacement");

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("DeleteFileW", exception.Operation);
            Assert.Contains("Conflict", exception.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("ordinary replacement", await File.ReadAllTextAsync(linkPath));
        }
        finally
        {
            DeleteFileEntryIfPresent(linkPath);
            DeleteFileEntryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenSymbolicLinkWasReplacedWithDirectory_RejectsAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "source");
            var record = await CreateRecordAsync(sourcePath, linkPath);
            File.Delete(linkPath);
            Directory.CreateDirectory(linkPath);
            var sentinelPath = Path.Combine(linkPath, "sentinel.txt");
            await File.WriteAllTextAsync(sentinelPath, "preserve directory");

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("DeleteFileW", exception.Operation);
            Assert.Contains("Conflict", exception.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(linkPath));
            Assert.Equal("preserve directory", await File.ReadAllTextAsync(sentinelPath));
        }
        finally
        {
            DeleteFileEntryIfPresent(Path.Combine(linkPath, "sentinel.txt"));
            DeleteDirectoryEntryIfPresent(linkPath);
            DeleteFileEntryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenFileSymbolicLinkWasReplacedWithDirectorySymbolicLink_RejectsWrongType()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var directorySourcePath = Path.Combine(directory, "directory-source");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "source");
            Directory.CreateDirectory(directorySourcePath);
            var record = await CreateRecordAsync(sourcePath, linkPath);
            File.Delete(linkPath);
            Directory.CreateSymbolicLink(linkPath, directorySourcePath);

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("DeleteFileW", exception.Operation);
            Assert.Contains("type", exception.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(linkPath));
            Assert.True(Directory.Exists(directorySourcePath));
        }
        finally
        {
            DeleteDirectoryEntryIfPresent(linkPath);
            DeleteDirectoryEntryIfPresent(directorySourcePath);
            DeleteFileEntryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenSymbolicLinkTargetsDifferentSource_RejectsAndPreservesReplacementAndSources()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var otherSourcePath = Path.Combine(directory, "other-source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "original source");
            await File.WriteAllTextAsync(otherSourcePath, "other source");
            var record = await CreateRecordAsync(sourcePath, linkPath);
            File.Delete(linkPath);
            File.CreateSymbolicLink(linkPath, otherSourcePath);

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("DeleteFileW", exception.Operation);
            Assert.Contains("target", exception.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("other source", await File.ReadAllTextAsync(linkPath));
            Assert.Equal("original source", await File.ReadAllTextAsync(sourcePath));
            Assert.Equal("other source", await File.ReadAllTextAsync(otherSourcePath));
        }
        finally
        {
            DeleteFileEntryIfPresent(linkPath);
            DeleteFileEntryIfPresent(otherSourcePath);
            DeleteFileEntryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task InspectAndDeleteAsync_WhenSourceWasRemoved_UsesDanglingLinkEntryAndDeletesOnlyLink()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "temporary source");
            var record = await CreateRecordAsync(sourcePath, linkPath);
            File.Delete(sourcePath);

            var inspection = await new WindowsFileLinkService().InspectAsync(linkPath, CancellationToken.None);

            Assert.Equal(LinkStatus.Valid, inspection.Status);
            Assert.Equal(FileLinkType.FileSymbolicLink, inspection.Type);
            Assert.Equal(SymbolicLinkTag, inspection.Fingerprint?.ReparseTag);
            Assert.Equal(sourcePath, inspection.Fingerprint?.NormalizedTargetPath);

            await new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None);

            Assert.False(Path.Exists(linkPath));
            Assert.False(Path.Exists(sourcePath));
        }
        finally
        {
            DeleteFileEntryIfPresent(linkPath);
            DeleteFileEntryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Validate_DirectoryJunctionIsSupportedForDirectorySources()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source-directory");
        var linkPath = Path.Combine(directory, "link-directory");

        try
        {
            Directory.CreateDirectory(sourcePath);

            var validation = new WindowsFileLinkService().Validate(
                new LinkRequest(sourcePath, linkPath, FileLinkType.DirectoryJunction));

            Assert.True(validation.IsValid);
        }
        finally
        {
            DeleteDirectoryEntryIfPresent(sourcePath);
            Directory.Delete(directory);
        }
    }

    private static async Task<LinkRecord> CreateRecordAsync(string sourcePath, string linkPath)
    {
        var request = new LinkRequest(sourcePath, linkPath, FileLinkType.FileSymbolicLink);
        var created = await new WindowsFileLinkService().CreateAsync(request, CancellationToken.None);
        return new LinkRecord(request, created.Fingerprint!);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"FileLinkManager.SymbolicLinkTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteFileEntryIfPresent(string path)
    {
        if (File.Exists(path) || IsReparsePoint(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryEntryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }
}
