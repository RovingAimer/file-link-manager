using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;

namespace FileLinkManager.App.Presentation.Dialogs;

public sealed class WindowsOrganizationDialogService : IOrganizationDialogService
{
    public bool EditMetadata(RecordMetadataViewModel editor)
    {
        var panel = Panel();
        var window = Dialog("编辑记录信息", panel);
        window.DataContext = editor;
        panel.Children.Add(Text("只修改软件中的名称和分组，不修改磁盘路径或文件名。"));
        panel.Children.Add(Text("自定义名称（选填）"));
        var input = new TextBox { Margin = new Thickness(0, 6, 0, 4), Padding = new Thickness(8) };
        input.SetBinding(TextBox.TextProperty, new Binding("CustomName") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        panel.Children.Add(input);
        var count = Text(""); count.SetBinding(TextBlock.TextProperty, new Binding("TextElementCount") { StringFormat = "{0}/20" }); panel.Children.Add(count);
        var error = Text(""); error.Foreground = System.Windows.Media.Brushes.Firebrick;
        error.SetBinding(TextBlock.TextProperty, new Binding("NameError")); panel.Children.Add(error);
        panel.Children.Add(Text("分组"));
        var groups = new ComboBox { DisplayMemberPath = "DisplayName", Margin = new Thickness(0, 6, 0, 12) };
        groups.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Groups"));
        groups.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding("SelectedGroup") { Mode = BindingMode.TwoWay });
        panel.Children.Add(groups);
        var save = new Button { Content = "保存信息", Padding = new Thickness(16, 8, 16, 8), IsDefault = true };
        save.SetBinding(UIElement.IsEnabledProperty, new Binding("CanSave"));
        save.Click += (_, _) => window.DialogResult = true;
        AddActions(panel, save);
        return window.ShowDialog() == true;
    }

    public string? PromptGroupName(string title, string initialName)
    {
        var panel = Panel(); var window = Dialog(title, panel);
        panel.Children.Add(Text("分组名称"));
        var input = new TextBox { Text = initialName, Margin = new Thickness(0, 6, 0, 8), Padding = new Thickness(8) };
        panel.Children.Add(input);
        var error = Text(""); error.Foreground = System.Windows.Media.Brushes.Firebrick; panel.Children.Add(error);
        var save = new Button { Content = "确认", Padding = new Thickness(16, 8, 16, 8), IsDefault = true };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) error.Text = "分组名称不能为空。";
            else window.DialogResult = true;
        };
        AddActions(panel, save);
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }

    public bool ConfirmDissolveGroup(string name) => System.Windows.MessageBox.Show(
        $"删除分组“{name}”？\n\n保留链接记录并归入未分组，不删除磁盘链接。", "确认删除分组",
        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowGroupManager(LinkGroupManagerViewModel viewModel)
    {
        var panel = Panel(); var window = Dialog("管理分组", panel);
        window.DataContext = viewModel;
        panel.Children.Add(Text("删除分组会保留链接记录并归入未分组，不删除磁盘链接。"));
        var list = new ListBox { DisplayMemberPath = "Name", Height = 200, Margin = new Thickness(0, 8, 0, 12) };
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Groups"));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding("SelectedGroup") { Mode = BindingMode.TwoWay });
        panel.Children.Add(list);
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        foreach (var (label, command) in new[] { ("新增分组", "NewCommand"), ("重命名", "RenameCommand"), ("删除分组", "DissolveCommand") })
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
            button.SetBinding(Button.CommandProperty, new Binding(command)); buttons.Children.Add(button);
        }
        var message = Text(""); message.SetBinding(TextBlock.TextProperty, new Binding("Message")); panel.Children.Add(message);
        panel.Children.Add(new Button { Content = "完成", IsCancel = true, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 12, 0, 0) });
        window.Closing += (_, e) => e.Cancel = viewModel.IsBusy;
        try { window.ShowDialog(); } finally { viewModel.Detach(); }
    }

    private static StackPanel Panel() => new() { Margin = new Thickness(24) };
    private static void AddActions(StackPanel panel, Button save)
    {
        var actions = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(new Button { Content = "取消", IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) });
        actions.Children.Add(save); panel.Children.Add(actions);
    }
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private static Window Dialog(string title, StackPanel panel)
    {
        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        return new Window
        {
            Title = title, Width = 500, MaxHeight = 620, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner, Background = System.Windows.Media.Brushes.WhiteSmoke,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
    }
}
