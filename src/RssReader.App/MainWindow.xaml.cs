using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Input;
using RssReader.App.ViewModels;
using RssReader.Domain;

namespace RssReader.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _autoRefreshTimer = new(DispatcherPriority.Background);
    private readonly WindowPlacementStore _windowPlacementStore;
    private MainWindowViewModel? _viewModel;
    private ArticleRowViewModel? _articleToRestoreFocus;
    private string? _articleRouteWhenOpened;

    public MainWindow() : this(new MainWindowViewModel(Profile.CreateRegular("Reader")))
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this(viewModel, new WindowPlacementStore())
    {
    }

    internal MainWindow(MainWindowViewModel viewModel, WindowPlacementStore windowPlacementStore)
    {
        AppThemeManager.Apply(viewModel.ReaderTheme);
        InitializeComponent();
        _windowPlacementStore = windowPlacementStore;
        _viewModel = viewModel;
        DataContext = viewModel;
        UpdateSplitPaneColumnWidths();
        RestoreWindowPlacement(viewModel);
        viewModel.FolderSelectionRequested = ShowFolderSelectionAsync;
        viewModel.ConfirmUnfollowRequested = ConfirmUnfollowAsync;
        viewModel.ConfirmUnfollowAllRequested = ConfirmUnfollowAllAsync;
        viewModel.ConfirmDeleteFolderRequested = ConfirmDeleteFolderAsync;
        if (viewModel.CatalogManagement is { } catalogManagement)
        {
            catalogManagement.ConfirmDeleteRequested = ConfirmCatalogDeletionAsync;
        }

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        viewModel.OriginalArticleRequested += OpenExternalUri;
        SidebarView.LogoutRequested += SidebarView_LogoutRequested;
        SidebarView.PreferencesRequested += SidebarView_PreferencesRequested;
        ArticleReaderView.SourceLinkRequested += SourceLink_RequestNavigate;
        CatalogAdminView.SourceLinkRequested += SourceLink_RequestNavigate;
        ArticleReaderView.RawFeedRequested += ShowRawFeed_Click;
        ArticleReaderView.ExternalLinkRequested += ArticleHtmlViewer_ExternalLinkRequested;
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        Closing += (_, _) => SaveWindowPlacement();
        Closed += (_, _) =>
        {
            _autoRefreshTimer.Stop();
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            viewModel.OriginalArticleRequested -= OpenExternalUri;
            SidebarView.LogoutRequested -= SidebarView_LogoutRequested;
            SidebarView.PreferencesRequested -= SidebarView_PreferencesRequested;
            ArticleReaderView.SourceLinkRequested -= SourceLink_RequestNavigate;
            CatalogAdminView.SourceLinkRequested -= SourceLink_RequestNavigate;
            ArticleReaderView.RawFeedRequested -= ShowRawFeed_Click;
            ArticleReaderView.ExternalLinkRequested -= ArticleHtmlViewer_ExternalLinkRequested;
        };
        UpdateSidebarPresentation();
        ConfigureAutoRefreshTimer();
    }

    private void RestoreWindowPlacement(MainWindowViewModel viewModel)
    {
        var placement = _windowPlacementStore.Load();
        if (placement is null)
        {
            return;
        }

        viewModel.SetSidebarPanelWidth(placement.SidebarWidth ?? MainWindowViewModel.DefaultSidebarWidth);
        var visibleArea = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        if (!placement.TryGetVisibleBounds(visibleArea, MinWidth, MinHeight, out var bounds))
        {
            return;
        }

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (bounds.IsEmpty ||
            !double.IsFinite(bounds.Left) ||
            !double.IsFinite(bounds.Top) ||
            !double.IsFinite(bounds.Width) ||
            !double.IsFinite(bounds.Height))
        {
            return;
        }

        try
        {
            _windowPlacementStore.Save(new WindowPlacement(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                WindowState == WindowState.Maximized,
                _viewModel?.SidebarPanelWidth ?? MainWindowViewModel.DefaultSidebarWidth));
        }
        catch (IOException exception)
        {
            ShowWindowPlacementSaveError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowWindowPlacementSaveError(exception);
        }
    }

    private void ShowWindowPlacementSaveError(Exception exception)
    {
        Trace.TraceError($"Could not save main-window placement: {exception}");
        MessageDialogWindow.Show(
            this,
            $"The main window position and size could not be saved.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
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

    private Task<bool> ConfirmUnfollowAsync(string feedName)
    {
        var result = MessageDialogWindow.Show(
            this,
            $"Unfollow '{feedName}'? If this is your personal feed and no other profile uses it, its cached articles will also be removed.",
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

    private Task<bool> ConfirmCatalogDeletionAsync(string message)
    {
        var result = MessageDialogWindow.Show(this, message, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public event Action? LogoutRequested;

    public event Action? PreferencesRequested;

    private void SidebarView_LogoutRequested() => LogoutRequested?.Invoke();

    private void SidebarView_PreferencesRequested() => PreferencesRequested?.Invoke();

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized;

    private void RestoreWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Normal;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        var isMaximized = WindowState == WindowState.Maximized;
        WindowResizeFrame.CornerRadius = new CornerRadius(isMaximized ? 0 : 18);
        MainWindowSurface.Margin = isMaximized ? new Thickness(0) : new Thickness(6);
        MainWindowSurface.CornerRadius = new CornerRadius(isMaximized ? 0 : 18);
        MainWindowSurface.BorderThickness = isMaximized ? new Thickness(0) : new Thickness(1);
        UpdateShellGridClip();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.K ||
            Keyboard.Modifiers != ModifierKeys.Control ||
            e.OriginalSource is TextBoxBase or PasswordBox ||
            e.OriginalSource is ComboBox { IsEditable: true } ||
            _viewModel is null)
        {
            return;
        }

        var palette = new CommandPaletteWindow(_viewModel.CreateCommandPaletteItems())
        {
            Owner = this
        };
        palette.ShowDialog();
        e.Handled = true;
    }

    private void ShellGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateShellGridClip();
    }

    private void WorkspaceGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        _viewModel?.UpdateSplitPaneViewportWidth(e.NewSize.Width);

    private async void SplitPaneResizeSplitter_DragCompleted(object? sender, DragCompletedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var listWidth = ReadingWorkspaceGrid.ColumnDefinitions[0].ActualWidth;
        var readerWidth = ReadingWorkspaceGrid.ColumnDefinitions[2].ActualWidth;
        var totalWidth = listWidth + readerWidth;
        if (!double.IsFinite(totalWidth) || totalWidth <= 0)
        {
            return;
        }

        await _viewModel.UpdateSplitPaneListRatioAsync(listWidth / totalWidth);
    }

    private void UpdateSplitPaneColumnWidths()
    {
        if (_viewModel is null || ReadingWorkspaceGrid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        ReadingWorkspaceGrid.ColumnDefinitions[0].Width =
            new GridLength(_viewModel.SplitPaneListRatio, GridUnitType.Star);
        ReadingWorkspaceGrid.ColumnDefinitions[2].Width =
            new GridLength(1 - _viewModel.SplitPaneListRatio, GridUnitType.Star);
    }

    private void UpdateShellGridClip()
    {
        if (WindowState == WindowState.Maximized)
        {
            ShellGrid.Clip = null;
            return;
        }

        const double contentCornerRadius = 17;
        ShellGrid.Clip = new RectangleGeometry(
            new Rect(0, 0, ShellGrid.ActualWidth, ShellGrid.ActualHeight),
            contentCornerRadius,
            contentCornerRadius);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SplitPaneListRatio))
        {
            UpdateSplitPaneColumnWidths();
        }

        if (e.PropertyName == nameof(MainWindowViewModel.ReaderTheme) && _viewModel is not null)
        {
            AppThemeManager.Apply(_viewModel.ReaderTheme);
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedArticle))
        {
            if (_viewModel?.SelectedArticle is { } selectedArticle)
            {
                _articleToRestoreFocus = selectedArticle;
                _articleRouteWhenOpened = _viewModel.ActiveRoute;
            }
            else if (_articleToRestoreFocus is { } articleToRestore &&
                     string.Equals(_articleRouteWhenOpened, _viewModel?.ActiveRoute, StringComparison.Ordinal))
            {
                _articleToRestoreFocus = null;
                _articleRouteWhenOpened = null;
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(() => ArticleListView.RestoreArticleFocus(articleToRestore)));
            }
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

    private void ConfigureAutoRefreshTimer()
    {
        _autoRefreshTimer.Stop();
        if (_viewModel is null || _viewModel.AutoRefreshIntervalMinutes <= 0)
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

    private void UpdateSidebarPresentation() => SidebarView.UpdatePresentation();

    private void SourceLink_RequestNavigate(object? sender, RequestNavigateEventArgs e)
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
            var dialog = new RawFeedWindow(article.Source, rawContent.Xml, rawContent.IsCached) { Owner = this };
            dialog.Show();
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
}
