using System.IO;
using System.Reflection;
using FileLinkManager.App.Presentation.Commands;
using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.Application.Abstractions;

namespace FileLinkManager.App.Presentation;

public sealed class AboutPageViewModel : ObservableObject
{
    private readonly IFileExplorerService fileExplorerService;
    private string? resultMessage;

    public AboutPageViewModel(
        IDeviceIdentityProvider deviceIdentityProvider,
        string dataRootPath,
        IFileExplorerService fileExplorerService)
    {
        ArgumentNullException.ThrowIfNull(deviceIdentityProvider);
        this.fileExplorerService = fileExplorerService ?? throw new ArgumentNullException(nameof(fileExplorerService));
        if (string.IsNullOrWhiteSpace(dataRootPath))
        {
            throw new ArgumentException("A data root path is required.", nameof(dataRootPath));
        }

        var device = deviceIdentityProvider.GetCurrent();
        DataRootPath = Path.GetFullPath(dataRootPath);
        DataFilePath = Path.Combine(DataRootPath, "link-records.json");
        BackupFilePath = Path.Combine(DataRootPath, "link-records.json.bak");
        DeviceDisplayName = device.DisplayName;
        DeviceKeySummary = device.DeviceKey.Length <= 12
            ? device.DeviceKey
            : device.DeviceKey[..12];
        VersionText = GetVersionText();
        OpenDataFolderCommand = new RelayCommand(_ => OpenDataFolder());
    }

    public string VersionText { get; }

    public string DeviceDisplayName { get; }

    public string DeviceKeySummary { get; }

    public string DataRootPath { get; }

    public string DataFilePath { get; }

    public string BackupFilePath { get; }

    public string? ResultMessage
    {
        get => resultMessage;
        private set => SetField(ref resultMessage, value);
    }

    public RelayCommand OpenDataFolderCommand { get; }

    private void OpenDataFolder()
    {
        try
        {
            fileExplorerService.OpenFolder(DataRootPath);
            ResultMessage = "已打开数据目录（包含备份文件）。";
        }
        catch (Exception exception)
        {
            ResultMessage = $"打开数据目录失败：{exception.Message}";
        }
    }

    private static string GetVersionText()
    {
        var assembly = Assembly.GetEntryAssembly();
        var informationalVersion = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly?.GetName().Version?.ToString(3) ?? "未标记版本";
    }

}
