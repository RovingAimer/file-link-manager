using System.ComponentModel;
using FileLinkManager.App.Presentation.Commands;
using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.Application.Abstractions;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Presentation;

public sealed record RecordDeviceOptionViewModel(
    string? DeviceKey,
    string DisplayName,
    bool IsCurrentDevice,
    bool IsOfflineSnapshot);

public sealed record RecordStatusOptionViewModel(LinkStatus? Status, string DisplayName);

public sealed partial class LinkRecordsPageViewModel : ObservableObject
{
    private static readonly IReadOnlyList<RecordStatusOptionViewModel> DefaultStatusOptions = new[]
    {
        new RecordStatusOptionViewModel(null, "全部状态"),
        new RecordStatusOptionViewModel(LinkStatus.Valid, "有效"),
        new RecordStatusOptionViewModel(LinkStatus.Missing, "失效（路径缺失）"),
        new RecordStatusOptionViewModel(LinkStatus.Conflict, "冲突"),
        new RecordStatusOptionViewModel(LinkStatus.Unknown, "未知"),
        new RecordStatusOptionViewModel(LinkStatus.Deleted, "已删除")
    };

    private readonly ILinkRecordStore recordStore;
    private readonly IDeviceIdentityProvider deviceIdentityProvider;
    private readonly LinkRecordQueryService queryService;
    private readonly InspectLinksUseCase inspectLinksUseCase;
    private readonly DeleteLinksUseCase deleteLinksUseCase;
    private readonly RemoveDeletedLinkRecordUseCase removeDeletedLinkRecordUseCase;
    private readonly IRecordPageDialogService dialogService;
    private readonly ILinkRecordStoreDiagnostics? recordStoreDiagnostics;
    private readonly StorageAccessViewModel storageAccess;
    private DeviceIdentity currentDevice;
    private LinkDatabase? database;
    private RecordDeviceOptionViewModel? selectedDevice;
    private RecordStatusOptionViewModel selectedStatus;
    private string searchText = string.Empty;
    private IReadOnlyList<LinkRecordRowViewModel> records = Array.Empty<LinkRecordRowViewModel>();
    private LinkStatistics statistics = new(0, 0);
    private bool isBusy;
    private string? resultMessage;

    public LinkRecordsPageViewModel(
        ILinkRecordStore recordStore,
        IDeviceIdentityProvider deviceIdentityProvider,
        LinkRecordQueryService queryService,
        InspectLinksUseCase inspectLinksUseCase,
        DeleteLinksUseCase deleteLinksUseCase,
        IRecordPageDialogService dialogService,
        StorageAccessViewModel? storageAccess = null,
        ILinkRecordStoreDiagnostics? recordStoreDiagnostics = null,
        RemoveDeletedLinkRecordUseCase? removeDeletedLinkRecordUseCase = null,
        IOrganizationDialogService? organizationDialogs = null,
        LinkRecordOrganizationService? organizationService = null)
    {
        this.recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        this.deviceIdentityProvider = deviceIdentityProvider ?? throw new ArgumentNullException(nameof(deviceIdentityProvider));
        this.queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        this.inspectLinksUseCase = inspectLinksUseCase ?? throw new ArgumentNullException(nameof(inspectLinksUseCase));
        this.deleteLinksUseCase = deleteLinksUseCase ?? throw new ArgumentNullException(nameof(deleteLinksUseCase));
        this.removeDeletedLinkRecordUseCase = removeDeletedLinkRecordUseCase ?? new RemoveDeletedLinkRecordUseCase(recordStore);
        this.dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        this.storageAccess = storageAccess ?? new StorageAccessViewModel();
        this.recordStoreDiagnostics = recordStoreDiagnostics;
        this.storageAccess.PropertyChanged += StorageAccessOnPropertyChanged;

        currentDevice = this.deviceIdentityProvider.GetCurrent();
        StatusOptions = DefaultStatusOptions;
        selectedStatus = StatusOptions[0];
        Devices = BuildDeviceOptions(LinkDatabase.Empty(), currentDevice);
        selectedDevice = Devices.First(device => device.IsCurrentDevice);

        RefreshCommand = new AsyncRelayCommand(
            _ => ExecuteRefreshAsync(),
            _ => !IsBusy);
        InspectCommand = new AsyncRelayCommand(
            _ => ExecuteRefreshAsync(),
            _ => !IsBusy && !StorageAccess.IsReadOnly);
        ClearFiltersCommand = new RelayCommand(
            _ => ClearFilters(),
            _ => !IsBusy);
        OpenFolderCommand = new RelayCommand(
            OpenFolder,
            CanOpenFolder);
        DeleteCommand = new AsyncRelayCommand(
            DeleteAsync,
            CanDelete);
        InitializeOrganization(organizationDialogs, organizationService);
    }

    public IReadOnlyList<RecordDeviceOptionViewModel> Devices { get; private set; }

    public RecordDeviceOptionViewModel? SelectedDevice
    {
        get => selectedDevice;
        set
        {
            if (IsBusy || ReferenceEquals(selectedDevice, value))
            {
                return;
            }

            selectedDevice = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public IReadOnlyList<RecordStatusOptionViewModel> StatusOptions { get; }

    public RecordStatusOptionViewModel SelectedStatus
    {
        get => selectedStatus;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (IsBusy || ReferenceEquals(selectedStatus, value))
            {
                return;
            }

            selectedStatus = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (IsBusy)
            {
                return;
            }

            var next = value ?? string.Empty;
            if (string.Equals(searchText, next, StringComparison.Ordinal))
            {
                return;
            }

            searchText = next;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public IReadOnlyList<LinkRecordRowViewModel> Records
    {
        get => records;
        private set => SetField(ref records, value);
    }

    public LinkStatistics Statistics
    {
        get => statistics;
        private set
        {
            if (!SetField(ref statistics, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HistoricalTotal));
            OnPropertyChanged(nameof(CurrentValid));
        }
    }

    public int HistoricalTotal => Statistics.HistoricalTotal;

    public int CurrentValid => Statistics.CurrentValid;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetField(ref isBusy, value))
            {
                return;
            }

            RaiseCommandStates();
            OnPropertyChanged(nameof(CanEditFilters));
        }
    }

    public bool CanEditFilters => !IsBusy;

    public string? ResultMessage
    {
        get => resultMessage;
        private set => SetField(ref resultMessage, value);
    }

    public StorageAccessViewModel StorageAccess => storageAccess;

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand InspectCommand { get; }

    public RelayCommand ClearFiltersCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public Task EnterPageAsync() => ExecuteRefreshAsync(forceCurrentInspection: true);

    private Task ExecuteRefreshAsync() => ExecuteRefreshAsync(forceCurrentInspection: false);

    private async Task ExecuteRefreshAsync(bool forceCurrentInspection)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ResultMessage = null;
        var selectedDeviceKey = SelectedDevice?.DeviceKey;

        try
        {
            currentDevice = deviceIdentityProvider.GetCurrent();
            var loadedDatabase = await recordStore.LoadAsync(CancellationToken.None);
            RefreshStorageAccess();
            var shouldInspectCurrentDevice = forceCurrentInspection ||
                selectedDeviceKey is null ||
                string.Equals(selectedDeviceKey, currentDevice.DeviceKey, StringComparison.Ordinal);

            if (StorageAccess.IsReadOnly)
            {
                ResultMessage = "记录存储处于只读模式，未检查当前设备链接。";
            }
            else if (shouldInspectCurrentDevice)
            {
                var inspectionResult = await inspectLinksUseCase.ExecuteAsync(CancellationToken.None);
                RefreshStorageAccess();
                loadedDatabase = inspectionResult.Database;
                ResultMessage = $"已检查当前设备 {inspectionResult.InspectedCount} 条链接。";
            }
            else
            {
                ResultMessage = "已载入离线设备快照，未检查其他设备路径。";
            }

            SetDatabase(loadedDatabase, selectedDeviceKey);
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "记录检查已取消。";
        }
        catch (Exception exception)
        {
            ResultMessage = $"加载记录失败：{exception.Message}";
        }
        finally
        {
            RefreshStorageAccess();
            IsBusy = false;
        }
    }

    private void ClearFilters()
    {
        if (IsBusy)
        {
            return;
        }

        selectedDevice = Devices.First(device => device.DeviceKey is null);
        selectedStatus = StatusOptions[0];
        searchText = string.Empty;
        selectedGroup = GroupOptions[0];
        OnPropertyChanged(nameof(SelectedGroup));
        OnPropertyChanged(nameof(SelectedDevice));
        OnPropertyChanged(nameof(SelectedStatus));
        OnPropertyChanged(nameof(SearchText));
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (database is null)
        {
            Records = Array.Empty<LinkRecordRowViewModel>();
            Statistics = new LinkStatistics(0, 0);
            return;
        }

        var filter = CurrentFilter();
        Records = queryService
            .Query(database, filter)
            .Select(record => new LinkRecordRowViewModel(record, currentDevice.DeviceKey))
            .ToArray();
        Statistics = queryService.GetStatistics(database, SelectedDevice?.DeviceKey);
        BuildRecordGroups();
        RaiseCommandStates();
    }

    private void SetDatabase(LinkDatabase nextDatabase, string? selectedDeviceKey)
    {
        database = nextDatabase;
        Devices = BuildDeviceOptions(nextDatabase, currentDevice);
        selectedDevice = Devices.FirstOrDefault(device => device.DeviceKey == selectedDeviceKey) ??
            Devices.First(device => device.IsCurrentDevice);
        OnPropertyChanged(nameof(Devices));
        OnPropertyChanged(nameof(SelectedDevice));
        UpdateGroupOptions(nextDatabase);
        ApplyFilters();
    }

    private void OpenFolder(object? parameter)
    {
        if (parameter is not LinkRecordRowViewModel row || !CanOpenFolder(row))
        {
            return;
        }

        try
        {
            dialogService.OpenContainingFolder(row.LinkPath);
            ResultMessage = "已打开链接所在目录。";
        }
        catch (Exception exception)
        {
            ResultMessage = $"打开目录失败：{exception.Message}";
        }
    }

    private async Task DeleteAsync(object? parameter)
    {
        if (parameter is not LinkRecordRowViewModel row || !CanDelete(row))
        {
            return;
        }

        if (row.IsDeleted)
        {
            await RemoveDeletedRecordAsync(row);
            return;
        }

        if (!dialogService.ConfirmDelete(row.Record))
        {
            ResultMessage = "已取消删除。";
            return;
        }

        IsBusy = true;
        try
        {
            var outcome = await deleteLinksUseCase.ExecuteAsync(row.Id, false, CancellationToken.None);
            if (outcome is MissingConfirmationRequired missing)
            {
                ApplyRecord(missing.Record);
                if (!dialogService.ConfirmDeleteMissing(missing.Record))
                {
                    ResultMessage = "链接入口已缺失，未标记为已删除。";
                    return;
                }

                outcome = await deleteLinksUseCase.ExecuteAsync(row.Id, true, CancellationToken.None);
            }

            await ApplyDeleteOutcomeAsync(outcome);
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "删除操作已取消。";
        }
        catch (Exception exception)
        {
            ResultMessage = $"删除失败：{exception.Message}";
        }
        finally
        {
            RefreshStorageAccess();
            IsBusy = false;
        }
    }

    private async Task RemoveDeletedRecordAsync(LinkRecordRowViewModel row)
    {
        if (!dialogService.ConfirmRemoveDeletedRecord(row.Record))
        {
            ResultMessage = "已取消删除历史记录。";
            return;
        }

        IsBusy = true;
        var selectedDeviceKey = SelectedDevice?.DeviceKey;
        try
        {
            var outcome = await removeDeletedLinkRecordUseCase.ExecuteAsync(
                row.Record.DeviceKey,
                row.Id,
                CancellationToken.None);
            switch (outcome)
            {
                case DeletedLinkRecordRemoved removed:
                    RefreshStorageAccess();
                    SetDatabase(removed.Database, selectedDeviceKey);
                    ResultMessage = "仅删除了软件中的历史记录，不会再操作磁盘文件/目录。";
                    break;
                case RemoveDeletedLinkRecordRejected rejected:
                    SetDatabase(rejected.Database, selectedDeviceKey);
                    ResultMessage = $"删除记录被拒绝：{rejected.Reason}";
                    break;
                default:
                    throw new InvalidOperationException("Unknown deleted-record removal outcome.");
            }
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "删除历史记录操作已取消。";
        }
        catch (Exception exception)
        {
            ResultMessage = $"删除历史记录失败：{exception.Message}";
        }
        finally
        {
            RefreshStorageAccess();
            IsBusy = false;
        }
    }

    private async Task ApplyDeleteOutcomeAsync(DeleteLinksOutcome outcome)
    {
        switch (outcome)
        {
            case DeletedAndRegistered deleted:
                ApplyRecord(deleted.Record);
                ResultMessage = string.IsNullOrWhiteSpace(deleted.SourceCheckReason)
                    ? "链接入口已删除，源路径未受影响。"
                    : $"链接入口已删除，但源路径检查提示：{deleted.SourceCheckReason}";
                break;
            case DeletedButUnregistered unregistered:
                ApplyRecord(unregistered.Record);
                ResultMessage = $"链接入口已删除，但删除记录未能保存：{unregistered.StorageError}";
                break;
            case DeleteLinkRejected rejected:
                ResultMessage = $"删除被拒绝：{rejected.Reason}";
                await ReloadFromStoreAsync();
                break;
            case MissingConfirmationRequired:
                throw new InvalidOperationException("A confirmed delete unexpectedly still requires missing-link confirmation.");
            default:
                throw new InvalidOperationException("Unknown delete outcome.");
        }
    }

    private async Task ReloadFromStoreAsync()
    {
        var selectedDeviceKey = SelectedDevice?.DeviceKey;
        var loadedDatabase = await recordStore.LoadAsync(CancellationToken.None);
        RefreshStorageAccess();
        SetDatabase(loadedDatabase, selectedDeviceKey);
    }

    private void RefreshStorageAccess()
    {
        if (recordStoreDiagnostics is not null)
        {
            StorageAccess.Apply(recordStoreDiagnostics.LastLoadDiagnostics);
        }
    }

    private void ApplyRecord(PersistedLinkRecord updatedRecord)
    {
        if (database is null)
        {
            return;
        }

        var updatedRecords = database.Records.ToArray();
        var index = Array.FindIndex(
            updatedRecords,
            record => record.Id == updatedRecord.Id &&
                string.Equals(record.DeviceKey, updatedRecord.DeviceKey, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        updatedRecords[index] = updatedRecord;
        database = database with { Records = updatedRecords };
        ApplyFilters();
    }

    private bool CanOpenFolder(object? parameter) =>
        parameter is LinkRecordRowViewModel row && CanOpenFolder(row);

    private bool CanOpenFolder(LinkRecordRowViewModel row) =>
        !IsBusy && row.CanOperate;

    private bool CanDelete(object? parameter) =>
        parameter is LinkRecordRowViewModel row && CanDelete(row);

    private bool CanDelete(LinkRecordRowViewModel row) =>
        !IsBusy && !StorageAccess.IsReadOnly && (row.IsDeleted || row.CanOperate);

    private static IReadOnlyList<RecordDeviceOptionViewModel> BuildDeviceOptions(
        LinkDatabase database,
        DeviceIdentity currentDevice)
    {
        var devicesByKey = new Dictionary<string, DeviceIdentity>(StringComparer.Ordinal);
        foreach (var device in database.Devices)
        {
            devicesByKey[device.DeviceKey] = device;
        }

        devicesByKey[currentDevice.DeviceKey] = currentDevice;
        var options = new List<RecordDeviceOptionViewModel>
        {
            new(null, "全部设备", false, false),
            new(currentDevice.DeviceKey, $"{currentDevice.DisplayName}（当前设备）", true, false)
        };

        options.AddRange(
            devicesByKey.Values
                .Where(device => !string.Equals(device.DeviceKey, currentDevice.DeviceKey, StringComparison.Ordinal))
                .OrderBy(device => device.DisplayName, StringComparer.CurrentCulture)
                .Select(device => new RecordDeviceOptionViewModel(
                    device.DeviceKey,
                    $"{device.DisplayName}（离线设备快照）",
                    false,
                    true)));
        return options;
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        InspectCommand.RaiseCanExecuteChanged();
        ClearFiltersCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        RaiseOrganizationCommands();
    }

    private void StorageAccessOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(StorageAccessViewModel.IsReadOnly) or nameof(StorageAccessViewModel.Message))
        {
            RaiseCommandStates();
        }
    }

}
