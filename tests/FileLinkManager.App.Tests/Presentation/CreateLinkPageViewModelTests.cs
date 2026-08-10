using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.App.Presentation;
using FileLinkManager.Application.Abstractions;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Tests.Presentation;

public sealed class CreateLinkPageViewModelTests
{
    private static readonly LinkRequest Request = new(
        @"C:\Input\Original.txt",
        @"C:\Links\Original-Link.txt",
        FileLinkType.FileHardLink);

    private static readonly LinkInspection Inspection = new(
        Request.LinkPath,
        Request.Type,
        LinkStatus.Valid,
        new LinkFingerprint(
            null,
            Request.SourcePath,
            1,
            "0123456789ABCDEF0123456789ABCDEF",
            Request.LinkPath),
        null);

    [Fact]
    public void TypeCards_UseDirectoryFirstOrderAndDefaultToDirectorySymbolicLink()
    {
        var context = CreateContext();

        Assert.Equal(
            new[]
            {
                FileLinkType.DirectorySymbolicLink,
                FileLinkType.DirectoryJunction,
                FileLinkType.FileSymbolicLink,
                FileLinkType.FileHardLink
            },
            context.ViewModel.TypeCards.Select(card => card.Type));
        Assert.Equal(FileLinkType.DirectorySymbolicLink, context.ViewModel.SelectedType);
        Assert.True(context.ViewModel.TypeCards[0].IsSelected);
        Assert.All(context.ViewModel.TypeCards.Skip(1), card => Assert.False(card.IsSelected));

        var selectedCard = context.ViewModel.TypeCards.Single(card => card.Type == FileLinkType.DirectoryJunction);
        context.ViewModel.SelectTypeCommand.Execute(selectedCard);

        Assert.Equal(FileLinkType.DirectoryJunction, context.ViewModel.SelectedType);
    }

    [Fact]
    public void TypeCards_ExplainStorageImpactAndKeepLimitations()
    {
        var cards = CreateContext().ViewModel.TypeCards;

        Assert.All(cards, card =>
        {
            Assert.False(string.IsNullOrWhiteSpace(card.Description));
            Assert.False(string.IsNullOrWhiteSpace(card.Limitation));
        });

        Assert.Equal(
            "目录的跨卷等价路径；只占少量元数据，不复制内容。",
            cards.Single(card => card.Type == FileLinkType.DirectorySymbolicLink).Description);
        Assert.Equal(
            "将目录重定向到本机另一目录；只占少量元数据，不复制内容。",
            cards.Single(card => card.Type == FileLinkType.DirectoryJunction).Description);
        Assert.Equal(
            "文件的跨卷等价路径；只占少量元数据，不复制内容。",
            cards.Single(card => card.Type == FileLinkType.FileSymbolicLink).Description);
        Assert.Equal(
            "共享同一文件内容，不复制数据；仅占少量目录项/元数据，最后一个链接删除后才释放内容空间。",
            cards.Single(card => card.Type == FileLinkType.FileHardLink).Description);
    }

    [Fact]
    public void InputChanges_RefreshValidationAndPreview()
    {
        var links = new FakeFileLinkService
        {
            Validation = new LinkValidationResult(false, new[] { "The source is invalid.", "The link is invalid." })
        };
        var context = CreateContext(links);

        context.ViewModel.SourcePath = Request.SourcePath;
        context.ViewModel.LinkPath = Request.LinkPath;

        Assert.Equal(
            new[] { "请输入有效的源路径。" },
            context.ViewModel.SourceValidationErrors);
        Assert.Equal(
            new[] { "请输入有效的链接路径。" },
            context.ViewModel.LinkValidationErrors);
        Assert.Equal(
            new[] { "请输入有效的源路径。", "请输入有效的链接路径。" },
            context.ViewModel.ValidationErrors);
        Assert.Equal(
            @"C:\Links\Original-Link.txt（新链接） → C:\Input\Original.txt（现有源）",
            context.ViewModel.PreviewText);
        Assert.False(context.ViewModel.CanCreate);
        Assert.Contains(links.ValidatedRequests, request => request.SourcePath == Request.SourcePath);
    }

    [Fact]
    public void ValidationErrors_AreLocalizedAndClassifiedByPathWithoutDuplicatingValidation()
    {
        var links = new FakeFileLinkService
        {
            Validation = new LinkValidationResult(false, new[]
            {
                "Enter a valid source path.",
                "The source path does not exist.",
                "The selected link type requires a file source.",
                "Enter a valid link path.",
                "The link path already exists.",
                "The link path parent directory does not exist.",
                "File hard links must be created on the same volume.",
                "Directory junctions require local paths; network paths are not supported."
            })
        };
        var context = CreateContext(links);

        Assert.Equal(
            new[]
            {
                "请输入有效的绝对源路径。",
                "源路径不存在。",
                "当前链接类型要求源路径为文件。"
            },
            context.ViewModel.SourceValidationErrors);
        Assert.Equal(
            new[]
            {
                "请输入有效的绝对链接路径。",
                "链接路径必须不存在。",
                "链接路径的父目录不存在。"
            },
            context.ViewModel.LinkValidationErrors);
        Assert.Equal(
            new[]
            {
                "文件硬链接的源路径和链接路径必须位于同一卷。",
                "目录联接不支持网络路径，请使用本机路径。"
            },
            context.ViewModel.GeneralValidationErrors);
        Assert.All(context.ViewModel.ValidationErrors, error =>
            Assert.DoesNotContain("path", error, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectedTypeChange_RevalidatesAndMovesTypeMismatchToSourceCard()
    {
        var links = new FakeFileLinkService
        {
            Validation = new LinkValidationResult(false, new[]
            {
                "The selected link type requires a directory source."
            })
        };
        var context = CreateContext(links);

        context.ViewModel.SelectedType = FileLinkType.DirectoryJunction;

        Assert.Equal(
            new[] { "当前链接类型要求源路径为目录。" },
            context.ViewModel.SourceValidationErrors);
        Assert.Empty(context.ViewModel.LinkValidationErrors);
        Assert.Contains(
            links.ValidatedRequests,
            request => request.Type == FileLinkType.DirectoryJunction);
    }

    [Fact]
    public void RelativePaths_AreRejectedByCreatePageBeforeCreateDespiteServiceCanonicalization()
    {
        var links = new FakeFileLinkService { Validation = LinkValidationResult.Success };
        var context = CreateContext(links);

        context.ViewModel.SourcePath = "source.txt";
        context.ViewModel.LinkPath = "link.txt";

        Assert.Equal(new[] { "请输入有效的绝对源路径。" }, context.ViewModel.SourceValidationErrors);
        Assert.Equal(new[] { "请输入有效的绝对链接路径。" }, context.ViewModel.LinkValidationErrors);
        Assert.False(context.ViewModel.CanCreate);
    }

    [Fact]
    public void ValidPaths_ShowGreenStatus_AndTypeMismatchImmediatelyReplacesSourceStatus()
    {
        var links = new FakeFileLinkService { Validation = LinkValidationResult.Success };
        var context = CreateContext(links);

        context.ViewModel.SelectedType = FileLinkType.FileSymbolicLink;
        context.ViewModel.SourcePath = Request.SourcePath;
        context.ViewModel.LinkPath = Request.LinkPath;

        Assert.Equal("源路径有效。", context.ViewModel.SourceValidationStatus);
        Assert.Equal("链接路径可用。", context.ViewModel.LinkValidationStatus);
        Assert.True(context.ViewModel.CanCreate);

        links.Validation = new LinkValidationResult(false, new[]
        {
            "The selected link type requires a directory source."
        });
        context.ViewModel.SelectedType = FileLinkType.DirectoryJunction;

        Assert.Null(context.ViewModel.SourceValidationStatus);
        Assert.Equal(new[] { "当前链接类型要求源路径为目录。" }, context.ViewModel.SourceValidationErrors);
        Assert.False(context.ViewModel.CanCreate);
    }

    [Fact]
    public void CanCreate_IsOnlyEnabledForValidNonBlankRequest()
    {
        var links = new FakeFileLinkService { Validation = LinkValidationResult.Success };
        var context = CreateContext(links);

        Assert.False(context.ViewModel.CanCreate);
        context.ViewModel.SourcePath = Request.SourcePath;
        context.ViewModel.LinkPath = Request.LinkPath;

        Assert.True(context.ViewModel.CanCreate);
    }

    [Fact]
    public void StorageReadOnly_DisablesCreateAndCleanDiagnosticsRestoreIt()
    {
        var links = new FakeFileLinkService { Validation = LinkValidationResult.Success };
        var storageAccess = new StorageAccessViewModel();
        var context = CreateContext(links, storageAccess: storageAccess);
        SetValidPaths(context.ViewModel);

        storageAccess.MarkReadOnly("数据文件已从备份恢复，当前处于只读模式。");

        Assert.False(context.ViewModel.CanCreate);
        Assert.Equal("数据文件已从备份恢复，当前处于只读模式。", storageAccess.Message);

        storageAccess.Apply(LinkRecordStoreLoadDiagnostics.Clean);

        Assert.True(context.ViewModel.CanCreate);
    }

    [Fact]
    public async Task RetryRegistration_WhenStorageBecomesReadOnly_IsDisabledAndDoesNotSave()
    {
        var links = new FakeFileLinkService { Validation = LinkValidationResult.Success };
        var storageAccess = new StorageAccessViewModel();
        var store = new FakeLinkRecordStore
        {
            SaveException = new LinkRecordStoreException("data.json", "Disk full.")
        };
        var context = CreateContext(links, store, storageAccess: storageAccess);
        SetValidPaths(context.ViewModel);

        await context.ViewModel.CreateCommand.ExecuteAsync(null);

        Assert.True(context.ViewModel.CanRetryRegistration);
        var saveAttemptsBeforeReadOnly = store.SaveAttempts;
        storageAccess.MarkReadOnly("数据文件已从备份恢复，当前处于只读模式。");

        Assert.False(context.ViewModel.CanRetryRegistration);
        await context.ViewModel.RetryRegistrationCommand.ExecuteAsync(null);

        Assert.Equal(saveAttemptsBeforeReadOnly, store.SaveAttempts);
    }

    [Fact]
    public async Task CreateCommand_WhenOperationIsPending_FreezesEditablePaths()
    {
        var links = new FakeFileLinkService { Validation = LinkValidationResult.Success };
        var createCompletion = new TaskCompletionSource<LinkInspection>(TaskCreationOptions.RunContinuationsAsynchronously);
        links.PendingCreate = createCompletion;
        var context = CreateContext(links);
        SetValidPaths(context.ViewModel);

        var createTask = context.ViewModel.CreateCommand.ExecuteAsync(null);

        Assert.True(context.ViewModel.IsBusy);
        context.ViewModel.SourcePath = @"C:\Input\Changed.txt";
        context.ViewModel.LinkPath = @"C:\Links\Changed-Link.txt";

        Assert.Equal(Request.SourcePath, context.ViewModel.SourcePath);
        Assert.Equal(Request.LinkPath, context.ViewModel.LinkPath);

        createCompletion.SetResult(Inspection);
        await createTask;

        Assert.False(context.ViewModel.IsBusy);
    }

    [Fact]
    public void PickerCancellationLeavesTheExistingPathUnchanged()
    {
        var dialogs = new FakePathDialogService { NextSourcePath = Request.SourcePath };
        var context = CreateContext(dialogs: dialogs);
        context.ViewModel.SourcePath = @"C:\Input\Existing.txt";

        dialogs.NextSourcePath = null;
        context.ViewModel.BrowseSourceCommand.Execute(null);

        Assert.Equal(@"C:\Input\Existing.txt", context.ViewModel.SourcePath);
    }

    [Fact]
    public void PickerSelectionUpdatesSourcePath()
    {
        var dialogs = new FakePathDialogService { NextSourcePath = Request.SourcePath };
        var context = CreateContext(dialogs: dialogs);

        context.ViewModel.BrowseSourceCommand.Execute(null);

        Assert.Equal(Request.SourcePath, context.ViewModel.SourcePath);
    }

    [Fact]
    public async Task CreateCommand_WhenRegistrationSucceeds_ShowsSuccessAndClearsRetry()
    {
        var links = new FakeFileLinkService { Validation = LinkValidationResult.Success };
        var context = CreateContext(links);
        SetValidPaths(context.ViewModel);

        await context.ViewModel.CreateCommand.ExecuteAsync(null);

        Assert.Equal("链接已创建并登记。", context.ViewModel.ResultMessage);
        Assert.False(context.ViewModel.CanRetryRegistration);
        Assert.NotNull(context.ViewModel.CreatedRecord);
        Assert.False(context.ViewModel.IsBusy);
        Assert.False(context.ViewModel.CanCreate);

        context.ViewModel.LinkPath = @"C:\Links\Another-Link.txt";
        Assert.True(context.ViewModel.CanCreate);
    }

    [Fact]
    public async Task CreateCommand_WhenValidationChangesBeforeExecution_DisablesCreateAndShowsCurrentErrors()
    {
        var links = new FakeFileLinkService { Validation = LinkValidationResult.Success };
        var context = CreateContext(links);
        SetValidPaths(context.ViewModel);
        links.Validation = new LinkValidationResult(false, new[] { "The link already exists." });

        await context.ViewModel.CreateCommand.ExecuteAsync(null);

        Assert.False(context.ViewModel.CanCreate);
        Assert.Equal(new[] { "链接路径必须不存在。" }, context.ViewModel.LinkValidationErrors);
        Assert.Equal("请先修正路径校验错误。", context.ViewModel.ResultMessage);
    }

    [Fact]
    public async Task CreateCommand_WhenRegistrationFails_PreservesPathsAndEnablesRetry()
    {
        var links = new FakeFileLinkService { Validation = LinkValidationResult.Success };
        var store = new FakeLinkRecordStore
        {
            SaveException = new LinkRecordStoreException("data.json", "Disk full.")
        };
        var context = CreateContext(links, store);
        SetValidPaths(context.ViewModel);

        await context.ViewModel.CreateCommand.ExecuteAsync(null);

        Assert.True(context.ViewModel.CanRetryRegistration);
        Assert.Contains("Disk full", context.ViewModel.ResultMessage, StringComparison.Ordinal);
        Assert.Equal(Request.SourcePath, context.ViewModel.SourcePath);
        Assert.Equal(Request.LinkPath, context.ViewModel.LinkPath);

        store.SaveException = null;
        await context.ViewModel.RetryRegistrationCommand.ExecuteAsync(null);

        Assert.False(context.ViewModel.CanRetryRegistration);
        Assert.Equal("链接登记已重试成功。", context.ViewModel.ResultMessage);
    }

    private static void SetValidPaths(CreateLinkPageViewModel viewModel)
    {
        viewModel.SelectedType = Request.Type;
        viewModel.SourcePath = Request.SourcePath;
        viewModel.LinkPath = Request.LinkPath;
    }

    private static TestContext CreateContext(
        FakeFileLinkService? links = null,
        FakeLinkRecordStore? store = null,
        FakePathDialogService? dialogs = null,
        StorageAccessViewModel? storageAccess = null)
    {
        links ??= new FakeFileLinkService();
        store ??= new FakeLinkRecordStore();
        dialogs ??= new FakePathDialogService();
        var device = new FakeDeviceIdentityProvider(new DeviceIdentity("device-current", "Current Device"));
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));
        var viewModel = new CreateLinkPageViewModel(
            links,
            new CreateLinkUseCase(links, store, device, time),
            new RetryRegistrationUseCase(store, device, time),
            dialogs,
            storageAccess: storageAccess);
        return new TestContext(viewModel, links, store, dialogs);
    }

    private sealed record TestContext(
        CreateLinkPageViewModel ViewModel,
        FakeFileLinkService Links,
        FakeLinkRecordStore Store,
        FakePathDialogService Dialogs);

    private sealed class FakeFileLinkService : IFileLinkService
    {
        public LinkValidationResult Validation { get; set; } = LinkValidationResult.Success;

        public TaskCompletionSource<LinkInspection>? PendingCreate { get; set; }

        public List<LinkRequest> ValidatedRequests { get; } = new();

        public LinkValidationResult Validate(LinkRequest request)
        {
            ValidatedRequests.Add(request);
            return Validation;
        }

        public Task<LinkInspection> CreateAsync(LinkRequest request, CancellationToken cancellationToken) =>
            PendingCreate?.Task ?? Task.FromResult(Inspection);

        public Task<LinkInspection> InspectAsync(string linkPath, CancellationToken cancellationToken) =>
            Task.FromResult(Inspection);

        public Task DeleteAsync(LinkRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeLinkRecordStore : ILinkRecordStore
    {
        public LinkDatabase Database { get; set; } = LinkDatabase.Empty();

        public Exception? SaveException { get; set; }

        public int SaveAttempts { get; private set; }

        public Task<LinkDatabase> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Database);

        public Task SaveAsync(LinkDatabase database, CancellationToken cancellationToken)
        {
            SaveAttempts++;
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            Database = database;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeviceIdentityProvider(DeviceIdentity identity) : IDeviceIdentityProvider
    {
        public DeviceIdentity GetCurrent() => identity;
    }

    private sealed class FakePathDialogService : IPathDialogService
    {
        public string? NextSourcePath { get; set; }

        public string? NextLinkParentPath { get; set; }

        public string? PickSourcePath(FileLinkType type, string currentPath) => NextSourcePath;

        public string? PickLinkParentPath(string currentPath) => NextLinkParentPath;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
