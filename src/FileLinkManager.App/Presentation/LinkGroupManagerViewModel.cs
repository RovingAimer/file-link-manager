using System.ComponentModel;
using FileLinkManager.App.Presentation.Commands;
using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Presentation;

public sealed class LinkGroupManagerViewModel : ObservableObject
{
    private readonly LinkRecordOrganizationService service;
    private readonly IOrganizationDialogService dialogs;
    private readonly StorageAccessViewModel storageAccess;
    private readonly Action refreshStorage;
    private IReadOnlyList<LinkRecordGroup> groups;
    private LinkRecordGroup? selectedGroup;
    private bool isBusy;
    private string? message;

    public LinkGroupManagerViewModel(LinkDatabase database, LinkRecordOrganizationService service,
        IOrganizationDialogService dialogs, StorageAccessViewModel storageAccess, Action refreshStorage,
        Func<Guid, bool>? canDissolveGroup = null)
    {
        this.service = service;
        this.dialogs = dialogs;
        this.storageAccess = storageAccess;
        this.refreshStorage = refreshStorage;
        groups = database.Groups.OrderBy(group => group.SortOrder).ToArray();
        NewCommand = new AsyncRelayCommand(_ => RunAsync(async () =>
        {
            var name = dialogs.PromptGroupName("新建分组", "");
            return name is null ? null : await service.CreateGroupAsync(name, CancellationToken.None);
        }), _ => CanWrite);
        RenameCommand = new AsyncRelayCommand(_ => RunAsync(async () =>
        {
            var group = SelectedGroup!;
            var name = dialogs.PromptGroupName("修改分组名称", group.Name);
            return name is null ? null : await service.RenameGroupAsync(group.Id, name, CancellationToken.None);
        }), _ => CanWrite && SelectedGroup is not null);
        DissolveCommand = new AsyncRelayCommand(_ => RunAsync(async () =>
        {
            var group = SelectedGroup!;
            if (canDissolveGroup is not null && !canDissolveGroup(group.Id))
            {
                Message = "该分组关联待重试登记，请先完成登记后再删除。";
                return null;
            }
            return dialogs.ConfirmDissolveGroup(group.Name)
                ? await service.DissolveGroupAsync(group.Id, CancellationToken.None) : null;
        }), _ => CanWrite && SelectedGroup is not null);
        storageAccess.PropertyChanged += StorageChanged;
    }

    public IReadOnlyList<LinkRecordGroup> Groups { get => groups; private set => SetField(ref groups, value); }
    public LinkRecordGroup? SelectedGroup
    {
        get => selectedGroup;
        set { if (SetField(ref selectedGroup, value)) RaiseCommands(); }
    }
    public bool IsBusy { get => isBusy; private set { SetField(ref isBusy, value); OnPropertyChanged(nameof(CanWrite)); RaiseCommands(); } }
    public bool CanWrite => !IsBusy && !storageAccess.IsReadOnly;
    public string? Message { get => message; private set => SetField(ref message, value); }
    public AsyncRelayCommand NewCommand { get; }
    public AsyncRelayCommand RenameCommand { get; }
    public AsyncRelayCommand DissolveCommand { get; }

    public void Detach() => storageAccess.PropertyChanged -= StorageChanged;

    private async Task RunAsync(Func<Task<LinkDatabase?>> operation)
    {
        IsBusy = true;
        Message = null;
        try
        {
            var id = SelectedGroup?.Id;
            var result = await operation();
            if (result is null) return;
            Groups = result.Groups.OrderBy(group => group.SortOrder).ToArray();
            SelectedGroup = Groups.FirstOrDefault(group => group.Id == id);
            Message = "分组变更已保存。";
        }
        catch (Exception exception) { Message = $"分组操作失败：{exception.Message}"; }
        finally { refreshStorage(); IsBusy = false; }
    }

    private void StorageChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanWrite));
        RaiseCommands();
    }
    private void RaiseCommands()
    {
        NewCommand.RaiseCanExecuteChanged(); RenameCommand.RaiseCanExecuteChanged(); DissolveCommand.RaiseCanExecuteChanged();
    }
}
