using System.Windows;
using FileLinkManager.App.Presentation;

namespace FileLinkManager.App;

public partial class MainWindow : Window
{
    private readonly CreateLinkPageViewModel createViewModel;
    private readonly LinkRecordsPageViewModel? recordsViewModel;
    private bool NavigationBusy => createViewModel.IsBusy || recordsViewModel?.IsBusy == true;
    public MainWindow(CreateLinkPageViewModel createLinkPageViewModel)
        : this(createLinkPageViewModel, null, null)
    {
    }

    public MainWindow(
        CreateLinkPageViewModel createLinkPageViewModel,
        LinkRecordsPageViewModel? linkRecordsPageViewModel)
        : this(createLinkPageViewModel, linkRecordsPageViewModel, null)
    {
    }

    public MainWindow(
        CreateLinkPageViewModel createLinkPageViewModel,
        LinkRecordsPageViewModel? linkRecordsPageViewModel,
        AboutPageViewModel? aboutPageViewModel)
    {
        ArgumentNullException.ThrowIfNull(createLinkPageViewModel);
        createViewModel = createLinkPageViewModel;
        recordsViewModel = linkRecordsPageViewModel;
        InitializeComponent();
        CreatePage = new CreateLinkPage
        {
            DataContext = createLinkPageViewModel
        };
        RecordsPage = linkRecordsPageViewModel is null
            ? null
            : new LinkRecordsPage
            {
                DataContext = linkRecordsPageViewModel
            };
        AboutPage = aboutPageViewModel is null
            ? null
            : new AboutPage
            {
                DataContext = aboutPageViewModel
            };
        RecordsNavigationButton.IsEnabled = RecordsPage is not null;
        AboutNavigationButton.IsEnabled = AboutPage is not null;
        createViewModel.PropertyChanged += BusyChanged;
        if (recordsViewModel is not null) recordsViewModel.PropertyChanged += BusyChanged;
        Closed += (_, _) =>
        {
            createViewModel.PropertyChanged -= BusyChanged;
            if (recordsViewModel is not null) recordsViewModel.PropertyChanged -= BusyChanged;
        };
        ShowCreatePage();
    }

    public CreateLinkPage CreatePage { get; }

    public LinkRecordsPage? RecordsPage { get; }

    public AboutPage? AboutPage { get; }

    public FrameworkElement ActivePage { get; private set; } = null!;

    public void ShowCreatePage()
    {
        if (NavigationBusy) return;
        SetActivePage(CreatePage, "创建链接", "选择链接类型，填写已有源路径和新的链接路径。");
        _ = createViewModel.EnterPageAsync();
    }

    public void ShowRecordsPage()
    {
        if (RecordsPage is null || NavigationBusy)
        {
            return;
        }

        SetActivePage(RecordsPage, "链接记录", "按设备、分组和名称查找记录，并安全删除链接入口。");
    }

    public void ShowAboutPage()
    {
        if (AboutPage is null || NavigationBusy)
        {
            return;
        }

        SetActivePage(AboutPage, "关于", "查看版本、当前设备身份摘要和便携数据位置。");
    }

    private void ShowCreatePageClick(object sender, RoutedEventArgs e) => ShowCreatePage();

    private void BusyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CreateLinkPageViewModel.IsBusy)) return;
        CreateNavigationButton.IsEnabled = !NavigationBusy;
        RecordsNavigationButton.IsEnabled = !NavigationBusy && RecordsPage is not null;
        AboutNavigationButton.IsEnabled = !NavigationBusy && AboutPage is not null;
    }

    private void ShowRecordsPageClick(object sender, RoutedEventArgs e) => ShowRecordsPage();

    private void ShowAboutPageClick(object sender, RoutedEventArgs e) => ShowAboutPage();

    private void SetActivePage(FrameworkElement page, string title, string subtitle)
    {
        ActivePage = page;
        MainContent.Content = page;
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
    }
}
