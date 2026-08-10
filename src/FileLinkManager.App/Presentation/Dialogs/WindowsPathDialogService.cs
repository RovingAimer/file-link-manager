using System.IO;
using FileLinkManager.Domain.Links;
using Microsoft.Win32;

namespace FileLinkManager.App.Presentation.Dialogs;

public sealed class WindowsPathDialogService : IPathDialogService
{
    public string? PickSourcePath(FileLinkType type, string currentPath)
    {
        return type is FileLinkType.FileHardLink or FileLinkType.FileSymbolicLink
            ? PickFile(currentPath)
            : PickFolder(currentPath);
    }

    public string? PickLinkParentPath(string currentPath) => PickFolder(GetParentPath(currentPath));

    private static string? PickFile(string currentPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择源文件",
            CheckFileExists = true,
            Multiselect = false
        };
        SetInitialDirectory(dialog, currentPath);
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? PickFolder(string currentPath)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(currentPath))
        {
            dialog.SelectedPath = currentPath;
        }

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private static void SetInitialDirectory(Microsoft.Win32.OpenFileDialog dialog, string currentPath)
    {
        var directory = File.Exists(currentPath)
            ? Path.GetDirectoryName(currentPath)
            : Directory.Exists(currentPath) ? currentPath : null;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            dialog.InitialDirectory = directory;
        }
    }

    private static string GetParentPath(string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetDirectoryName(currentPath) ?? currentPath;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }
}
