using System.ComponentModel;
using FileLinkManager.App.Presentation.Commands;
using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.Application.Abstractions;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Presentation;

public sealed partial class CreateLinkPageViewModel
{
    private ILinkRecordStore? organizationStore;
    private LinkRecordOrganizationService? organizationService;
    private IOrganizationDialogService organizationDialogs = null!;
    private LinkRecordMetadata pendingMetadata = new();
    public RecordMetadataViewModel MetadataEditor { get; } = new();
    public AsyncRelayCommand NewGroupCommand { get; private set; } = null!;

    private void InitializeOrganization(ILinkRecordStore? store, IOrganizationDialogService? dialogs, LinkRecordOrganizationService? service)
    {
        organizationStore = store;
        organizationService = service ?? (store is null ? null : new LinkRecordOrganizationService(store));
        organizationDialogs = dialogs ?? new WindowsOrganizationDialogService();
        MetadataEditor.IsEnabled = !StorageAccess.IsReadOnly;
        MetadataEditor.PropertyChanged += MetadataChanged;
        NewGroupCommand = new AsyncRelayCommand(_ => CreateGroupAsync(), _ => store is not null && !IsBusy && !StorageAccess.IsReadOnly);
    }

    private void MetadataChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanCreate));
        CreateCommand.RaiseCanExecuteChanged();
    }

    public async Task EnterPageAsync()
    {
        if (organizationStore is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var database = await organizationStore.LoadAsync(CancellationToken.None);
            MetadataEditor.SetGroups(database.Groups, MetadataEditor.SelectedGroup?.GroupId);
        }
        catch (Exception exception) { ResultMessage = $"加载分组失败：{exception.Message}"; }
        finally { RefreshStorageAccess(); IsBusy = false; }
    }

    private async Task CreateGroupAsync()
    {
        IsBusy = true;
        try
        {
            var name = organizationDialogs.PromptGroupName("新建分组", "");
            if (name is null) return;
            var database = await organizationService!.CreateGroupAsync(name, CancellationToken.None);
            var group = database.Groups.Single(item => item.Name == name.Trim());
            MetadataEditor.SetGroups(database.Groups, group.Id);
            ResultMessage = "分组已创建。";
        }
        catch (Exception exception) { ResultMessage = $"新建分组失败：{exception.Message}"; }
        finally { RefreshStorageAccess(); IsBusy = false; }
    }
}
