using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Links;
using FileLinkManager.Infrastructure.Paths;

namespace FileLinkManager.Infrastructure.Tests.Links;

public sealed class DirectoryJunctionIntegrationTests
{
    private const uint MountPointTag = 0xA0000003;

    [Fact]
    public async Task CreateAndInspectAsync_CreatesVerifiedJunctionAndSynchronizesChildFiles()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var sourceFilePath = Path.Combine(sourcePath, "child.txt");
        var linkPath = Path.Combine(directory, "junction");

        try
        {
            Directory.CreateDirectory(sourcePath);
            await File.WriteAllTextAsync(sourceFilePath, "source content");
            var request = new LinkRequest(sourcePath, linkPath, FileLinkType.DirectoryJunction);
            var service = new WindowsFileLinkService();

            Assert.True(service.Validate(request).IsValid);

            var created = await service.CreateAsync(request, CancellationToken.None);
            var readback = new ReparsePointReader().Read(linkPath);
            var inspected = await service.InspectAsync(linkPath, CancellationToken.None);

            Assert.Equal(LinkStatus.Valid, created.Status);
            Assert.Equal(FileLinkType.DirectoryJunction, created.Type);
            Assert.Equal(MountPointTag, created.Fingerprint?.ReparseTag);
            Assert.Equal(sourcePath, created.Fingerprint?.NormalizedTargetPath);
            Assert.Equal(linkPath, created.Fingerprint?.NormalizedLinkPath);
            Assert.Equal(FileLinkType.DirectoryJunction, readback.FileLinkType);
            Assert.Equal(MountPointTag, readback.Tag);
            Assert.Equal(sourcePath, readback.TargetPath);
            Assert.Equal(LinkStatus.Valid, inspected.Status);
            Assert.Equal(FileLinkType.DirectoryJunction, inspected.Type);
            Assert.Equal(MountPointTag, inspected.Fingerprint?.ReparseTag);
            Assert.Equal(sourcePath, inspected.Fingerprint?.NormalizedTargetPath);
            Assert.Equal("source content", await File.ReadAllTextAsync(Path.Combine(linkPath, "child.txt")));

            await File.WriteAllTextAsync(Path.Combine(linkPath, "child.txt"), "changed through junction");

            Assert.Equal("changed through junction", await File.ReadAllTextAsync(sourceFilePath));
        }
        finally
        {
            DeleteEntryIfPresent(linkPath);
            DeleteFileIfPresent(sourceFilePath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyJunctionEntryAndPreservesSourceContents()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var sourceFilePath = Path.Combine(sourcePath, "preserved.txt");
        var linkPath = Path.Combine(directory, "junction");

        try
        {
            Directory.CreateDirectory(sourcePath);
            await File.WriteAllTextAsync(sourceFilePath, "preserve source");
            var record = await CreateRecordAsync(sourcePath, linkPath);

            await new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None);

            Assert.False(PathEntryExists(linkPath));
            Assert.True(Directory.Exists(sourcePath));
            Assert.Equal("preserve source", await File.ReadAllTextAsync(sourceFilePath));
        }
        finally
        {
            DeleteEntryIfPresent(linkPath);
            DeleteFileIfPresent(sourceFilePath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task InspectAndDeleteAsync_WhenSourceWasRemoved_UsesDanglingJunctionEntry()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var sourceFilePath = Path.Combine(sourcePath, "temporary.txt");
        var linkPath = Path.Combine(directory, "junction");

        try
        {
            Directory.CreateDirectory(sourcePath);
            await File.WriteAllTextAsync(sourceFilePath, "temporary");
            var record = await CreateRecordAsync(sourcePath, linkPath);
            File.Delete(sourceFilePath);
            Directory.Delete(sourcePath);

            var inspection = await new WindowsFileLinkService().InspectAsync(linkPath, CancellationToken.None);

            Assert.Equal(LinkStatus.Valid, inspection.Status);
            Assert.Equal(FileLinkType.DirectoryJunction, inspection.Type);
            Assert.Equal(MountPointTag, inspection.Fingerprint?.ReparseTag);
            Assert.Equal(sourcePath, inspection.Fingerprint?.NormalizedTargetPath);

            await new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None);

            Assert.False(PathEntryExists(linkPath));
            Assert.False(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteEntryIfPresent(linkPath);
            DeleteFileIfPresent(sourceFilePath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_AfterRelativeCreationAndWorkingDirectoryChange_DeletesOriginalAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var createDirectory = Path.Combine(directory, "create");
        var deleteDirectory = Path.Combine(directory, "delete");
        var sourcePath = Path.Combine(createDirectory, "source");
        var sourceFilePath = Path.Combine(sourcePath, "source.txt");
        var linkPath = Path.Combine(createDirectory, "junction");
        var replacementPath = Path.Combine(deleteDirectory, "junction");
        var replacementFilePath = Path.Combine(replacementPath, "replacement.txt");
        var originalWorkingDirectory = Environment.CurrentDirectory;

        try
        {
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(deleteDirectory);
            await File.WriteAllTextAsync(sourceFilePath, "preserve source");

            Environment.CurrentDirectory = createDirectory;
            var request = new LinkRequest("source", "junction", FileLinkType.DirectoryJunction);
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
            Directory.CreateDirectory(replacementPath);
            await File.WriteAllTextAsync(replacementFilePath, "preserve replacement");

            await new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None);

            Assert.False(PathEntryExists(linkPath));
            Assert.Equal("preserve source", await File.ReadAllTextAsync(sourceFilePath));
            Assert.True(Directory.Exists(replacementPath));
            Assert.Equal("preserve replacement", await File.ReadAllTextAsync(replacementFilePath));
        }
        finally
        {
            Environment.CurrentDirectory = originalWorkingDirectory;
            DeleteFileIfPresent(replacementFilePath);
            DeleteEntryIfPresent(replacementPath);
            DeleteEntryIfPresent(linkPath);
            DeleteFileIfPresent(sourceFilePath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(createDirectory);
            DeleteEntryIfPresent(deleteDirectory);
            DeleteEntryIfPresent(directory);
        }
    }

    [Theory]
    [InlineData("ordinary-file")]
    [InlineData("ordinary-directory")]
    [InlineData("file-symbolic-link")]
    [InlineData("directory-symbolic-link")]
    [InlineData("junction-with-different-target")]
    public async Task DeleteAsync_WhenReplaced_RejectsAndPreservesReplacement(string replacementKind)
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var alternatePath = Path.Combine(directory, "alternate");
        var alternateFilePath = Path.Combine(alternatePath, "sentinel.txt");
        var fileTargetPath = Path.Combine(directory, "target.txt");
        var linkPath = Path.Combine(directory, "junction");

        try
        {
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(alternatePath);
            await File.WriteAllTextAsync(alternateFilePath, "preserve alternate");
            await File.WriteAllTextAsync(fileTargetPath, "preserve file target");
            var record = await CreateRecordAsync(sourcePath, linkPath);
            Directory.Delete(linkPath);

            switch (replacementKind)
            {
                case "ordinary-file":
                    await File.WriteAllTextAsync(linkPath, "preserve ordinary file");
                    break;
                case "ordinary-directory":
                    Directory.CreateDirectory(linkPath);
                    await File.WriteAllTextAsync(Path.Combine(linkPath, "sentinel.txt"), "preserve ordinary directory");
                    break;
                case "file-symbolic-link":
                    File.CreateSymbolicLink(linkPath, fileTargetPath);
                    break;
                case "directory-symbolic-link":
                    Directory.CreateSymbolicLink(linkPath, alternatePath);
                    break;
                case "junction-with-different-target":
                    await new WindowsFileLinkService().CreateAsync(
                        new LinkRequest(alternatePath, linkPath, FileLinkType.DirectoryJunction),
                        CancellationToken.None);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(replacementKind), replacementKind, null);
            }

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("SetFileInformationByHandle", exception.Operation);
            Assert.Contains("Conflict", exception.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.True(PathEntryExists(linkPath));
            Assert.True(Directory.Exists(sourcePath));
            Assert.Equal("preserve alternate", await File.ReadAllTextAsync(alternateFilePath));
            Assert.Equal("preserve file target", await File.ReadAllTextAsync(fileTargetPath));
        }
        finally
        {
            if (replacementKind == "ordinary-directory")
            {
                DeleteFileIfPresent(Path.Combine(linkPath, "sentinel.txt"));
            }

            DeleteEntryIfPresent(linkPath);
            DeleteFileIfPresent(fileTargetPath);
            DeleteFileIfPresent(alternateFilePath);
            DeleteEntryIfPresent(alternatePath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenSetReparseFails_RemovesOnlyTheEmptyPlaceholderItCreated()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var linkPath = Path.Combine(directory, "junction");

        try
        {
            Directory.CreateDirectory(sourcePath);
            var service = new WindowsFileLinkService(
                new WindowsPathService(),
                (_, _) => throw new FileLinkException("FSCTL_SET_REPARSE_POINT", linkPath, "forced native failure"));

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => service.CreateAsync(
                    new LinkRequest(sourcePath, linkPath, FileLinkType.DirectoryJunction),
                    CancellationToken.None));

            Assert.Equal("FSCTL_SET_REPARSE_POINT", exception.Operation);
            Assert.False(PathEntryExists(linkPath));
            Assert.True(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteEntryIfPresent(linkPath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenVerifiedJunctionIsReplacedBeforeDisposition_DeletesOriginalAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var linkPath = Path.Combine(directory, "junction");
        var movedOriginalPath = Path.Combine(directory, "moved-original");

        try
        {
            Directory.CreateDirectory(sourcePath);
            var record = await CreateRecordAsync(sourcePath, linkPath);
            var service = new WindowsFileLinkService(
                new WindowsPathService(),
                (_, _) => throw new InvalidOperationException("The deletion test does not set reparse data."),
                () =>
                {
                    Directory.Move(linkPath, movedOriginalPath);
                    Directory.CreateDirectory(linkPath);
                });

            await service.DeleteAsync(record, CancellationToken.None);

            Assert.False(PathEntryExists(movedOriginalPath));
            Assert.True(Directory.Exists(linkPath));
            Assert.True(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteEntryIfPresent(linkPath);
            DeleteEntryIfPresent(movedOriginalPath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenSetReparseFailsAfterPlaceholderIsReplaced_RemovesOriginalAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var linkPath = Path.Combine(directory, "junction");
        var movedOriginalPath = Path.Combine(directory, "moved-original");

        try
        {
            Directory.CreateDirectory(sourcePath);
            var service = new WindowsFileLinkService(
                new WindowsPathService(),
                (path, _) =>
                {
                    Directory.Move(path, movedOriginalPath);
                    Directory.CreateDirectory(path);
                    throw new FileLinkException("FSCTL_SET_REPARSE_POINT", path, "forced native failure");
                });

            await Assert.ThrowsAsync<FileLinkException>(
                () => service.CreateAsync(
                    new LinkRequest(sourcePath, linkPath, FileLinkType.DirectoryJunction),
                    CancellationToken.None));

            Assert.False(PathEntryExists(movedOriginalPath));
            Assert.True(Directory.Exists(linkPath));
            Assert.True(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteEntryIfPresent(linkPath);
            DeleteEntryIfPresent(movedOriginalPath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenSetReparseFailsAfterPlaceholderIsPopulated_PreservesTheDirectory()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var linkPath = Path.Combine(directory, "junction");
        var sentinelPath = Path.Combine(linkPath, "sentinel.txt");

        try
        {
            Directory.CreateDirectory(sourcePath);
            var service = new WindowsFileLinkService(
                new WindowsPathService(),
                (path, _) =>
                {
                    File.WriteAllText(Path.Combine(path, "sentinel.txt"), "preserve placeholder");
                    throw new FileLinkException("FSCTL_SET_REPARSE_POINT", path, "forced native failure");
                });

            await Assert.ThrowsAsync<FileLinkException>(
                () => service.CreateAsync(
                    new LinkRequest(sourcePath, linkPath, FileLinkType.DirectoryJunction),
                    CancellationToken.None));

            Assert.True(Directory.Exists(linkPath));
            Assert.Equal("preserve placeholder", await File.ReadAllTextAsync(sentinelPath));
            Assert.True(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteFileIfPresent(sentinelPath);
            DeleteEntryIfPresent(linkPath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenLinkPathAlreadyExists_PreservesThePreexistingDirectory()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var linkPath = Path.Combine(directory, "junction");
        var sentinelPath = Path.Combine(linkPath, "sentinel.txt");

        try
        {
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(linkPath);
            await File.WriteAllTextAsync(sentinelPath, "preserve existing entry");

            await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().CreateAsync(
                    new LinkRequest(sourcePath, linkPath, FileLinkType.DirectoryJunction),
                    CancellationToken.None));

            Assert.True(Directory.Exists(linkPath));
            Assert.Equal("preserve existing entry", await File.ReadAllTextAsync(sentinelPath));
        }
        finally
        {
            DeleteFileIfPresent(sentinelPath);
            DeleteEntryIfPresent(linkPath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenSetReparseFailsAfterPlaceholderBecomesReparsePoint_PreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source");
        var replacementTargetPath = Path.Combine(directory, "replacement-target");
        var replacementFilePath = Path.Combine(replacementTargetPath, "sentinel.txt");
        var linkPath = Path.Combine(directory, "junction");

        try
        {
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(replacementTargetPath);
            await File.WriteAllTextAsync(replacementFilePath, "preserve reparse replacement");
            var service = new WindowsFileLinkService(
                new WindowsPathService(),
                (path, _) =>
                {
                    Directory.Delete(path);
                    Directory.CreateSymbolicLink(path, replacementTargetPath);
                    throw new FileLinkException("FSCTL_SET_REPARSE_POINT", path, "forced native failure");
                });

            await Assert.ThrowsAsync<FileLinkException>(
                () => service.CreateAsync(
                    new LinkRequest(sourcePath, linkPath, FileLinkType.DirectoryJunction),
                    CancellationToken.None));

            Assert.True(PathEntryExists(linkPath));
            Assert.Equal("preserve reparse replacement", await File.ReadAllTextAsync(Path.Combine(linkPath, "sentinel.txt")));
            Assert.True(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteEntryIfPresent(linkPath);
            DeleteFileIfPresent(replacementFilePath);
            DeleteEntryIfPresent(replacementTargetPath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    private static async Task<LinkRecord> CreateRecordAsync(string sourcePath, string linkPath)
    {
        var request = new LinkRequest(sourcePath, linkPath, FileLinkType.DirectoryJunction);
        var created = await new WindowsFileLinkService().CreateAsync(request, CancellationToken.None);
        return new LinkRecord(request, created.Fingerprint!);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"FileLinkManager.DirectoryJunctionTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static bool PathEntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
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

    private static void DeleteFileIfPresent(string path)
    {
        if (PathEntryExists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteEntryIfPresent(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            Directory.Delete(path);
        }
        else
        {
            File.Delete(path);
        }
    }
}
