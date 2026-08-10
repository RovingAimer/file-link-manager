namespace FileLinkManager.App.Presentation;

public partial class LinkRecordsPage : System.Windows.Controls.UserControl
{
    public LinkRecordsPage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is LinkRecordsPageViewModel viewModel)
        {
            _ = viewModel.EnterPageAsync();
        }
    }
}
