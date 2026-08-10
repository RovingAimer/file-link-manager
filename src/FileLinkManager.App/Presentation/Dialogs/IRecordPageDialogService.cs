using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Presentation.Dialogs;

public interface IRecordPageDialogService
{
    bool ConfirmDelete(PersistedLinkRecord record);

    bool ConfirmDeleteMissing(PersistedLinkRecord record);

    bool ConfirmRemoveDeletedRecord(PersistedLinkRecord record);

    void OpenContainingFolder(string linkPath);
}
