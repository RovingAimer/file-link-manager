using System.Diagnostics;
using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Links;

namespace FileLinkManager.Infrastructure.Tests.Links;

public sealed class DirectorySymbolicLinkIntegrationTests
{
    private const uint SymbolicLinkTag = 0xA000000C;

    [Fact]
    public async Task CreateAndInspectAsync_WithDirectorySource_CreatesVerifiedDirectorySymbolicLink()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source-directory");
        var sourceFilePath = Path.Combine(sourcePath, "child.txt");
        var linkPath = Path.Combine(directory, "directory-link");
        var linkedFilePath = Path.Combine(linkPath, "child.txt");

        try
        {
            Directory.CreateDirectory(sourcePath);
            await File.WriteAllTextAsync(sourceFilePath, "original");
            var request = new LinkRequest(sourcePath, linkPath, FileLinkType.DirectorySymbolicLink);
            var service = new WindowsFileLinkService();

            Assert.True(service.Validate(request).IsValid);

            var created = await service.CreateAsync(request, CancellationToken.None);
            var readback = new ReparsePointReader().Read(linkPath);
            var inspected = await service.InspectAsync(linkPath, CancellationToken.None);

            Assert.Equal(linkPath, created.LinkPath);
            Assert.Equal(LinkStatus.Valid, created.Status);
            Assert.Equal(FileLinkType.DirectorySymbolicLink, created.Type);
            Assert.Equal(SymbolicLinkTag, created.Fingerprint?.ReparseTag);
            Assert.Equal(sourcePath, created.Fingerprint?.NormalizedTargetPath);
            Assert.Equal(FileLinkType.DirectorySymbolicLink, readback.FileLinkType);
            Assert.Equal(SymbolicLinkTag, readback.Tag);
            Assert.Equal(sourcePath, readback.TargetPath);
            Assert.Equal(LinkStatus.Valid, inspected.Status);
            Assert.Equal(FileLinkType.DirectorySymbolicLink, inspected.Type);
            Assert.Equal(SymbolicLinkTag, inspected.Fingerprint?.ReparseTag);
            Assert.Equal(sourcePath, inspected.Fingerprint?.NormalizedTargetPath);
            Assert.Equal("original", await File.ReadAllTextAsync(linkedFilePath));

            await File.WriteAllTextAsync(linkedFilePath, "changed through directory link");

            Assert.Equal("changed through directory link", await File.ReadAllTextAsync(sourceFilePath));
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
    public async Task CreateAsync_WithRelativeSourceAndLinkPaths_UsesCanonicalAbsolutePaths()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "relative-source-directory");
        var sourceFilePath = Path.Combine(sourcePath, "child.txt");
        var linkPath = Path.Combine(directory, "relative-directory-link");
        var relativeSourcePath = Path.GetRelativePath(Environment.CurrentDirectory, sourcePath);
        var relativeLinkPath = Path.GetRelativePath(Environment.CurrentDirectory, linkPath);

        try
        {
            Directory.CreateDirectory(sourcePath);
            await File.WriteAllTextAsync(sourceFilePath, "relative source");
            Assert.False(Path.IsPathFullyQualified(relativeSourcePath));
            Assert.False(Path.IsPathFullyQualified(relativeLinkPath));

            var created = await new WindowsFileLinkService().CreateAsync(
                new LinkRequest(relativeSourcePath, relativeLinkPath, FileLinkType.DirectorySymbolicLink),
                CancellationToken.None);

            Assert.Equal(linkPath, created.LinkPath);
            Assert.True(Directory.Exists(linkPath));
            Assert.Equal("relative source", await File.ReadAllTextAsync(Path.Combine(linkPath, "child.txt")));
            Assert.Equal(sourcePath, created.Fingerprint?.NormalizedTargetPath);

            var readback = new ReparsePointReader().Read(created.LinkPath);
            Assert.Equal(FileLinkType.DirectorySymbolicLink, readback.FileLinkType);
            Assert.Equal(sourcePath, readback.TargetPath);
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
    public async Task DeleteAsync_AfterRelativeCreationAndWorkingDirectoryChange_DeletesOriginalLinkAndPreservesSameNamedReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var createWorkingDirectory = Path.Combine(directory, "create-working-directory");
        var deleteWorkingDirectory = Path.Combine(directory, "delete-working-directory");
        var sourcePath = Path.Combine(createWorkingDirectory, "source-directory");
        var sourceFilePath = Path.Combine(sourcePath, "preserved.txt");
        var linkPath = Path.Combine(createWorkingDirectory, "directory-link");
        var replacementPath = Path.Combine(deleteWorkingDirectory, "directory-link");
        var replacementFilePath = Path.Combine(replacementPath, "replacement.txt");
        var originalWorkingDirectory = Environment.CurrentDirectory;

        try
        {
            Directory.CreateDirectory(createWorkingDirectory);
            Directory.CreateDirectory(deleteWorkingDirectory);
            Directory.CreateDirectory(sourcePath);
            await File.WriteAllTextAsync(sourceFilePath, "preserve source");

            Environment.CurrentDirectory = createWorkingDirectory;
            var request = new LinkRequest("source-directory", "directory-link", FileLinkType.DirectorySymbolicLink);
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

            Environment.CurrentDirectory = deleteWorkingDirectory;
            Directory.CreateDirectory(replacementPath);
            await File.WriteAllTextAsync(replacementFilePath, "preserve replacement");

            await new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None);

            Assert.False(PathEntryExists(linkPath));
            Assert.True(Directory.Exists(sourcePath));
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
            DeleteEntryIfPresent(createWorkingDirectory);
            DeleteEntryIfPresent(deleteWorkingDirectory);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task InspectAsync_WithOrdinaryDirectory_ReportsThatDirectorySymbolicLinksAreSupported()
    {
        var directory = CreateTemporaryDirectory();
        var ordinaryDirectoryPath = Path.Combine(directory, "ordinary-directory");

        try
        {
            Directory.CreateDirectory(ordinaryDirectoryPath);

            var inspection = await new WindowsFileLinkService().InspectAsync(ordinaryDirectoryPath, CancellationToken.None);

            Assert.Equal(LinkStatus.Conflict, inspection.Status);
            Assert.Contains("file and directory symbolic links", inspection.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteEntryIfPresent(ordinaryDirectoryPath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_DirectorySymbolicLink_RemovesOnlyLinkEntryAndPreservesSourceTree()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source-directory");
        var sourceFilePath = Path.Combine(sourcePath, "preserved.txt");
        var linkPath = Path.Combine(directory, "directory-link");

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
    public async Task InspectAndDeleteAsync_WhenSourceDirectoryWasRemoved_UsesDanglingLinkEntry()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source-directory");
        var sourceFilePath = Path.Combine(sourcePath, "temporary.txt");
        var linkPath = Path.Combine(directory, "directory-link");

        try
        {
            Directory.CreateDirectory(sourcePath);
            await File.WriteAllTextAsync(sourceFilePath, "temporary source");
            var record = await CreateRecordAsync(sourcePath, linkPath);
            File.Delete(sourceFilePath);
            Directory.Delete(sourcePath);

            Assert.True(PathEntryExists(linkPath));

            var inspection = await new WindowsFileLinkService().InspectAsync(linkPath, CancellationToken.None);

            Assert.Equal(LinkStatus.Valid, inspection.Status);
            Assert.Equal(FileLinkType.DirectorySymbolicLink, inspection.Type);
            Assert.Equal(SymbolicLinkTag, inspection.Fingerprint?.ReparseTag);
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
    public async Task DeleteAsync_WhenReplacedWithOrdinaryFile_RejectsAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source-directory");
        var linkPath = Path.Combine(directory, "directory-link");

        try
        {
            Directory.CreateDirectory(sourcePath);
            var record = await CreateRecordAsync(sourcePath, linkPath);
            Directory.Delete(linkPath);
            await File.WriteAllTextAsync(linkPath, "ordinary file replacement");

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("RemoveDirectoryW", exception.Operation);
            Assert.Contains("Conflict", exception.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("ordinary file replacement", await File.ReadAllTextAsync(linkPath));
            Assert.True(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteFileIfPresent(linkPath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenReplacedWithOrdinaryDirectory_RejectsAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source-directory");
        var linkPath = Path.Combine(directory, "directory-link");
        var sentinelPath = Path.Combine(linkPath, "sentinel.txt");

        try
        {
            Directory.CreateDirectory(sourcePath);
            var record = await CreateRecordAsync(sourcePath, linkPath);
            Directory.Delete(linkPath);
            Directory.CreateDirectory(linkPath);
            await File.WriteAllTextAsync(sentinelPath, "preserve ordinary directory");

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("RemoveDirectoryW", exception.Operation);
            Assert.Contains("Conflict", exception.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve ordinary directory", await File.ReadAllTextAsync(sentinelPath));
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
    public async Task DeleteAsync_WhenReplacedWithFileSymbolicLink_RejectsAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source-directory");
        var replacementTargetPath = Path.Combine(directory, "replacement-target.txt");
        var linkPath = Path.Combine(directory, "directory-link");

        try
        {
            Directory.CreateDirectory(sourcePath);
            await File.WriteAllTextAsync(replacementTargetPath, "file symbolic-link target");
            var record = await CreateRecordAsync(sourcePath, linkPath);
            Directory.Delete(linkPath);
            File.CreateSymbolicLink(linkPath, replacementTargetPath);

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("RemoveDirectoryW", exception.Operation);
            Assert.Contains("type", exception.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("file symbolic-link target", await File.ReadAllTextAsync(linkPath));
            Assert.True(File.Exists(replacementTargetPath));
            Assert.True(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteFileIfPresent(linkPath);
            DeleteFileIfPresent(replacementTargetPath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenReplacedWithJunction_RejectsAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source-directory");
        var junctionTargetPath = Path.Combine(directory, "junction-target");
        var junctionTargetFilePath = Path.Combine(junctionTargetPath, "sentinel.txt");
        var linkPath = Path.Combine(directory, "directory-link");

        try
        {
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(junctionTargetPath);
            await File.WriteAllTextAsync(junctionTargetFilePath, "preserve junction target");
            var record = await CreateRecordAsync(sourcePath, linkPath);
            Directory.Delete(linkPath);
            await CreateJunctionAsync(linkPath, junctionTargetPath);

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("RemoveDirectoryW", exception.Operation);
            Assert.Contains("tag", exception.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve junction target", await File.ReadAllTextAsync(Path.Combine(linkPath, "sentinel.txt")));
            Assert.True(Directory.Exists(junctionTargetPath));
            Assert.True(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteEntryIfPresent(linkPath);
            DeleteFileIfPresent(junctionTargetFilePath);
            DeleteEntryIfPresent(junctionTargetPath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenReplacedWithDifferentTargetDirectorySymbolicLink_RejectsAndPreservesReplacement()
    {
        var directory = CreateTemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source-directory");
        var otherSourcePath = Path.Combine(directory, "other-source-directory");
        var otherSourceFilePath = Path.Combine(otherSourcePath, "sentinel.txt");
        var linkPath = Path.Combine(directory, "directory-link");

        try
        {
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(otherSourcePath);
            await File.WriteAllTextAsync(otherSourceFilePath, "preserve other target");
            var record = await CreateRecordAsync(sourcePath, linkPath);
            Directory.Delete(linkPath);
            Directory.CreateSymbolicLink(linkPath, otherSourcePath);

            var exception = await Assert.ThrowsAsync<FileLinkException>(
                () => new WindowsFileLinkService().DeleteAsync(record, CancellationToken.None));

            Assert.Equal("RemoveDirectoryW", exception.Operation);
            Assert.Contains("target", exception.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve other target", await File.ReadAllTextAsync(Path.Combine(linkPath, "sentinel.txt")));
            Assert.True(Directory.Exists(otherSourcePath));
            Assert.True(Directory.Exists(sourcePath));
        }
        finally
        {
            DeleteEntryIfPresent(linkPath);
            DeleteFileIfPresent(otherSourceFilePath);
            DeleteEntryIfPresent(otherSourcePath);
            DeleteEntryIfPresent(sourcePath);
            DeleteEntryIfPresent(directory);
        }
    }

    private static async Task<LinkRecord> CreateRecordAsync(string sourcePath, string linkPath)
    {
        var request = new LinkRequest(sourcePath, linkPath, FileLinkType.DirectorySymbolicLink);
        var created = await new WindowsFileLinkService().CreateAsync(request, CancellationToken.None);
        return new LinkRecord(request, created.Fingerprint!);
    }

    private static async Task CreateJunctionAsync(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start cmd.exe for junction test setup.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Junction test setup failed with exit code {process.ExitCode}: {standardOutput} {standardError}");
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"FileLinkManager.DirectorySymbolicLinkTests.{Guid.NewGuid():N}");
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
