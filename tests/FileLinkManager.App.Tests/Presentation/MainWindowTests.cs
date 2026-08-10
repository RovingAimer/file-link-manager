using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FileLinkManager.App.Presentation;
using FileLinkManager.App.Presentation.Dialogs;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;
using FileLinkManager.Infrastructure.Devices;
using FileLinkManager.Infrastructure.Links;
using FileLinkManager.Infrastructure.Runtime;
using FileLinkManager.Infrastructure.Storage;

namespace FileLinkManager.App.Tests.Presentation;

[CollectionDefinition("WPF UI", DisableParallelization = true)]
public sealed class WpfUiTestCollection
{
}

[Collection("WPF UI")]
public sealed class MainWindowTests
{
    private static readonly object ApplicationGate = new();

    [Fact]
    public void App_ExposesConcreteWindowsInstanceGuardInjection()
    {
        var constructor = typeof(FileLinkManager.App.App).GetConstructor(
            new[] { typeof(WindowsApplicationInstanceGuard) });

        Assert.NotNull(constructor);
        Assert.Equal(
            typeof(WindowsApplicationInstanceGuard),
            constructor!.GetParameters().Single().ParameterType);
    }

    [Fact]
    public void MainWindow_UsesSuppliedCreatePageViewModelAsActivePage()
    {
        var viewModel = CreateViewModel();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                var window = new MainWindow(viewModel);

                Assert.IsType<CreateLinkPage>(window.ActivePage);
                Assert.Same(viewModel, window.ActivePage.DataContext);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    [Fact]
    public void MainWindow_InitialLayoutRendersValidationErrors()
    {
        var viewModel = CreateViewModel();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                var window = new MainWindow(viewModel);
                window.Show();
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    [Fact]
    public void NavigationButtons_RenderEnabledTextWithExplicitForeground()
    {
        var createViewModel = CreateViewModel();
        var recordsViewModel = CreateRecordsViewModel();
        var aboutViewModel = CreateAboutViewModel();

        RunOnSta(() =>
        {
            EnsureApplication();
            var window = new MainWindow(createViewModel, recordsViewModel, aboutViewModel);
            try
            {
                window.Show();
                window.UpdateLayout();

                foreach (var name in new[] { "RecordsNavigationButton", "AboutNavigationButton" })
                {
                    var button = Assert.IsType<Button>(window.FindName(name));
                    Assert.True(button.IsEnabled);
                    button.ApplyTemplate();

                    var text = Assert.IsType<TextBlock>(button.Template.FindName("NavigationText", button));
                    Assert.Equal(button.Content?.ToString(), text.Text);
                    var brush = Assert.IsType<SolidColorBrush>(text.Foreground);
                    Assert.Equal(Colors.White, brush.Color);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Sidebar_UsesCompactWidthWithoutClippingText()
    {
        var viewModel = CreateViewModel();

        RunOnSta(() =>
        {
            EnsureApplication();
            var window = new MainWindow(viewModel);
            try
            {
                window.Show();
                window.UpdateLayout();

                var sidebar = Assert.Single(
                    FindVisualDescendants<Border>(window),
                    border => Math.Abs(border.ActualWidth - 190.4) < 0.1);
                Assert.InRange(sidebar.ActualWidth, 189.5, 191.5);

                var textBlocks = FindVisualDescendants<TextBlock>(sidebar)
                    .Where(text => text.Text is "文件链接管理器" or "Windows 原生链接工具")
                    .ToArray();
                Assert.Equal(2, textBlocks.Length);
                Assert.All(textBlocks, text =>
                {
                    Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
                    Assert.True(text.DesiredSize.Width <= text.ActualWidth + 0.5);
                });

                var buttons = FindVisualDescendants<Button>(sidebar).ToArray();
                Assert.Equal(3, buttons.Length);
                Assert.All(buttons, button =>
                {
                    Assert.Equal(HorizontalAlignment.Stretch, button.HorizontalAlignment);
                    Assert.True(button.ActualWidth >= button.DesiredSize.Width - 0.5);
                });
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DeletedRecordConfirmation_ExplainsThatOnlySoftwareHistoryWillBeRemoved()
    {
        var request = new LinkRequest(
            @"C:\Sources\source.txt",
            @"C:\Links\history.txt",
            FileLinkType.FileHardLink);
        var record = new PersistedLinkRecord(
            Guid.NewGuid(),
            "device-current",
            "Current Device",
            new LinkRecord(request, new LinkFingerprint(null, request.SourcePath, 1, "ABC", request.LinkPath)),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            LinkStatus.Deleted,
            null);

        var message = WindowsRecordPageDialogService.FormatRemoveDeletedRecordConfirmation(record);

        Assert.Contains("只删除软件中的历史记录", message, StringComparison.Ordinal);
        Assert.Contains("不会再操作磁盘文件/目录", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePage_TypeCardSelectionKeepsCardGeometryStable()
    {
        var viewModel = CreateViewModel();

        RunOnSta(() =>
        {
            EnsureApplication();
            var page = new CreateLinkPage
            {
                DataContext = viewModel
            };
            page.Measure(new Size(1100, 800));
            page.Arrange(new Rect(0, 0, 1100, 800));
            page.UpdateLayout();

            var cards = FindTypeCardButtons(page);
            Assert.Equal(4, cards.Count);
            Assert.All(cards.Values, button => Assert.Equal(new Thickness(1), button.BorderThickness));
            var before = CaptureCardGeometry(cards, page);

            viewModel.SelectedType = FileLinkType.FileHardLink;
            page.UpdateLayout();

            cards = FindTypeCardButtons(page);
            Assert.Equal(4, cards.Count);
            Assert.All(cards.Values, button => Assert.Equal(new Thickness(1), button.BorderThickness));
            var after = CaptureCardGeometry(cards, page);
            foreach (var type in before.Keys)
            {
                AssertGeometryEqual(before[type], after[type]);
            }
        });
    }

    [Fact]
    public void CreatePage_PathErrorsAreBoundBesideTheirInputsWithoutPreviewGlobalList()
    {
        var viewModel = CreateViewModel();

        RunOnSta(() =>
        {
            EnsureApplication();
            var page = new CreateLinkPage
            {
                DataContext = viewModel
            };
            page.Measure(new Size(1100, 1000));
            page.Arrange(new Rect(0, 0, 1100, 1000));
            page.UpdateLayout();

            var itemsControls = FindVisualDescendants<ItemsControl>(page);
            var sourceErrors = Assert.Single(itemsControls, control =>
                GetBindingPath(control, ItemsControl.ItemsSourceProperty) == nameof(CreateLinkPageViewModel.SourceValidationErrors));
            var linkErrors = Assert.Single(itemsControls, control =>
                GetBindingPath(control, ItemsControl.ItemsSourceProperty) == nameof(CreateLinkPageViewModel.LinkValidationErrors));
            Assert.DoesNotContain(itemsControls, control =>
                GetBindingPath(control, ItemsControl.ItemsSourceProperty) == nameof(CreateLinkPageViewModel.ValidationErrors));

            var sourceTextBox = Assert.Single(FindVisualDescendants<TextBox>(page), textBox =>
                GetBindingPath(textBox, TextBox.TextProperty) == nameof(CreateLinkPageViewModel.SourcePath));
            var linkTextBox = Assert.Single(FindVisualDescendants<TextBox>(page), textBox =>
                GetBindingPath(textBox, TextBox.TextProperty) == nameof(CreateLinkPageViewModel.LinkPath));

            var sourceOrigin = sourceTextBox.TranslatePoint(new Point(0, 0), page);
            var sourceErrorOrigin = sourceErrors.TranslatePoint(new Point(0, 0), page);
            var linkOrigin = linkTextBox.TranslatePoint(new Point(0, 0), page);
            var linkErrorOrigin = linkErrors.TranslatePoint(new Point(0, 0), page);
            Assert.True(sourceErrors.ActualHeight > 0);
            Assert.True(linkErrors.ActualHeight > 0);
            Assert.True(sourceErrorOrigin.Y >= sourceOrigin.Y + sourceTextBox.ActualHeight);
            Assert.True(sourceErrorOrigin.Y + sourceErrors.ActualHeight <= linkOrigin.Y);
            Assert.True(linkErrorOrigin.Y >= linkOrigin.Y + linkTextBox.ActualHeight);

            Assert.Contains("源路径", viewModel.SourceValidationErrors[0], StringComparison.Ordinal);
            Assert.Contains("链接路径", viewModel.LinkValidationErrors[0], StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CreatePage_PathStatusesRefreshInRealLayoutAndFollowSelectedType()
    {
        var viewModel = CreateViewModel();
        var root = Path.Combine(Path.GetTempPath(), $"FileLinkManager.App.Tests.{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(root, "source.txt");
        var linkParent = Path.Combine(root, "links");
        var linkPath = Path.Combine(linkParent, "source-link.txt");
        Directory.CreateDirectory(linkParent);
        File.WriteAllText(sourcePath, "source");

        try
        {
            RunOnSta(() =>
            {
                EnsureApplication();
                var page = new CreateLinkPage
                {
                    DataContext = viewModel
                };
                page.Measure(new Size(1100, 1200));
                page.Arrange(new Rect(0, 0, 1100, 1200));
                page.UpdateLayout();

                var sourceErrors = Assert.Single(
                    FindVisualDescendants<ItemsControl>(page),
                    control => GetBindingPath(control, ItemsControl.ItemsSourceProperty) == nameof(CreateLinkPageViewModel.SourceValidationErrors));
                var linkErrors = Assert.Single(
                    FindVisualDescendants<ItemsControl>(page),
                    control => GetBindingPath(control, ItemsControl.ItemsSourceProperty) == nameof(CreateLinkPageViewModel.LinkValidationErrors));
                Assert.True(sourceErrors.ActualHeight > 0);
                Assert.True(linkErrors.ActualHeight > 0);

                var sourceTextBox = Assert.Single(
                    FindVisualDescendants<TextBox>(page),
                    textBox => GetBindingPath(textBox, TextBox.TextProperty) == nameof(CreateLinkPageViewModel.SourcePath));
                var linkTextBox = Assert.Single(
                    FindVisualDescendants<TextBox>(page),
                    textBox => GetBindingPath(textBox, TextBox.TextProperty) == nameof(CreateLinkPageViewModel.LinkPath));
                var sourceStatus = Assert.Single(
                    FindVisualDescendants<TextBlock>(page),
                    text => GetBindingPath(text, TextBlock.TextProperty) == "SourceValidationStatus");
                var linkStatus = Assert.Single(
                    FindVisualDescendants<TextBlock>(page),
                    text => GetBindingPath(text, TextBlock.TextProperty) == "LinkValidationStatus");

                viewModel.SelectedType = FileLinkType.FileSymbolicLink;
                sourceTextBox.Text = sourcePath;
                linkTextBox.Text = linkPath;
                page.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.DataBind, new Action(() => { }));
                page.UpdateLayout();

                Assert.True(viewModel.CanCreate);
                Assert.Equal(0, sourceErrors.ActualHeight, 0.01);
                Assert.Equal(0, linkErrors.ActualHeight, 0.01);
                Assert.Equal("源路径有效。", sourceStatus.Text);
                Assert.Equal("链接路径可用。", linkStatus.Text);

                viewModel.SelectedType = FileLinkType.DirectoryJunction;
                page.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.DataBind, new Action(() => { }));
                page.UpdateLayout();

                Assert.False(viewModel.CanCreate);
                Assert.True(sourceErrors.ActualHeight > 0);
                Assert.Contains("目录", string.Join(" ", FindVisualDescendants<TextBlock>(page).Select(text => text.Text)), StringComparison.Ordinal);
                Assert.Equal(string.Empty, sourceStatus.Text ?? string.Empty);
                Assert.NotEqual("源路径有效。", sourceStatus.Text);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MainWindow_NavigatesBetweenCreateAndRecordsPages()
    {
        var createViewModel = CreateViewModel();
        var recordsViewModel = CreateRecordsViewModel();
        var aboutViewModel = CreateAboutViewModel();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                var window = new MainWindow(createViewModel, recordsViewModel, aboutViewModel);

                Assert.IsType<CreateLinkPage>(window.ActivePage);
                window.ShowRecordsPage();
                Assert.IsType<LinkRecordsPage>(window.ActivePage);
                Assert.Same(recordsViewModel, window.ActivePage.DataContext);

                window.ShowAboutPage();
                Assert.IsType<AboutPage>(window.ActivePage);
                Assert.Same(aboutViewModel, window.ActivePage.DataContext);

                window.ShowRecordsPage();
                Assert.IsType<LinkRecordsPage>(window.ActivePage);
                window.ShowCreatePage();
                Assert.IsType<CreateLinkPage>(window.ActivePage);
                Assert.Same(createViewModel, window.ActivePage.DataContext);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private static CreateLinkPageViewModel CreateViewModel()
    {
        var fileLinkService = new WindowsFileLinkService();
        var store = new JsonLinkRecordStore(Path.Combine(Path.GetTempPath(), $"FileLinkManager.App.Tests.{Guid.NewGuid():N}"));
        var device = new WindowsDeviceIdentityProvider();
        return new CreateLinkPageViewModel(
            fileLinkService,
            new CreateLinkUseCase(fileLinkService, store, device),
            new RetryRegistrationUseCase(store, device),
            new WindowsPathDialogService());
    }

    private static void EnsureApplication()
    {
        lock (ApplicationGate)
        {
            if (System.Windows.Application.Current is null)
            {
                _ = new FileLinkManager.App.App();
            }
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private static IReadOnlyDictionary<FileLinkType, Button> FindTypeCardButtons(FrameworkElement root) =>
        FindVisualDescendants<Button>(root)
            .Where(button => button.DataContext is LinkTypeCardViewModel)
            .ToDictionary(
                button => ((LinkTypeCardViewModel)button.DataContext!).Type,
                button => button);

    private static IReadOnlyDictionary<FileLinkType, Rect> CaptureCardGeometry(
        IReadOnlyDictionary<FileLinkType, Button> cards,
        FrameworkElement root) =>
        cards.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var origin = pair.Value.TranslatePoint(new Point(0, 0), root);
                return new Rect(origin, new Size(pair.Value.ActualWidth, pair.Value.ActualHeight));
            });

    private static string? GetBindingPath(DependencyObject target, DependencyProperty property) =>
        BindingOperations.GetBinding(target, property)?.Path?.Path;

    private static void AssertGeometryEqual(Rect expected, Rect actual)
    {
        const double tolerance = 0.01;
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Width - actual.Width), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Height - actual.Height), 0, tolerance);
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            yield return match;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var descendant in FindVisualDescendants<T>(VisualTreeHelper.GetChild(root, index)))
            {
                yield return descendant;
            }
        }
    }

    private static LinkRecordsPageViewModel CreateRecordsViewModel()
    {
        var fileLinkService = new WindowsFileLinkService();
        var store = new JsonLinkRecordStore(Path.Combine(Path.GetTempPath(), $"FileLinkManager.App.Tests.{Guid.NewGuid():N}"));
        var device = new WindowsDeviceIdentityProvider();
        return new LinkRecordsPageViewModel(
            store,
            device,
            new LinkRecordQueryService(),
            new InspectLinksUseCase(fileLinkService, store, device),
            new DeleteLinksUseCase(fileLinkService, store, device),
            new StubRecordPageDialogService());
    }

    private static AboutPageViewModel CreateAboutViewModel()
    {
        var device = new WindowsDeviceIdentityProvider();
        return new AboutPageViewModel(
            device,
            Path.Combine(Path.GetTempPath(), $"FileLinkManager.App.Tests.{Guid.NewGuid():N}"),
            new StubFileExplorerService());
    }

    private sealed class StubRecordPageDialogService : IRecordPageDialogService
    {
        public bool ConfirmDelete(PersistedLinkRecord record) => false;

        public bool ConfirmDeleteMissing(PersistedLinkRecord record) => false;

        public bool ConfirmRemoveDeletedRecord(PersistedLinkRecord record) => false;

        public void OpenContainingFolder(string linkPath)
        {
        }
    }

    private sealed class StubFileExplorerService : IFileExplorerService
    {
        public void OpenFolder(string folderPath)
        {
        }
    }
}
