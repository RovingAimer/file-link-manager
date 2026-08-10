using System.Diagnostics;
using System.Windows;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Presentation.Dialogs;

public sealed class WindowsRecordPageDialogService : IRecordPageDialogService
{
    public bool ConfirmDelete(PersistedLinkRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return ShowConfirmation(
            "确认删除链接",
            $"将删除链接入口：\n{record.LinkRecord.Request.LinkPath}\n\n不会删除源路径：\n{record.LinkRecord.Request.SourcePath}");
    }

    public bool ConfirmDeleteMissing(PersistedLinkRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return ShowConfirmation(
            "确认标记为已删除",
            $"链接入口已不存在：\n{record.LinkRecord.Request.LinkPath}\n\n源路径不会被删除。是否保留历史并标记为已删除？");
    }

    public bool ConfirmRemoveDeletedRecord(PersistedLinkRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return ShowConfirmation(
            "确认删除历史记录",
            FormatRemoveDeletedRecordConfirmation(record));
    }

    internal static string FormatRemoveDeletedRecordConfirmation(PersistedLinkRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return $"只删除软件中的历史记录，不会再操作磁盘文件/目录。\n\n历史链接入口：\n{record.LinkRecord.Request.LinkPath}\n\n是否继续？";
    }

    public void OpenContainingFolder(string linkPath)
    {
        if (string.IsNullOrWhiteSpace(linkPath))
        {
            throw new ArgumentException("A link path is required.", nameof(linkPath));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{linkPath}\"",
            UseShellExecute = true
        });
    }

    private static bool ShowConfirmation(string title, string message)
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        var result = owner is null
            ? System.Windows.MessageBox.Show(
                message,
                title,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No)
            : System.Windows.MessageBox.Show(
                owner,
                message,
                title,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);
        return result == System.Windows.MessageBoxResult.Yes;
    }
}
