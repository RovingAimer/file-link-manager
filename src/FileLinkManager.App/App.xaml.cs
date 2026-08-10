using System.IO;
using System.Windows;
using FileLinkManager.App.Presentation;
using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.Application.Abstractions;
using FileLinkManager.Application.Links;
using FileLinkManager.Infrastructure.Devices;
using FileLinkManager.Infrastructure.Links;
using FileLinkManager.Infrastructure.Runtime;
using FileLinkManager.Infrastructure.Storage;

namespace FileLinkManager.App;

public partial class App : System.Windows.Application
{
    private readonly WindowsApplicationInstanceGuard instanceGuard;

    public App()
        : this(new WindowsApplicationInstanceGuard())
    {
    }

    public App(WindowsApplicationInstanceGuard instanceGuard)
    {
        this.instanceGuard = instanceGuard ?? throw new ArgumentNullException(nameof(instanceGuard));
        InitializeComponent();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!instanceGuard.TryAcquire())
        {
            System.Windows.MessageBox.Show(
                "文件链接管理器已经在运行中。",
                "文件链接管理器",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var fileLinkService = new WindowsFileLinkService();
        var dataRoot = Path.Combine(AppContext.BaseDirectory, "data");
        var recordStore = new JsonLinkRecordStore(dataRoot);
        var deviceIdentityProvider = new WindowsDeviceIdentityProvider();
        var storageAccess = new StorageAccessViewModel();
        var recordStoreDiagnostics = (ILinkRecordStoreDiagnostics)recordStore;
        try
        {
            _ = await recordStore.LoadAsync(CancellationToken.None);
        }
        catch (LinkRecordStoreException exception)
        {
            storageAccess.MarkReadOnly(exception.Reason);
        }
        storageAccess.Apply(recordStoreDiagnostics.LastLoadDiagnostics);

        var createLinkPage = new CreateLinkPageViewModel(
            fileLinkService,
            new CreateLinkUseCase(fileLinkService, recordStore, deviceIdentityProvider),
            new RetryRegistrationUseCase(recordStore, deviceIdentityProvider),
            new WindowsPathDialogService(),
            storageAccess: storageAccess,
            recordStoreDiagnostics: recordStoreDiagnostics);
        var inspectLinksUseCase = new InspectLinksUseCase(fileLinkService, recordStore, deviceIdentityProvider);
        var deleteLinksUseCase = new DeleteLinksUseCase(fileLinkService, recordStore, deviceIdentityProvider);
        var removeDeletedLinkRecordUseCase = new RemoveDeletedLinkRecordUseCase(recordStore);
        var linkRecordsPage = new LinkRecordsPageViewModel(
            recordStore,
            deviceIdentityProvider,
            new LinkRecordQueryService(),
            inspectLinksUseCase,
            deleteLinksUseCase,
            new WindowsRecordPageDialogService(),
            storageAccess,
            recordStoreDiagnostics,
            removeDeletedLinkRecordUseCase);
        var aboutPage = new AboutPageViewModel(
            deviceIdentityProvider,
            dataRoot,
            new WindowsFileExplorerService());

        var window = new MainWindow(createLinkPage, linkRecordsPage, aboutPage);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instanceGuard.Dispose();
        base.OnExit(e);
    }
}
