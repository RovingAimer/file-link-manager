using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Links;
using FileLinkManager.Infrastructure.Paths;

namespace FileLinkManager.Infrastructure.Tests.Links;

public sealed class HardLinkIntegrationTests
{
    [Fact]
    public void Validate_WhenDirectoryJunctionIsRequested_ReturnsValidForDirectorySource()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var linkPath = Path.Combine(directory, "junction");
        try
        {
            Directory.CreateDirectory(sourcePath);
            var validation = new WindowsFileLinkService().Validate(
                new LinkRequest(sourcePath, linkPath, FileLinkType.DirectoryJunction));

            Assert.True(validation.IsValid);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task CreateInspectAndDeleteAsync_HardLinkSharesFileIdentityAndPreservesSource()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "original");
            var request = new LinkRequest(sourcePath, linkPath, FileLinkType.FileHardLink);
            var service = new WindowsFileLinkService();

            Assert.True(service.Validate(request).IsValid);

            var created = await service.CreateAsync(request, CancellationToken.None);
            await File.WriteAllTextAsync(linkPath, "changed");

            Assert.Equal("changed", await File.ReadAllTextAsync(sourcePath));
            Assert.Equal(LinkStatus.Valid, created.Status);

            var source = await service.InspectAsync(sourcePath, CancellationToken.None);
            var link = await service.InspectAsync(linkPath, CancellationToken.None);

            Assert.Equal(LinkStatus.Valid, link.Status);
            Assert.NotNull(source.Fingerprint);
            Assert.NotNull(link.Fingerprint);
            Assert.Equal(source.Fingerprint.VolumeSerialNumber, link.Fingerprint.VolumeSerialNumber);
            Assert.Equal(source.Fingerprint.FileIdHex, link.Fingerprint.FileIdHex);
            Assert.Matches("^[0-9A-F]{32}$", link.Fingerprint.FileIdHex);

            var record = new LinkRecord(request, created.Fingerprint!);
            await service.DeleteAsync(record, CancellationToken.None);

            Assert.False(File.Exists(linkPath));
            Assert.True(File.Exists(sourcePath));
            Assert.Equal("changed", await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
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
            var request = new LinkRequest("source.txt", "link.txt", FileLinkType.FileHardLink);
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

            Assert.False(File.Exists(linkPath));
            Assert.Equal("source", await File.ReadAllTextAsync(sourcePath));
            Assert.Equal("replacement", await File.ReadAllTextAsync(replacementPath));
        }
        finally
        {
            Environment.CurrentDirectory = originalWorkingDirectory;
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenLinkPathWasReplacedWithOrdinaryFile_ThrowsConflictAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "original");
            var request = new LinkRequest(sourcePath, linkPath, FileLinkType.FileHardLink);
            var service = new WindowsFileLinkService();
            var created = await service.CreateAsync(request, CancellationToken.None);
            var record = new LinkRecord(request, created.Fingerprint!);

            File.Delete(linkPath);
            await File.WriteAllTextAsync(linkPath, "ordinary file");

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => service.DeleteAsync(record, CancellationToken.None));

            Assert.Equal("DeleteFileW", exception.Operation);
            Assert.Equal(linkPath, exception.Path);
            Assert.Equal("ordinary file", await File.ReadAllTextAsync(linkPath));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenLinkPathWasReplacedWithDirectory_ThrowsConflictAndPreservesDirectory()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "original");
            var request = new LinkRequest(sourcePath, linkPath, FileLinkType.FileHardLink);
            var service = new WindowsFileLinkService();
            var created = await service.CreateAsync(request, CancellationToken.None);
            var record = new LinkRecord(request, created.Fingerprint!);

            File.Delete(linkPath);
            Directory.CreateDirectory(linkPath);
            var sentinelPath = Path.Combine(linkPath, "sentinel.txt");
            await File.WriteAllTextAsync(sentinelPath, "preserve directory");

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => service.DeleteAsync(record, CancellationToken.None));

            Assert.Equal("DeleteFileW", exception.Operation);
            Assert.Equal(linkPath, exception.Path);
            Assert.True(Directory.Exists(linkPath));
            Assert.Equal("preserve directory", await File.ReadAllTextAsync(sentinelPath));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceDeletionIsAttemptedAfterValidation_ProtectsSourceEntry()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");
        var sourceDeleteBlocked = false;

        try
        {
            await File.WriteAllTextAsync(sourcePath, "original");
            var request = new LinkRequest(sourcePath, linkPath, FileLinkType.FileHardLink);
            var created = await new WindowsFileLinkService().CreateAsync(request, CancellationToken.None);
            var record = new LinkRecord(request, created.Fingerprint!);
            var service = new WindowsFileLinkService(
                new WindowsPathService(),
                setMountPointReparseData: null,
                beforeJunctionDisposition: null,
                beforeHardLinkDisposition: () =>
                {
                    try
                    {
                        File.Delete(sourcePath);
                    }
                    catch (IOException)
                    {
                        sourceDeleteBlocked = true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        sourceDeleteBlocked = true;
                    }
                });

            await service.DeleteAsync(record, CancellationToken.None);

            Assert.True(sourceDeleteBlocked);
            Assert.False(File.Exists(linkPath));
            Assert.True(File.Exists(sourcePath));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceEntryIsMissing_RefusesToDeleteTheLastHardLink()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "original");
            var request = new LinkRequest(sourcePath, linkPath, FileLinkType.FileHardLink);
            var created = await new WindowsFileLinkService().CreateAsync(request, CancellationToken.None);
            var record = new LinkRecord(request, created.Fingerprint!);
            File.Delete(sourcePath);

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("DeleteFileW", exception.Operation);
            Assert.True(File.Exists(linkPath));
            Assert.Equal("original", await File.ReadAllTextAsync(linkPath));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceEntryWasReplaced_RefusesToDeleteTheHardLink()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.txt");
        var linkPath = Path.Combine(directory, "link.txt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "original");
            var request = new LinkRequest(sourcePath, linkPath, FileLinkType.FileHardLink);
            var created = await new WindowsFileLinkService().CreateAsync(request, CancellationToken.None);
            var record = new LinkRecord(request, created.Fingerprint!);
            File.Delete(sourcePath);
            await File.WriteAllTextAsync(sourcePath, "replacement");

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("DeleteFileW", exception.Operation);
            Assert.Equal("original", await File.ReadAllTextAsync(linkPath));
            Assert.Equal("replacement", await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"FileLinkManager.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            File.Delete(file);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
        {
            foreach (var file in Directory.EnumerateFiles(childDirectory))
            {
                File.Delete(file);
            }

            Directory.Delete(childDirectory);
        }

        Directory.Delete(directory);
    }
}
