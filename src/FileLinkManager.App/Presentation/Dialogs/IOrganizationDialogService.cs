namespace FileLinkManager.App.Presentation.Dialogs;

public interface IOrganizationDialogService
{
    bool EditMetadata(RecordMetadataViewModel editor);
    string? PromptGroupName(string title, string initialName);
    bool ConfirmDissolveGroup(string name);
    void ShowGroupManager(LinkGroupManagerViewModel viewModel);
}
