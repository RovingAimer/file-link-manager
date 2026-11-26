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
    private string? groupMessage;
    public RecordMetadataViewModel MetadataEditor { get; } = new();
    public AsyncRelayCommand NewGroupCommand { get; private set; } = null!;
    public AsyncRelayCommand RenameGroupCommand { get; private set; } = null!;
    public AsyncRelayCommand DeleteGroupCommand { get; private set; } = null!;
    public string? GroupMessage { get => groupMessage; private set => SetField(ref groupMessage, value); }

    private void InitializeOrganization(ILinkRecordStore? store, IOrganizationDialogService? dialogs, LinkRecordOrganizationService? service)
    {
        organizationStore = store;
        organizationService = service ?? (store is null ? null : new LinkRecordOrganizationService(store));
        organizationDialogs = dialogs ?? new WindowsOrganizationDialogService();
        MetadataEditor.IsEnabled = !StorageAccess.IsReadOnly;
        MetadataEditor.PropertyChanged += MetadataChanged;
        NewGroupCommand = new AsyncRelayCommand(_ => CreateGroupAsync(), _ => store is not null && !IsBusy && !StorageAccess.IsReadOnly);
        RenameGroupCommand = new AsyncRelayCommand(parameter => ManageGroupAsync(parameter, rename: true), CanManageGroup);
        DeleteGroupCommand = new AsyncRelayCommand(parameter => ManageGroupAsync(parameter, rename: false), CanManageGroup);
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
            GroupMessage = "分组已创建。";
        }
        catch (Exception exception) { GroupMessage = $"新建分组失败：{exception.Message}"; }
        finally { RefreshStorageAccess(); IsBusy = false; }
    }

    internal bool CanDissolveGroup(Guid groupId) => pendingRequest is null || pendingMetadata.GroupId != groupId;

    private bool CanManageGroup(object? parameter) =>
        organizationStore is not null && !IsBusy && !StorageAccess.IsReadOnly &&
        parameter is RecordGroupOptionViewModel { GroupId: not null };

    private async Task ManageGroupAsync(object? parameter, bool rename)
    {
        if (parameter is not RecordGroupOptionViewModel { GroupId: { } groupId } || !CanManageGroup(parameter)) return;
        IsBusy = true;
        LinkGroupManagerViewModel? manager = null;
        try
        {
            var database = await organizationStore!.LoadAsync(CancellationToken.None);
            RefreshStorageAccess();
            if (StorageAccess.IsReadOnly) throw new InvalidOperationException("记录存储处于只读模式。");
            var group = database.Groups.SingleOrDefault(item => item.Id == groupId)
                ?? throw new InvalidOperationException("该分组已不存在，请重新加载分组。");
            var selectedId = MetadataEditor.SelectedGroup?.GroupId;
            manager = new LinkGroupManagerViewModel(database, organizationService!, organizationDialogs, StorageAccess, RefreshStorageAccess, CanDissolveGroup)
            {
                SelectedGroup = group
            };
            await (rename ? manager.RenameCommand : manager.DissolveCommand).ExecuteAsync(null);
            MetadataEditor.SetGroups(manager.Groups, selectedId);
            GroupMessage = manager.Message;
        }
        catch (Exception exception) { GroupMessage = $"分组操作失败：{exception.Message}"; }
        finally { manager?.Detach(); RefreshStorageAccess(); IsBusy = false; }
    }
}
