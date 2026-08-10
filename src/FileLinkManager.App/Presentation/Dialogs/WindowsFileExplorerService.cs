using System.Diagnostics;

namespace FileLinkManager.App.Presentation.Dialogs;

public sealed class WindowsFileExplorerService : IFileExplorerService
{
    public void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("A folder path is required.", nameof(folderPath));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folderPath}\"",
            UseShellExecute = true
        });
    }
}
