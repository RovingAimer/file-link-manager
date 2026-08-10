using FileLinkManager.Domain.Links;

namespace FileLinkManager.App.Presentation.Dialogs;

public interface IPathDialogService
{
    string? PickSourcePath(FileLinkType type, string currentPath);

    string? PickLinkParentPath(string currentPath);
}
