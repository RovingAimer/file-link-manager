using FileLinkManager.App.Presentation.Commands;
using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Presentation;

public sealed record RecordSearchScopeViewModel(LinkRecordSearchScope Scope, string DisplayName);

public sealed partial class LinkRecordsPageViewModel
{
    private LinkRecordOrganizationService organizationService = null!;
    private IOrganizationDialogService organizationDialogs = null!;
    private RecordGroupOptionViewModel selectedGroup = new(null, "全部分组", true);
    private RecordSearchScopeViewModel selectedSearchScope = new(LinkRecordSearchScope.Name, "名称");
    private IReadOnlyList<RecordGroupViewModel> recordGroups = Array.Empty<RecordGroupViewModel>();
    private readonly Dictionary<Guid, bool> expansion = new();

    public IReadOnlyList<RecordGroupOptionViewModel> GroupOptions { get; private set; } = new[] { new RecordGroupOptionViewModel(null, "全部分组", true) };
    public RecordGroupOptionViewModel SelectedGroup
    {
        get => selectedGroup;
        set { if (!IsBusy && value is not null && SetField(ref selectedGroup, value)) ApplyFilters(); }
    }
    public IReadOnlyList<RecordSearchScopeViewModel> SearchScopes { get; } = new[]
    {
        new RecordSearchScopeViewModel(LinkRecordSearchScope.Name, "名称"),
        new RecordSearchScopeViewModel(LinkRecordSearchScope.SourcePath, "源路径"),
        new RecordSearchScopeViewModel(LinkRecordSearchScope.LinkPath, "链接路径"),
        new RecordSearchScopeViewModel(LinkRecordSearchScope.Paths, "源路径或链接路径")
    };
    public RecordSearchScopeViewModel SelectedSearchScope
    {
        get => selectedSearchScope;
        set { if (!IsBusy && value is not null && SetField(ref selectedSearchScope, value)) ApplyFilters(); }
    }
    public IReadOnlyList<RecordGroupViewModel> RecordGroups { get => recordGroups; private set => SetField(ref recordGroups, value); }
    public bool HasFilters => SelectedDevice?.DeviceKey is not null || SelectedStatus.Status.HasValue ||
        !SelectedGroup.IsAll || !string.IsNullOrWhiteSpace(SearchText);
    private bool CanWriteOrganization => !IsBusy && !StorageAccess.IsReadOnly;
    public AsyncRelayCommand EditMetadataCommand { get; private set; } = null!;
    public AsyncRelayCommand ManageGroupsCommand { get; private set; } = null!;

    private void InitializeOrganization(IOrganizationDialogService? dialogs, LinkRecordOrganizationService? service)
    {
        organizationService = service ?? new LinkRecordOrganizationService(recordStore);
        organizationDialogs = dialogs ?? new WindowsOrganizationDialogService();
        EditMetadataCommand = new AsyncRelayCommand(EditMetadataAsync, parameter => CanWriteOrganization && parameter is LinkRecordRowViewModel);
        ManageGroupsCommand = new AsyncRelayCommand(_ => ManageGroupsAsync(), _ => CanWriteOrganization);
    }

    private LinkRecordFilter CurrentFilter() => new(SelectedDevice?.DeviceKey, SelectedStatus.Status, SearchText,
        SelectedGroup.GroupId, !SelectedGroup.IsAll && SelectedGroup.GroupId is null, SelectedSearchScope.Scope);

    private void UpdateGroupOptions(LinkDatabase nextDatabase)
    {
        var old = selectedGroup;
        GroupOptions = new[] { new RecordGroupOptionViewModel(null, "全部分组", true) }
            .Concat(nextDatabase.Groups.OrderBy(group => group.SortOrder).Select(group => new RecordGroupOptionViewModel(group.Id, group.Name)))
            .Append(new RecordGroupOptionViewModel(null, "未分组")).ToArray();
        selectedGroup = GroupOptions.FirstOrDefault(group => group.GroupId == old.GroupId && group.IsAll == old.IsAll) ?? GroupOptions[0];
        OnPropertyChanged(nameof(GroupOptions)); OnPropertyChanged(nameof(SelectedGroup));
    }

    private void BuildRecordGroups()
    {
        foreach (var group in RecordGroups) expansion[group.GroupId ?? Guid.Empty] = group.IsExpanded;
        RecordGroups = database!.Groups.OrderBy(group => group.SortOrder)
            .Select(group => new RecordGroupOptionViewModel(group.Id, group.Name))
            .Append(new RecordGroupOptionViewModel(null, "未分组"))
            .Select(group => new RecordGroupViewModel(group.GroupId, group.DisplayName,
                Records.Where(row => row.Record.GroupId == group.GroupId).ToArray())
                { IsExpanded = expansion.GetValueOrDefault(group.GroupId ?? Guid.Empty, true) })
            .Where(group => group.Count > 0 || !HasFilters).ToArray();
        OnPropertyChanged(nameof(HasFilters));
    }

    private async Task EditMetadataAsync(object? parameter)
    {
        var row = (LinkRecordRowViewModel)parameter!;
        await OrganizationOperationAsync(async () =>
        {
            var latest = await recordStore.LoadAsync(CancellationToken.None);
            RefreshStorageAccess();
            if (StorageAccess.IsReadOnly) throw new InvalidOperationException("记录存储处于只读模式。");
            var record = latest.Records.Single(item => item.Id == row.Id && item.DeviceKey == row.Record.DeviceKey);
            var editor = new RecordMetadataViewModel { CustomName = record.CustomName ?? "" };
            editor.SetGroups(latest.Groups, record.GroupId);
            return organizationDialogs.EditMetadata(editor)
                ? await organizationService.UpdateMetadataAsync(record.DeviceKey, record.Id, editor.ToMetadata(), CancellationToken.None) : null;
        }, "记录信息已保存，磁盘路径未改变。");
    }

    private async Task ManageGroupsAsync() => await OrganizationOperationAsync(async () =>
    {
        var latest = await recordStore.LoadAsync(CancellationToken.None);
        RefreshStorageAccess();
        if (StorageAccess.IsReadOnly) throw new InvalidOperationException("记录存储处于只读模式。");
        organizationDialogs.ShowGroupManager(new LinkGroupManagerViewModel(latest, organizationService, organizationDialogs, StorageAccess, RefreshStorageAccess));
        return await recordStore.LoadAsync(CancellationToken.None);
    }, "分组列表已更新。");

    private async Task OrganizationOperationAsync(Func<Task<LinkDatabase?>> operation, string success)
    {
        IsBusy = true;
        try
        {
            var result = await operation();
            if (result is null) return;
            SetDatabase(result, SelectedDevice?.DeviceKey);
            ResultMessage = success;
        }
        catch (Exception exception) { ResultMessage = $"组织信息操作失败：{exception.Message}"; }
        finally { RefreshStorageAccess(); IsBusy = false; }
    }

    private void RaiseOrganizationCommands()
    {
        EditMetadataCommand.RaiseCanExecuteChanged(); ManageGroupsCommand.RaiseCanExecuteChanged();
    }
}
