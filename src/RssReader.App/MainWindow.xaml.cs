using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Diagnostics;
using RssReader.App.ViewModels;
using RssReader.Domain;

namespace RssReader.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _autoRefreshTimer = new(DispatcherPriority.Background);
    private MainWindowViewModel? _viewModel;
    private bool _isSidebarPeekOpen;

    public MainWindow() : this(new MainWindowViewModel(Profile.CreateRegular("Reader")))
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.FolderSelectionRequested = ShowFolderSelectionAsync;
        viewModel.ConfirmUnfollowAllRequested = ConfirmUnfollowAllAsync;
        viewModel.ConfirmDeleteFolderRequested = ConfirmDeleteFolderAsync;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        SelectedArticleHtmlViewer.ExternalLinkRequested += ArticleHtmlViewer_ExternalLinkRequested;
        UpdateSelectedArticleContent();
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        Closed += (_, _) =>
        {
            _autoRefreshTimer.Stop();
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            SelectedArticleHtmlViewer.ExternalLinkRequested -= ArticleHtmlViewer_ExternalLinkRequested;
        };
        UpdateSidebarPresentation();
        ConfigureAutoRefreshTimer();
    }

    private Task<bool> ConfirmUnfollowAllAsync(int feedCount)
    {
        var feedLabel = feedCount == 1 ? "feed" : "feeds";
        var result = MessageDialogWindow.Show(
            this,
            $"Unfollow {feedCount} {feedLabel} currently shown in this list?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    private Task<bool> ConfirmDeleteFolderAsync(string folderName, int feedCount)
    {
        var message = feedCount == 0
            ? $"Delete the empty folder '{folderName}'?"
            : $"Delete folder '{folderName}' and unfollow its {feedCount} feed(s)?";
        var result = MessageDialogWindow.Show(
            this,
            message,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public event Action? LogoutRequested;

    public event Action? PreferencesRequested;

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized;

    private void RestoreWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Normal;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedArticle))
        {
            UpdateSelectedArticleContent();
        }

        if (e.PropertyName == nameof(MainWindowViewModel.AutoRefreshIntervalMinutes))
        {
            ConfigureAutoRefreshTimer();
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsSidebarPinned))
        {
            UpdateSidebarPresentation();
        }
    }

    private void UpdateSelectedArticleContent()
    {
        var article = _viewModel?.SelectedArticle;
        SelectedArticleHtmlViewer.SetArticle(
            article?.Content,
            article?.Summary,
            article?.ImageUrl,
            article?.Link,
            article?.FeedUrl);
    }

    private void ConfigureAutoRefreshTimer()
    {
        _autoRefreshTimer.Stop();
        if (_viewModel is null || _viewModel.IsCatalogMaster || _viewModel.AutoRefreshIntervalMinutes <= 0)
        {
            return;
        }

        _autoRefreshTimer.Interval = TimeSpan.FromMinutes(_viewModel.AutoRefreshIntervalMinutes);
        _autoRefreshTimer.Start();
    }

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.RefreshNowAsync();
        }
    }

    private void UpdateSidebarPresentation()
    {
        if (_viewModel is null)
        {
            return;
        }

        var sidebarActionName = _viewModel.IsSidebarPinned ? "Hide sidebar" : "Pin sidebar";
        SidebarPinButton.ToolTip = sidebarActionName;
        AutomationProperties.SetName(SidebarPinButton, sidebarActionName);
        SidebarPeekButton.ToolTip = sidebarActionName;
        AutomationProperties.SetName(SidebarPeekButton, sidebarActionName);
        if (_viewModel.IsSidebarPinned)
        {
            _isSidebarPeekOpen = false;
            SidebarPeekButton.Visibility = Visibility.Collapsed;
            SidebarPanel.Visibility = Visibility.Visible;
            SetSidebarOffset(0, animate: false);
        }
        else
        {
            SidebarPeekButton.Visibility = Visibility.Visible;
            if (!_isSidebarPeekOpen)
            {
                SidebarPanel.Visibility = Visibility.Collapsed;
                SetSidebarOffset(-_viewModel.SidebarPanelWidth, animate: false);
            }
        }
    }

    private void SidebarPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => ShowSidebarPeek();

    private void SidebarPeekButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => ShowSidebarPeek();

    private void SidebarLinkRoot_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Grid { DataContext: SidebarLink { IsFolder: true } } row)
        {
            row.Tag = true;
        }
    }

    private void SidebarLinkRoot_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Grid { DataContext: SidebarLink { IsFolder: true } } row)
        {
            row.Tag = false;
        }
    }

    private void SidebarPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ScheduleSidebarPeekClose();

    private void SidebarPeekButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ScheduleSidebarPeekClose();

    private void ShowSidebarPeek()
    {
        if (_viewModel is null || _viewModel.IsSidebarPinned)
        {
            return;
        }

        var wasCollapsed = SidebarPanel.Visibility == Visibility.Collapsed;
        _isSidebarPeekOpen = true;
        if (wasCollapsed)
        {
            SetSidebarOffset(-_viewModel.SidebarPanelWidth, animate: false);
        }

        SidebarPanel.Visibility = Visibility.Visible;
        SetSidebarOffset(0, animate: true);
    }

    private void ScheduleSidebarPeekClose()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_viewModel is null || _viewModel.IsSidebarPinned || SidebarPanel.IsMouseOver || SidebarPeekButton.IsMouseOver)
            {
                return;
            }

            _isSidebarPeekOpen = false;
            SidebarPeekButton.ToolTip = "Show sidebar";
            var animation = CreateSidebarAnimation(-_viewModel.SidebarPanelWidth);
            animation.Completed += (_, _) =>
            {
                if (!_isSidebarPeekOpen && !_viewModel.IsSidebarPinned)
                {
                    SidebarPanel.Visibility = Visibility.Collapsed;
                }
            };
            ((TranslateTransform)SidebarPanel.RenderTransform).BeginAnimation(TranslateTransform.XProperty, animation);
        }));
    }

    private void SetSidebarOffset(double offset, bool animate)
    {
        var transform = (TranslateTransform)SidebarPanel.RenderTransform;
        if (animate)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, CreateSidebarAnimation(offset));
            return;
        }

        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = offset;
    }

    private static DoubleAnimation CreateSidebarAnimation(double offset) => new(offset, TimeSpan.FromMilliseconds(180))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    private void ProfileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void CatalogActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e) => PreferencesRequested?.Invoke();

    private void LogoutMenuItem_Click(object sender, RoutedEventArgs e) => LogoutRequested?.Invoke();

    private void SourceLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenExternalUri(e.Uri);
        e.Handled = true;
    }

    private void ArticleHtmlViewer_ExternalLinkRequested(Uri uri) => OpenExternalUri(uri);

    private void OpenExternalUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageDialogWindow.Show(
                this,
                $"The source link could not be opened.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ShowRawFeed_Click(object sender, RoutedEventArgs e)
    {
        var article = _viewModel?.SelectedArticle;
        if (article?.FeedId is null || _viewModel is null)
        {
            return;
        }

        try
        {
            var rawContent = await _viewModel.GetRawArticleContentAsync(article);
            var dialog = new RawFeedWindow(article.Source, rawContent) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageDialogWindow.Show(
                this,
                $"Raw feed content could not be loaded.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private Task<string?> ShowFolderSelectionAsync(
        IReadOnlyList<string> folders,
        IReadOnlyList<string> suggestions,
        Func<string, Task> createFolderAsync)
    {
        var dialog = new FolderSelectionWindow(
            folders,
            suggestions,
            createFolderAsync)
        {
            Owner = this
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.SelectedFolder : null);
    }

    private async void ImportOpml_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import feed list",
            Filter = "OPML files (*.opml;*.xml)|*.opml;*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        using var stream = dialog.OpenFile();
        await catalogManagement.ImportOpmlAsync(stream);
    }

    private void AddFeed_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Feed);

    private void EditFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFeedListItem feed } ||
            DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var feedList = (ListBox?)FindName("CatalogManagementFeedList");
        var scrollOffset = feedList is null
            ? null
            : FindVisualDescendant<ScrollViewer>(feedList)?.VerticalOffset;
        catalogManagement.PrepareFeedEdit(feed);
        ShowCatalogEntry(CatalogEntryKind.Feed, isEditingFeed: true);

        if (scrollOffset is { } offset)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (FindName("CatalogManagementFeedList") is ListBox updatedFeedList)
                {
                    FindVisualDescendant<ScrollViewer>(updatedFeedList)?.ScrollToVerticalOffset(offset);
                }
            }));
        }
    }

    private void PreviewManagedFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFeedListItem feed } ||
            DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var dialog = new CatalogFeedPreviewWindow(
            feed,
            token => catalogManagement.PreviewFeedAsync(feed, token),
            () => catalogManagement.DeleteFeedCommand.ExecuteAsync(feed),
            () => catalogManagement.CheckFeedHealthCommand.ExecuteAsync(feed))
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void PreviewCatalogFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFeedListItem feed } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new CatalogFeedPreviewWindow(
            feed,
            token => viewModel.LoadCatalogFeedPreviewAsync(feed, token))
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void RenameCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogCategory category } ||
            DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        catalogManagement.PrepareCategoryEdit(category);
        ShowCatalogEntry(CatalogEntryKind.Category, isEditingCategory: true);
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Category);

    private void AddCollection_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Collection);

    private void ShowCatalogEntry(
        CatalogEntryKind entryKind,
        bool isEditingFeed = false,
        bool isEditingCategory = false)
    {
        if (DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var dialog = new CatalogEntryWindow(catalogManagement, entryKind, isEditingFeed, isEditingCategory)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match)
        {
            return match;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (FindVisualDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}