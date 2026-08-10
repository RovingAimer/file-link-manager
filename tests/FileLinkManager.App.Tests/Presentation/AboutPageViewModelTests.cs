using FileLinkManager.App.Presentation;
using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Tests.Presentation;

public sealed class AboutPageViewModelTests
{
    private static readonly DeviceIdentity Device = new(
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
        "Test Device");

    [Fact]
    public void AboutState_ExposesVersionDeviceDigestAndPortableDataPaths()
    {
        var explorer = new FakeFileExplorerService();
        var viewModel = new AboutPageViewModel(
            new FakeDeviceIdentityProvider(Device),
            @"C:\PortableApp\data",
            explorer);

        Assert.False(string.IsNullOrWhiteSpace(viewModel.VersionText));
        Assert.Equal(Device.DisplayName, viewModel.DeviceDisplayName);
        Assert.Equal(Device.DeviceKey[..12], viewModel.DeviceKeySummary);
        Assert.DoesNotContain(Device.DeviceKey, viewModel.DeviceKeySummary, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("data", "link-records.json"), viewModel.DataFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("data", "link-records.json.bak"), viewModel.BackupFilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenDataFolderCommand_DelegatesPathAndShowsResult()
    {
        var explorer = new FakeFileExplorerService();
        var viewModel = new AboutPageViewModel(
            new FakeDeviceIdentityProvider(Device),
            @"C:\PortableApp\data",
            explorer);

        viewModel.OpenDataFolderCommand.Execute(null);

        Assert.Equal(viewModel.DataRootPath, explorer.OpenedPath);
        Assert.Equal("已打开数据目录（包含备份文件）。", viewModel.ResultMessage);
    }

    [Fact]
    public void OpenDataFolderCommand_WhenExplorerFails_ShowsError()
    {
        var explorer = new FakeFileExplorerService { Exception = new InvalidOperationException("Explorer failed.") };
        var viewModel = new AboutPageViewModel(
            new FakeDeviceIdentityProvider(Device),
            @"C:\PortableApp\data",
            explorer);

        viewModel.OpenDataFolderCommand.Execute(null);

        Assert.Contains("Explorer failed", viewModel.ResultMessage, StringComparison.Ordinal);
    }

    private sealed class FakeDeviceIdentityProvider(DeviceIdentity identity) : IDeviceIdentityProvider
    {
        public DeviceIdentity GetCurrent() => identity;
    }

    private sealed class FakeFileExplorerService : IFileExplorerService
    {
        public Exception? Exception { get; set; }

        public string? OpenedPath { get; private set; }

        public void OpenFolder(string folderPath)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            OpenedPath = folderPath;
        }
    }
}
