using FileLinkManager.App.Presentation;
using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.Application.Abstractions;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Tests.Presentation;

public sealed class LinkRecordsPageViewModelTests
{
    private static readonly DeviceIdentity CurrentDevice = new("device-current", "Current Device");
    private static readonly DeviceIdentity OfflineDevice = new("device-offline", "Offline Device");
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Refresh_DefaultsToCurrentDeviceAndMarksOtherDeviceRowsAsOfflineSnapshots()
    {
        var current = CreateRecord("11111111-1111-1111-1111-111111111111", CurrentDevice, "current.txt", LinkStatus.Valid);
        var offline = CreateRecord("22222222-2222-2222-2222-222222222222", OfflineDevice, "offline.txt", LinkStatus.Valid);
        var context = CreateContext(DatabaseWith(current, offline));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(CurrentDevice.DeviceKey, context.ViewModel.SelectedDevice!.DeviceKey);
        Assert.Equal(new LinkStatistics(1, 1), context.ViewModel.Statistics);
        Assert.Single(context.ViewModel.Records);
        Assert.Equal(current.Id, context.ViewModel.Records[0].Id);

        context.ViewModel.SelectedDevice = context.ViewModel.Devices.Single(device => device.DeviceKey is null);

        Assert.Equal(2, context.ViewModel.Records.Count);
        Assert.Contains(context.ViewModel.Records, row => row.Id == offline.Id && row.IsOfflineSnapshot);
        Assert.DoesNotContain(offline.LinkRecord.Request.LinkPath, context.Links.InspectedPaths);
    }

    [Fact]
    public async Task Refresh_AppliesReadOnlyStoreDiagnosticsToSharedStorageAccess()
    {
        var record = CreateRecord("12121212-1212-1212-1212-121212121212", CurrentDevice, "recovered.txt", LinkStatus.Valid);
        var storageAccess = new StorageAccessViewModel();
        var context = CreateContext(DatabaseWith(record), storageAccess);
        context.Store.LastLoadDiagnostics = new LinkRecordStoreLoadDiagnostics(
            RecoveredFromBackup: true,
            IsReadOnly: true,
            Message: "主文件损坏，已载入备份并保持只读。");

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(storageAccess.IsReadOnly);
        Assert.Equal("主文件损坏，已载入备份并保持只读。", storageAccess.Message);
    }

    [Fact]
    public async Task Refresh_WhenStorageIsReadOnly_LoadsRecordsWithoutInspectionOrSave()
    {
        var record = CreateRecord("13131313-1313-1313-1313-131313131313", CurrentDevice, "read-only.txt", LinkStatus.Valid);
        var context = CreateContext(DatabaseWith(record));
        context.Store.LastLoadDiagnostics = new LinkRecordStoreLoadDiagnostics(
            RecoveredFromBackup: true,
            IsReadOnly: true,
            Message: "主文件损坏，已载入备份并保持只读。");

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(context.ViewModel.StorageAccess.IsReadOnly);
        Assert.Empty(context.Links.InspectedPaths);
        Assert.Equal(0, context.Store.SaveCount);
        Assert.Contains("只读", context.ViewModel.ResultMessage);
    }

    [Fact]
    public async Task DeleteCommand_WhenStorageIsReadOnly_IsDisabledAndDoesNotDelete()
    {
        var record = CreateRecord("14141414-1414-1414-1414-141414141414", CurrentDevice, "read-only-delete.txt", LinkStatus.Valid);
        var context = CreateContext(DatabaseWith(record));
        context.Store.LastLoadDiagnostics = new LinkRecordStoreLoadDiagnostics(
            RecoveredFromBackup: true,
            IsReadOnly: true,
            Message: "主文件损坏，已载入备份并保持只读。");

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        var row = Assert.Single(context.ViewModel.Records);

        Assert.False(context.ViewModel.DeleteCommand.CanExecute(row));
        await context.ViewModel.DeleteCommand.ExecuteAsync(row);

        Assert.False(context.Links.DeleteCalled);
        Assert.False(context.Dialogs.ConfirmDeleteCalled);
    }

    [Fact]
    public async Task DeleteCommand_WhenRecordIsDeleted_ConfirmsAndRemovesHistoryWithoutNativeCalls()
    {
        var deleted = CreateRecord(
            "17171717-1717-1717-1717-171717171717",
            CurrentDevice,
            "deleted-history.txt",
            LinkStatus.Deleted);
        var remaining = CreateRecord(
            "18181818-1818-1818-1818-181818181818",
            CurrentDevice,
            "remaining-history.txt",
            LinkStatus.Deleted);
        var context = CreateContext(DatabaseWith(deleted, remaining));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        context.ViewModel.SelectedDevice = context.ViewModel.Devices.Single(device => device.DeviceKey is null);
        context.Links.InspectedPaths.Clear();
        var saveCountBeforeRemoval = context.Store.SaveCount;
        await context.ViewModel.DeleteCommand.ExecuteAsync(
            context.ViewModel.Records.Single(row => row.Id == deleted.Id));

        Assert.DoesNotContain(context.ViewModel.Records, row => row.Id == deleted.Id);
        Assert.Contains(context.ViewModel.Records, row => row.Id == remaining.Id);
        Assert.Equal(new LinkStatistics(1, 0), context.ViewModel.Statistics);
        Assert.True(context.Dialogs.ConfirmRemoveDeletedRecordCalled);
        Assert.False(context.Dialogs.ConfirmDeleteCalled);
        Assert.Empty(context.Links.InspectedPaths);
        Assert.False(context.Links.DeleteCalled);
        Assert.Equal(saveCountBeforeRemoval + 1, context.Store.SaveCount);
        Assert.DoesNotContain(context.Store.Database.Records, row => row.Id == deleted.Id);
        Assert.Equal("删除记录", context.ViewModel.Records.Single().DeleteActionDisplayName);
        Assert.Contains("历史记录", context.ViewModel.ResultMessage, StringComparison.Ordinal);
        Assert.Contains("不会再操作磁盘", context.ViewModel.ResultMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteCommand_WhenDeletedRecordConfirmationIsCancelled_LeavesHistoryAndDoesNotCallNative()
    {
        var deleted = CreateRecord(
            "19191919-1919-1919-1919-191919191919",
            CurrentDevice,
            "cancel-history.txt",
            LinkStatus.Deleted);
        var context = CreateContext(DatabaseWith(deleted));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        var saveCountBeforeRemoval = context.Store.SaveCount;
        context.Links.InspectedPaths.Clear();
        context.Dialogs.ConfirmRemoveDeletedRecordResult = false;

        await context.ViewModel.DeleteCommand.ExecuteAsync(Assert.Single(context.ViewModel.Records));

        Assert.Single(context.ViewModel.Records);
        Assert.Equal(saveCountBeforeRemoval, context.Store.SaveCount);
        Assert.Empty(context.Links.InspectedPaths);
        Assert.False(context.Links.DeleteCalled);
        Assert.Contains("取消", context.ViewModel.ResultMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteCommand_WhenDeletedRecordIsShownForOfflineDevice_RemovesOnlyThatHistory()
    {
        var current = CreateRecord(
            "20202020-2020-2020-2020-202020202020",
            CurrentDevice,
            "current-history.txt",
            LinkStatus.Deleted);
        var offline = CreateRecord(
            "21212121-2121-2121-2121-212121212121",
            OfflineDevice,
            "offline-history.txt",
            LinkStatus.Deleted);
        var context = CreateContext(DatabaseWith(current, offline));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        context.ViewModel.SelectedDevice = context.ViewModel.Devices.Single(
            device => device.DeviceKey == OfflineDevice.DeviceKey);
        context.Links.InspectedPaths.Clear();

        await context.ViewModel.DeleteCommand.ExecuteAsync(Assert.Single(context.ViewModel.Records));

        Assert.Empty(context.ViewModel.Records);
        Assert.Contains(context.Store.Database.Records, row => row.Id == current.Id);
        Assert.DoesNotContain(context.Store.Database.Records, row => row.Id == offline.Id);
        Assert.Empty(context.Links.InspectedPaths);
        Assert.False(context.Links.DeleteCalled);
        Assert.Equal(new LinkStatistics(0, 0), context.ViewModel.Statistics);
    }

    [Fact]
    public async Task DeleteCommand_WhenDeletedRecordIsReadOnly_IsDisabled()
    {
        var deleted = CreateRecord(
            "22222222-2222-2222-2222-222222222222",
            CurrentDevice,
            "read-only-history.txt",
            LinkStatus.Deleted);
        var storageAccess = new StorageAccessViewModel();
        var context = CreateContext(DatabaseWith(deleted), storageAccess);
        context.Store.LastLoadDiagnostics = new LinkRecordStoreLoadDiagnostics(
            RecoveredFromBackup: true,
            IsReadOnly: true,
            Message: "记录存储已切换为只读。");

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        var row = Assert.Single(context.ViewModel.Records);

        Assert.False(context.ViewModel.DeleteCommand.CanExecute(row));
        await context.ViewModel.DeleteCommand.ExecuteAsync(row);

        Assert.False(context.Dialogs.ConfirmRemoveDeletedRecordCalled);
        Assert.Contains(context.Store.Database.Records, record => record.Id == deleted.Id);
    }

    [Fact]
    public async Task DeleteCommand_WhenDeletedRecordSaveFails_LeavesHistoryAndRefreshesReadOnlyState()
    {
        var deleted = CreateRecord(
            "23232323-2323-2323-2323-232323232323",
            CurrentDevice,
            "save-fails-history.txt",
            LinkStatus.Deleted);
        var context = CreateContext(DatabaseWith(deleted));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        context.Links.InspectedPaths.Clear();
        context.Store.FailNextSaveAsReadOnly = true;

        await context.ViewModel.DeleteCommand.ExecuteAsync(Assert.Single(context.ViewModel.Records));

        Assert.True(context.ViewModel.StorageAccess.IsReadOnly);
        Assert.Contains(context.Store.Database.Records, record => record.Id == deleted.Id);
        Assert.Empty(context.Links.InspectedPaths);
        Assert.False(context.Links.DeleteCalled);
        Assert.Contains("删除历史记录失败", context.ViewModel.ResultMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletedUtcLegacyRecord_IsShownAsDeletedAndMatchesDeletedFilter()
    {
        var legacy = CreateRecord(
            "24242424-2424-2424-2424-242424242424",
            CurrentDevice,
            "legacy-deleted.txt",
            LinkStatus.Valid) with
        {
            DeletedUtc = Timestamp
        };
        var context = CreateContext(DatabaseWith(legacy));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);

        var row = Assert.Single(context.ViewModel.Records);
        Assert.True(row.IsDeleted);
        Assert.Equal("已删除", row.StatusDisplayName);
        Assert.Equal("删除记录", row.DeleteActionDisplayName);

        context.ViewModel.SelectedStatus = context.ViewModel.StatusOptions.Single(
            option => option.Status == LinkStatus.Valid);

        Assert.Empty(context.ViewModel.Records);

        context.ViewModel.SelectedStatus = context.ViewModel.StatusOptions.Single(
            option => option.Status == LinkStatus.Deleted);

        Assert.Single(context.ViewModel.Records);
    }

    [Fact]
    public async Task Filters_UseCombinedPathSearchAndStatusWithinSelectedDevice()
    {
        var sourceHit = CreateRecord("33333333-3333-3333-3333-333333333333", CurrentDevice, "archive-source.txt", LinkStatus.Valid);
        var linkHit = CreateRecord(
            "44444444-4444-4444-4444-444444444444",
            CurrentDevice,
            "other.txt",
            LinkStatus.Missing,
            linkName: "archive-link.txt");
        var unrelated = CreateRecord("55555555-5555-5555-5555-555555555555", CurrentDevice, "unrelated.txt", LinkStatus.Conflict);
        var context = CreateContext(DatabaseWith(sourceHit, linkHit, unrelated));
        context.Links.Inspections[linkHit.LinkRecord.Request.LinkPath] = new LinkInspection(
            linkHit.LinkRecord.Request.LinkPath,
            null,
            LinkStatus.Missing,
            null,
            "The link is missing.");

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        context.ViewModel.SelectedDevice = context.ViewModel.Devices.Single(device => device.DeviceKey is null);
        context.ViewModel.SearchText = "ARCHIVE";

        Assert.Equal(new[] { sourceHit.Id, linkHit.Id }, context.ViewModel.Records.Select(row => row.Id));

        context.ViewModel.SelectedStatus = context.ViewModel.StatusOptions.Single(option => option.Status == LinkStatus.Missing);

        Assert.Equal(new[] { linkHit.Id }, context.ViewModel.Records.Select(row => row.Id));
    }

    [Fact]
    public async Task Refresh_WhenOfflineDeviceIsSelected_DoesNotInspectThatDevice()
    {
        var current = CreateRecord("66666666-6666-6666-6666-666666666666", CurrentDevice, "current.txt", LinkStatus.Valid);
        var offline = CreateRecord("77777777-7777-7777-7777-777777777777", OfflineDevice, "offline.txt", LinkStatus.Valid);
        var context = CreateContext(DatabaseWith(current, offline));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        context.Links.InspectedPaths.Clear();
        context.ViewModel.SelectedDevice = context.ViewModel.Devices.Single(device => device.DeviceKey == OfflineDevice.DeviceKey);

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(context.Links.InspectedPaths);
        Assert.All(context.ViewModel.Records, row => Assert.True(row.IsOfflineSnapshot));
    }

    [Fact]
    public async Task EnterPageAsync_InspectsCurrentDeviceEvenWhenOfflineFilterIsSelected()
    {
        var current = CreateRecord("dddddddd-dddd-dddd-dddd-dddddddddddd", CurrentDevice, "current.txt", LinkStatus.Valid);
        var offline = CreateRecord("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", OfflineDevice, "offline.txt", LinkStatus.Valid);
        var context = CreateContext(DatabaseWith(current, offline));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        context.Links.InspectedPaths.Clear();
        context.ViewModel.SelectedDevice = context.ViewModel.Devices.Single(device => device.DeviceKey == OfflineDevice.DeviceKey);

        await context.ViewModel.EnterPageAsync();

        Assert.Contains(current.LinkRecord.Request.LinkPath, context.Links.InspectedPaths);
        Assert.DoesNotContain(offline.LinkRecord.Request.LinkPath, context.Links.InspectedPaths);
        Assert.Equal(OfflineDevice.DeviceKey, context.ViewModel.SelectedDevice!.DeviceKey);
    }

    [Fact]
    public async Task DeleteCommand_WhenConfirmedMarksRecordDeletedAndPreservesHistory()
    {
        var record = CreateRecord("88888888-8888-8888-8888-888888888888", CurrentDevice, "delete-me.txt", LinkStatus.Valid);
        var context = CreateContext(DatabaseWith(record));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        var activeRow = Assert.Single(context.ViewModel.Records);
        Assert.Equal("删除链接", activeRow.DeleteActionDisplayName);
        await context.ViewModel.DeleteCommand.ExecuteAsync(activeRow);

        var row = Assert.Single(context.ViewModel.Records);
        Assert.Equal("删除记录", row.DeleteActionDisplayName);
        Assert.Equal(LinkStatus.Deleted, row.Record.Status);
        Assert.Equal(new LinkStatistics(1, 0), context.ViewModel.Statistics);
        Assert.True(context.Dialogs.ConfirmDeleteCalled);
        Assert.True(context.Links.DeleteCalled);
    }

    [Fact]
    public async Task DeleteCommand_WhenMissingConfirmationIsCancelled_LeavesRecordUndeleted()
    {
        var record = CreateRecord("99999999-9999-9999-9999-999999999999", CurrentDevice, "missing.txt", LinkStatus.Missing);
        var context = CreateContext(DatabaseWith(record));
        context.Links.Inspections[record.LinkRecord.Request.LinkPath] = new LinkInspection(
            record.LinkRecord.Request.LinkPath,
            null,
            LinkStatus.Missing,
            null,
            "The link is missing.");
        context.Dialogs.ConfirmDeleteMissingResult = false;

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        await context.ViewModel.DeleteCommand.ExecuteAsync(context.ViewModel.Records.Single());

        Assert.Equal(LinkStatus.Missing, Assert.Single(context.ViewModel.Records).Record.Status);
        Assert.False(context.Links.DeleteCalled);
        Assert.True(context.Dialogs.ConfirmDeleteMissingCalled);
    }

    [Fact]
    public async Task DeleteCommand_WhenRegistrationSaveFails_RefreshesSharedReadOnlyState()
    {
        var deleted = CreateRecord("15151515-1515-1515-1515-151515151515", CurrentDevice, "write-fails.txt", LinkStatus.Valid);
        var remaining = CreateRecord("16161616-1616-1616-1616-161616161616", CurrentDevice, "remains.txt", LinkStatus.Valid);
        var context = CreateContext(DatabaseWith(deleted, remaining));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        context.Store.FailNextSaveAsReadOnly = true;
        var deletedRow = context.ViewModel.Records.Single(row => row.Id == deleted.Id);

        await context.ViewModel.DeleteCommand.ExecuteAsync(deletedRow);

        Assert.True(context.Links.DeleteCalled);
        Assert.True(context.ViewModel.StorageAccess.IsReadOnly);
        Assert.False(context.ViewModel.InspectCommand.CanExecute(null));
        Assert.False(context.ViewModel.DeleteCommand.CanExecute(
            context.ViewModel.Records.Single(row => row.Id == remaining.Id)));
    }

    [Fact]
    public async Task Commands_WhenRefreshIsPending_AreDisabledAndInputStateRemainsBusy()
    {
        var record = CreateRecord("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", CurrentDevice, "pending.txt", LinkStatus.Valid);
        var context = CreateContext(DatabaseWith(record));
        var inspectionCompletion = new TaskCompletionSource<LinkInspection>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Links.PendingInspection = inspectionCompletion;

        var refreshTask = context.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(context.ViewModel.IsBusy);
        Assert.False(context.ViewModel.RefreshCommand.CanExecute(null));
        Assert.False(context.ViewModel.DeleteCommand.CanExecute(null));

        inspectionCompletion.SetResult(MatchingInspection(record));
        await refreshTask;

        Assert.False(context.ViewModel.IsBusy);
    }

    [Fact]
    public async Task OpenFolderCommand_DelegatesCurrentDevicePathAndRejectsOfflineRows()
    {
        var current = CreateRecord("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", CurrentDevice, "current.txt", LinkStatus.Valid);
        var offline = CreateRecord("cccccccc-cccc-cccc-cccc-cccccccccccc", OfflineDevice, "offline.txt", LinkStatus.Valid);
        var context = CreateContext(DatabaseWith(current, offline));

        await context.ViewModel.RefreshCommand.ExecuteAsync(null);
        context.ViewModel.SelectedDevice = context.ViewModel.Devices.Single(device => device.DeviceKey is null);

        var currentRow = context.ViewModel.Records.Single(row => row.Id == current.Id);
        var offlineRow = context.ViewModel.Records.Single(row => row.Id == offline.Id);
        context.ViewModel.OpenFolderCommand.Execute(currentRow);
        context.ViewModel.OpenFolderCommand.Execute(offlineRow);

        Assert.Equal(new[] { current.LinkRecord.Request.LinkPath }, context.Dialogs.OpenedPaths);
    }

    private static TestContext CreateContext(LinkDatabase database, StorageAccessViewModel? storageAccess = null)
    {
        var links = new FakeFileLinkService();
        foreach (var record in database.Records)
        {
            links.Inspections[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
            links.Inspections[record.LinkRecord.Request.SourcePath] = new LinkInspection(
                record.LinkRecord.Request.SourcePath,
                record.LinkRecord.Request.Type,
                LinkStatus.Valid,
                record.LinkRecord.CreationFingerprint,
                null);
        }

        var store = new FakeLinkRecordStore { Database = database };
        var device = new FakeDeviceIdentityProvider(CurrentDevice);
        var dialogs = new FakeRecordPageDialogService();
        var inspect = new InspectLinksUseCase(links, store, device, new FixedTimeProvider(Timestamp));
        var delete = new DeleteLinksUseCase(
            links,
            store,
            device,
            new FixedTimeProvider(Timestamp),
            _ => true);
        var viewModel = new LinkRecordsPageViewModel(
            store,
            device,
            new LinkRecordQueryService(),
            inspect,
            delete,
            dialogs,
            storageAccess: storageAccess,
            recordStoreDiagnostics: store);
        return new TestContext(viewModel, links, store, dialogs);
    }

    private static LinkDatabase DatabaseWith(params PersistedLinkRecord[] records) => new(
        LinkDatabase.CurrentSchemaVersion,
        new[] { CurrentDevice, OfflineDevice },
        records);

    private static PersistedLinkRecord CreateRecord(
        string id,
        DeviceIdentity device,
        string name,
        LinkStatus status,
        string? linkName = null)
    {
        linkName ??= name;
        var request = new LinkRequest(
            $@"C:\Sources\{name}",
            $@"C:\Links\{device.DeviceKey}\{linkName}",
            FileLinkType.FileHardLink);
        var fingerprint = new LinkFingerprint(null, request.SourcePath, 1, id.Replace("-", string.Empty), request.LinkPath);
        return new PersistedLinkRecord(
            Guid.Parse(id),
            device.DeviceKey,
            device.DisplayName,
            new LinkRecord(request, fingerprint),
            Timestamp,
            Timestamp,
            status == LinkStatus.Deleted ? Timestamp : null,
            status,
            status == LinkStatus.Valid ? null : "Stored status reason.");
    }

    private static LinkInspection MatchingInspection(PersistedLinkRecord record) => new(
        record.LinkRecord.Request.LinkPath,
        record.LinkRecord.Request.Type,
        LinkStatus.Valid,
        record.LinkRecord.CreationFingerprint,
        null);

    private sealed record TestContext(
        LinkRecordsPageViewModel ViewModel,
        FakeFileLinkService Links,
        FakeLinkRecordStore Store,
        FakeRecordPageDialogService Dialogs);

    private sealed class FakeFileLinkService : IFileLinkService
    {
        public Dictionary<string, LinkInspection> Inspections { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> InspectedPaths { get; } = new();

        public TaskCompletionSource<LinkInspection>? PendingInspection { get; set; }

        public bool DeleteCalled { get; private set; }

        public LinkValidationResult Validate(LinkRequest request) => LinkValidationResult.Success;

        public Task<LinkInspection> CreateAsync(LinkRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(MatchingInspection(new PersistedLinkRecord(
                Guid.Empty,
                CurrentDevice.DeviceKey,
                CurrentDevice.DisplayName,
                new LinkRecord(request, new LinkFingerprint(null, request.SourcePath, 1, "CREATE", request.LinkPath)),
                Timestamp,
                Timestamp,
                null,
                LinkStatus.Valid,
                null)));

        public Task<LinkInspection> InspectAsync(string linkPath, CancellationToken cancellationToken)
        {
            InspectedPaths.Add(linkPath);
            if (PendingInspection is not null)
            {
                return PendingInspection.Task;
            }

            return Task.FromResult(Inspections[linkPath]);
        }

        public Task DeleteAsync(LinkRecord record, CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLinkRecordStore : ILinkRecordStore, ILinkRecordStoreDiagnostics
    {
        public LinkDatabase Database { get; set; } = LinkDatabase.Empty();

        public LinkRecordStoreLoadDiagnostics LastLoadDiagnostics { get; set; } = LinkRecordStoreLoadDiagnostics.Clean;

        public int SaveCount { get; private set; }

        public bool FailNextSaveAsReadOnly { get; set; }

        public Task<LinkDatabase> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Database);

        public Task SaveAsync(LinkDatabase database, CancellationToken cancellationToken)
        {
            SaveCount++;
            if (FailNextSaveAsReadOnly)
            {
                FailNextSaveAsReadOnly = false;
                LastLoadDiagnostics = new LinkRecordStoreLoadDiagnostics(
                    RecoveredFromBackup: false,
                    IsReadOnly: true,
                    Message: "写入失败，记录存储已切换为只读模式。");
                throw new LinkRecordStoreException("data.json", LastLoadDiagnostics.Message!);
            }

            Database = database;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeviceIdentityProvider(DeviceIdentity identity) : IDeviceIdentityProvider
    {
        public DeviceIdentity GetCurrent() => identity;
    }

    private sealed class FakeRecordPageDialogService : IRecordPageDialogService
    {
        public bool ConfirmDeleteResult { get; set; } = true;

        public bool ConfirmDeleteMissingResult { get; set; } = true;

        public bool ConfirmRemoveDeletedRecordResult { get; set; } = true;

        public bool ConfirmDeleteCalled { get; private set; }

        public bool ConfirmDeleteMissingCalled { get; private set; }

        public bool ConfirmRemoveDeletedRecordCalled { get; private set; }

        public List<string> OpenedPaths { get; } = new();

        public bool ConfirmDelete(PersistedLinkRecord record)
        {
            ConfirmDeleteCalled = true;
            return ConfirmDeleteResult;
        }

        public bool ConfirmDeleteMissing(PersistedLinkRecord record)
        {
            ConfirmDeleteMissingCalled = true;
            return ConfirmDeleteMissingResult;
        }

        public bool ConfirmRemoveDeletedRecord(PersistedLinkRecord record)
        {
            ConfirmRemoveDeletedRecordCalled = true;
            return ConfirmRemoveDeletedRecordResult;
        }

        public void OpenContainingFolder(string linkPath) => OpenedPaths.Add(linkPath);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
