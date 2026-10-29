using System.ComponentModel;
using System.IO;
using FileLinkManager.App.Presentation.Commands;
using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.Application.Abstractions;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Presentation;

public sealed record LinkTypeCardViewModel(
    FileLinkType Type,
    string Title,
    string Description,
    string Limitation,
    bool IsSelected = false);

public sealed partial class CreateLinkPageViewModel : ObservableObject
{
    private readonly IFileLinkService fileLinkService;
    private readonly CreateLinkUseCase createLinkUseCase;
    private readonly RetryRegistrationUseCase retryRegistrationUseCase;
    private readonly IPathDialogService pathDialogService;
    private readonly ILinkRecordStoreDiagnostics? recordStoreDiagnostics;
    private readonly StorageAccessViewModel storageAccess;
    private IReadOnlyList<string> sourceValidationErrors = Array.Empty<string>();
    private IReadOnlyList<string> linkValidationErrors = Array.Empty<string>();
    private IReadOnlyList<string> generalValidationErrors = Array.Empty<string>();
    private string? sourceValidationStatus;
    private string? linkValidationStatus;
    private FileLinkType selectedType;
    private string sourcePath = string.Empty;
    private string linkPath = string.Empty;
    private string previewText = string.Empty;
    private string? resultMessage;
    private bool isBusy;
    private bool linkCreated;
    private bool validationIsValid;
    private LinkRequest? pendingRequest;
    private LinkInspection? pendingInspection;
    private PersistedLinkRecord? createdRecord;

    public CreateLinkPageViewModel(
        IFileLinkService fileLinkService,
        CreateLinkUseCase createLinkUseCase,
        RetryRegistrationUseCase retryRegistrationUseCase,
        IPathDialogService pathDialogService,
        SynchronizationContext? synchronizationContext = null,
        StorageAccessViewModel? storageAccess = null,
        ILinkRecordStoreDiagnostics? recordStoreDiagnostics = null,
        ILinkRecordStore? recordStore = null,
        IOrganizationDialogService? organizationDialogs = null,
        LinkRecordOrganizationService? organizationService = null)
    {
        this.fileLinkService = fileLinkService ?? throw new ArgumentNullException(nameof(fileLinkService));
        this.createLinkUseCase = createLinkUseCase ?? throw new ArgumentNullException(nameof(createLinkUseCase));
        this.retryRegistrationUseCase = retryRegistrationUseCase ?? throw new ArgumentNullException(nameof(retryRegistrationUseCase));
        this.pathDialogService = pathDialogService ?? throw new ArgumentNullException(nameof(pathDialogService));
        this.storageAccess = storageAccess ?? new StorageAccessViewModel();
        this.recordStoreDiagnostics = recordStoreDiagnostics;
        this.storageAccess.PropertyChanged += StorageAccessOnPropertyChanged;
        _ = synchronizationContext;

        selectedType = FileLinkType.DirectorySymbolicLink;
        TypeCards = CreateTypeCards();

        SelectTypeCommand = new RelayCommand(
            parameter => SelectType(parameter),
            _ => !IsBusy);
        BrowseSourceCommand = new RelayCommand(
            _ => BrowseSource(),
            _ => !IsBusy);
        BrowseLinkLocationCommand = new RelayCommand(
            _ => BrowseLinkLocation(),
            _ => !IsBusy);
        CreateCommand = new AsyncRelayCommand(
            _ => ExecuteCreateAsync(),
            _ => CanCreate);
        RetryRegistrationCommand = new AsyncRelayCommand(
            _ => ExecuteRetryRegistrationAsync(),
            _ => CanRetryRegistration);

        InitializeOrganization(recordStore, organizationDialogs, organizationService);

        RefreshValidation();
    }

    public IReadOnlyList<LinkTypeCardViewModel> TypeCards { get; private set; }

    public FileLinkType SelectedType
    {
        get => selectedType;
        set
        {
            if (IsBusy)
            {
                return;
            }

            if (selectedType == value)
            {
                return;
            }

            selectedType = value;
            ClearResultForEdit();
            UpdateTypeCardSelection();
            OnPropertyChanged();
            RefreshValidation();
        }
    }

    public string SourcePath
    {
        get => sourcePath;
        set
        {
            if (IsBusy)
            {
                return;
            }

            var next = value ?? string.Empty;
            if (string.Equals(sourcePath, next, StringComparison.Ordinal))
            {
                return;
            }

            sourcePath = next;
            ClearResultForEdit();
            OnPropertyChanged();
            RefreshValidation();
        }
    }

    public string LinkPath
    {
        get => linkPath;
        set
        {
            if (IsBusy)
            {
                return;
            }

            var next = value ?? string.Empty;
            if (string.Equals(linkPath, next, StringComparison.Ordinal))
            {
                return;
            }

            linkPath = next;
            ClearResultForEdit();
            OnPropertyChanged();
            RefreshValidation();
        }
    }

    public IReadOnlyList<string> SourceValidationErrors => sourceValidationErrors;

    public IReadOnlyList<string> LinkValidationErrors => linkValidationErrors;

    public IReadOnlyList<string> GeneralValidationErrors => generalValidationErrors;

    public string? SourceValidationStatus => sourceValidationStatus;

    public string? LinkValidationStatus => linkValidationStatus;

    public IReadOnlyList<string> ValidationErrors => sourceValidationErrors
        .Concat(linkValidationErrors)
        .Concat(generalValidationErrors)
        .ToArray();

    public string PreviewText => previewText;

    public string? ResultMessage
    {
        get => resultMessage;
        private set => SetField(ref resultMessage, value);
    }

    public StorageAccessViewModel StorageAccess => storageAccess;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetField(ref isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanCreate));
            OnPropertyChanged(nameof(CanRetryRegistration));
            MetadataEditor.IsEnabled = !value && !StorageAccess.IsReadOnly;
            RaiseCommandStates();
        }
    }

    public bool CanCreate =>
        !IsBusy &&
        !CanRetryRegistration &&
        !linkCreated &&
        !StorageAccess.IsReadOnly &&
        validationIsValid &&
        MetadataEditor.CanSave &&
        !string.IsNullOrWhiteSpace(SourcePath) &&
        !string.IsNullOrWhiteSpace(LinkPath);

    public bool CanRetryRegistration =>
        !IsBusy &&
        !StorageAccess.IsReadOnly &&
        pendingRequest is not null &&
        pendingInspection is not null;

    public PersistedLinkRecord? CreatedRecord
    {
        get => createdRecord;
        private set => SetField(ref createdRecord, value);
    }

    public RelayCommand SelectTypeCommand { get; }

    public RelayCommand BrowseSourceCommand { get; }

    public RelayCommand BrowseLinkLocationCommand { get; }

    public AsyncRelayCommand CreateCommand { get; }

    public AsyncRelayCommand RetryRegistrationCommand { get; }

    private void SelectType(object? parameter)
    {
        var type = parameter switch
        {
            LinkTypeCardViewModel card => card.Type,
            FileLinkType value => value,
            _ => (FileLinkType?)null
        };
        if (type.HasValue)
        {
            SelectedType = type.Value;
        }
    }

    private void BrowseSource()
    {
        var selectedPath = pathDialogService.PickSourcePath(SelectedType, SourcePath);
        if (selectedPath is not null)
        {
            SourcePath = selectedPath;
        }
    }

    private void BrowseLinkLocation()
    {
        var parentPath = pathDialogService.PickLinkParentPath(LinkPath);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return;
        }

        var linkName = GetLinkName();
        LinkPath = Path.Combine(parentPath, linkName);
    }

    private async Task ExecuteCreateAsync()
    {
        if (!CanCreate)
        {
            return;
        }

        IsBusy = true;
        ResultMessage = null;
        CreatedRecord = null;
        pendingRequest = null;
        pendingInspection = null;
        OnPropertyChanged(nameof(CanRetryRegistration));
        OnPropertyChanged(nameof(CanCreate));

        try
        {
            var outcome = await createLinkUseCase.ExecuteAsync(BuildRequest(), MetadataEditor.ToMetadata(), CancellationToken.None);
            ApplyCreateOutcome(outcome);
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "创建操作已取消。";
        }
        catch (Exception exception)
        {
            ResultMessage = $"创建失败：{exception.Message}";
        }
        finally
        {
            RefreshStorageAccess();
            IsBusy = false;
        }
    }

    private async Task ExecuteRetryRegistrationAsync()
    {
        if (!CanRetryRegistration)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var outcome = await retryRegistrationUseCase.ExecuteAsync(
                pendingRequest!,
                pendingInspection!,
                pendingMetadata,
                CancellationToken.None);
            ApplyRetryOutcome(outcome);
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "登记重试已取消。";
        }
        catch (Exception exception)
        {
            ResultMessage = $"登记重试失败：{exception.Message}";
        }
        finally
        {
            RefreshStorageAccess();
            IsBusy = false;
        }
    }

    private void ApplyCreateOutcome(CreateLinkOutcome outcome)
    {
        switch (outcome)
        {
            case CreateLinkValidationFailed validationFailed:
                validationIsValid = false;
                SetValidationErrors(validationFailed.Validation.Errors);
                ResultMessage = "请先修正路径校验错误。";
                OnPropertyChanged(nameof(CanCreate));
                RaiseCommandStates();
                break;
            case CreateLinkInspectionFailed inspectionFailed:
                SetLinkCreated();
                ResultMessage = $"链接已创建但未通过回读校验：{inspectionFailed.Inspection.Error ?? "状态未知。"}";
                break;
            case CreatedAndRegistered registered:
                SetLinkCreated();
                CreatedRecord = registered.Record;
                ResultMessage = "链接已创建并登记。";
                break;
            case CreatedButUnregistered unregistered:
                SetLinkCreated();
                pendingRequest = unregistered.Request;
                pendingInspection = unregistered.Inspection;
                pendingMetadata = unregistered.Metadata;
                ResultMessage = $"链接已创建，但登记失败：{unregistered.RegistrationError}";
                OnPropertyChanged(nameof(CanRetryRegistration));
                OnPropertyChanged(nameof(CanCreate));
                break;
            default:
                throw new InvalidOperationException("Unknown create-link outcome.");
        }
    }

    private void ApplyRetryOutcome(RetryRegistrationOutcome outcome)
    {
        switch (outcome)
        {
            case RetryRegistrationSucceeded succeeded:
                SetLinkCreated();
                pendingRequest = null;
                pendingInspection = null;
                CreatedRecord = succeeded.Record;
                ResultMessage = "链接登记已重试成功。";
                OnPropertyChanged(nameof(CanRetryRegistration));
                OnPropertyChanged(nameof(CanCreate));
                break;
            case RetryRegistrationRejected rejected:
                ResultMessage = $"登记重试被拒绝：{rejected.Reason}";
                break;
            default:
                throw new InvalidOperationException("Unknown registration-retry outcome.");
        }
    }

    private LinkRequest BuildRequest() => new(SourcePath.Trim(), LinkPath.Trim(), SelectedType);

    private void RefreshValidation()
    {
        previewText = FormatPreview(LinkPath, SourcePath);
        OnPropertyChanged(nameof(PreviewText));

        try
        {
            var validation = fileLinkService.Validate(BuildRequest());
            var errors = validation.Errors.ToList();
            AddAbsolutePathValidationError(errors, SourcePath, "Enter a valid source path.");
            AddAbsolutePathValidationError(errors, LinkPath, "Enter a valid link path.");
            validationIsValid = validation.IsValid && errors.Count == 0;
            SetValidationErrors(errors);
        }
        catch (Exception exception)
        {
            validationIsValid = false;
            SetValidationErrors(new[] { exception.Message }, showSuccessStatuses: false);
        }

        OnPropertyChanged(nameof(CanCreate));
        RaiseCommandStates();
    }

    private static void AddAbsolutePathValidationError(
        ICollection<string> errors,
        string path,
        string error)
    {
        if (string.IsNullOrWhiteSpace(path) || IsFullyQualifiedPath(path))
        {
            return;
        }

        if (!errors.Contains(error, StringComparer.Ordinal))
        {
            errors.Add(error);
        }
    }

    private static bool IsFullyQualifiedPath(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path.Trim());
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void SetValidationErrors(IEnumerable<string> errors, bool showSuccessStatuses = true)
    {
        var sourceErrors = new List<string>();
        var linkErrors = new List<string>();
        var generalErrors = new List<string>();

        foreach (var error in errors.Where(error => !string.IsNullOrWhiteSpace(error)))
        {
            var presentation = PresentValidationError(error);
            switch (presentation.Scope)
            {
                case ValidationErrorScope.SourcePath:
                    sourceErrors.Add(presentation.Message);
                    break;
                case ValidationErrorScope.LinkPath:
                    linkErrors.Add(presentation.Message);
                    break;
                default:
                    generalErrors.Add(presentation.Message);
                    break;
            }
        }

        sourceValidationErrors = sourceErrors.ToArray();
        linkValidationErrors = linkErrors.ToArray();
        generalValidationErrors = generalErrors.ToArray();
        sourceValidationStatus = showSuccessStatuses &&
            !string.IsNullOrWhiteSpace(SourcePath) &&
            sourceValidationErrors.Count == 0
            ? "源路径有效。"
            : null;
        linkValidationStatus = showSuccessStatuses &&
            !string.IsNullOrWhiteSpace(LinkPath) &&
            linkValidationErrors.Count == 0
            ? "链接路径可用。"
            : null;

        OnPropertyChanged(nameof(SourceValidationErrors));
        OnPropertyChanged(nameof(LinkValidationErrors));
        OnPropertyChanged(nameof(GeneralValidationErrors));
        OnPropertyChanged(nameof(SourceValidationStatus));
        OnPropertyChanged(nameof(LinkValidationStatus));
        OnPropertyChanged(nameof(ValidationErrors));
    }

    private static ValidationErrorPresentation PresentValidationError(string error)
    {
        var normalized = error.Trim();
        if (normalized is "Enter a valid source path.")
        {
            return new(ValidationErrorScope.SourcePath, "请输入有效的绝对源路径。");
        }

        if (normalized is "The source is invalid.")
        {
            return new(ValidationErrorScope.SourcePath, "请输入有效的源路径。");
        }

        if (normalized is "The source path does not exist.")
        {
            return new(ValidationErrorScope.SourcePath, "源路径不存在。");
        }

        if (normalized is "The selected link type requires a file source.")
        {
            return new(ValidationErrorScope.SourcePath, "当前链接类型要求源路径为文件。");
        }

        if (normalized is "The selected link type requires a directory source.")
        {
            return new(ValidationErrorScope.SourcePath, "当前链接类型要求源路径为目录。");
        }

        if (normalized is "Enter a valid link path.")
        {
            return new(ValidationErrorScope.LinkPath, "请输入有效的绝对链接路径。");
        }

        if (normalized is "The link is invalid.")
        {
            return new(ValidationErrorScope.LinkPath, "请输入有效的链接路径。");
        }

        if (normalized is "The link path already exists." or "The link already exists.")
        {
            return new(ValidationErrorScope.LinkPath, "链接路径必须不存在。");
        }

        if (normalized is "The link path parent directory does not exist.")
        {
            return new(ValidationErrorScope.LinkPath, "链接路径的父目录不存在。");
        }

        if (normalized.StartsWith("File hard links must be created on the same volume.", StringComparison.Ordinal))
        {
            return new(ValidationErrorScope.General, "文件硬链接的源路径和链接路径必须位于同一卷。");
        }

        if (normalized.StartsWith("Directory junctions require local paths", StringComparison.Ordinal))
        {
            return new(ValidationErrorScope.General, "目录联接不支持网络路径，请使用本机路径。");
        }

        if (normalized is "The source and link locations could not be verified as Windows volumes.")
        {
            return new(ValidationErrorScope.General, "无法验证源路径和链接位置所在的 Windows 卷。");
        }

        if (normalized is "A link request is required.")
        {
            return new(ValidationErrorScope.General, "链接请求不能为空。");
        }

        if (normalized.StartsWith("Link type '", StringComparison.Ordinal) &&
            normalized.EndsWith("' is not supported yet.", StringComparison.Ordinal))
        {
            return new(ValidationErrorScope.General, "当前链接类型暂不支持。");
        }

        return new(ValidationErrorScope.General, normalized);
    }

    private void ClearResultForEdit()
    {
        ResultMessage = null;
        CreatedRecord = null;
        SetLinkCreated(false);
        if (pendingRequest is not null || pendingInspection is not null)
        {
            pendingRequest = null;
            pendingInspection = null;
            OnPropertyChanged(nameof(CanRetryRegistration));
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    private void SetLinkCreated(bool value = true)
    {
        if (linkCreated == value)
        {
            return;
        }

        linkCreated = value;
        OnPropertyChanged(nameof(CanCreate));
        RaiseCommandStates();
    }

    private string GetLinkName()
    {
        var existingName = TryGetFileName(LinkPath);
        if (!string.IsNullOrWhiteSpace(existingName))
        {
            return existingName;
        }

        var sourceName = TryGetFileName(SourcePath);
        return string.IsNullOrWhiteSpace(sourceName) ? "new-link" : sourceName;
    }

    private static string? TryGetFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string FormatPreview(string linkPath, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(linkPath) && string.IsNullOrWhiteSpace(sourcePath))
        {
            return string.Empty;
        }

        var link = string.IsNullOrWhiteSpace(linkPath) ? "（未填写）" : linkPath.Trim();
        var source = string.IsNullOrWhiteSpace(sourcePath) ? "（未填写）" : sourcePath.Trim();
        return $"{link}（新链接） → {source}（现有源）";
    }

    private static IReadOnlyList<LinkTypeCardViewModel> CreateTypeCards() => new[]
    {
        new LinkTypeCardViewModel(
            FileLinkType.DirectorySymbolicLink,
            "目录符号链接",
            "目录的跨卷等价路径；只占少量元数据，不复制内容。",
            "源必须是目录",
            true),
        new LinkTypeCardViewModel(
            FileLinkType.DirectoryJunction,
            "目录联接",
            "将目录重定向到本机另一目录；只占少量元数据，不复制内容。",
            "仅目录；可跨本机卷；不支持映射网络卷"),
        new LinkTypeCardViewModel(
            FileLinkType.FileSymbolicLink,
            "文件符号链接",
            "文件的跨卷等价路径；只占少量元数据，不复制内容。",
            "源必须是文件"),
        new LinkTypeCardViewModel(
            FileLinkType.FileHardLink,
            "文件硬链接",
            "共享同一文件内容，不复制数据；仅占少量目录项/元数据，最后一个链接删除后才释放内容空间。",
            "仅文件；必须同卷")
    };

    private enum ValidationErrorScope
    {
        SourcePath,
        LinkPath,
        General
    }

    private sealed record ValidationErrorPresentation(ValidationErrorScope Scope, string Message);

    private void UpdateTypeCardSelection()
    {
        TypeCards = TypeCards
            .Select(card => card with { IsSelected = card.Type == SelectedType })
            .ToArray();
        OnPropertyChanged(nameof(TypeCards));
    }

    private void RaiseCommandStates()
    {
        SelectTypeCommand.RaiseCanExecuteChanged();
        BrowseSourceCommand.RaiseCanExecuteChanged();
        BrowseLinkLocationCommand.RaiseCanExecuteChanged();
        CreateCommand.RaiseCanExecuteChanged();
        RetryRegistrationCommand.RaiseCanExecuteChanged();
        NewGroupCommand.RaiseCanExecuteChanged();
    }

    private void RefreshStorageAccess()
    {
        if (recordStoreDiagnostics is not null)
        {
            StorageAccess.Apply(recordStoreDiagnostics.LastLoadDiagnostics);
        }
    }

    private void StorageAccessOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(StorageAccessViewModel.IsReadOnly) or nameof(StorageAccessViewModel.Message))
        {
            MetadataEditor.IsEnabled = !IsBusy && !StorageAccess.IsReadOnly;
            OnPropertyChanged(nameof(CanCreate));
            OnPropertyChanged(nameof(CanRetryRegistration));
            RaiseCommandStates();
        }
    }

}
