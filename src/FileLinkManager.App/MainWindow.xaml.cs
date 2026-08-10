using System.Windows;
using FileLinkManager.App.Presentation;

namespace FileLinkManager.App;

public partial class MainWindow : Window
{
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
        ShowCreatePage();
    }

    public CreateLinkPage CreatePage { get; }

    public LinkRecordsPage? RecordsPage { get; }

    public AboutPage? AboutPage { get; }

    public FrameworkElement ActivePage { get; private set; } = null!;

    public void ShowCreatePage()
    {
        SetActivePage(CreatePage, "创建链接", "选择链接类型，填写已有源路径和新的链接路径。");
    }

    public void ShowRecordsPage()
    {
        if (RecordsPage is null)
        {
            return;
        }

        SetActivePage(RecordsPage, "链接记录", "按设备和状态查看已创建链接，并安全删除链接入口。");
    }

    public void ShowAboutPage()
    {
        if (AboutPage is null)
        {
            return;
        }

        SetActivePage(AboutPage, "关于", "查看版本、当前设备身份摘要和便携数据位置。");
    }

    private void ShowCreatePageClick(object sender, RoutedEventArgs e) => ShowCreatePage();

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
