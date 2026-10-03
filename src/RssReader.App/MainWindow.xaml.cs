using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        Closed += (_, _) =>
        {
            _autoRefreshTimer.Stop();
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        };
        UpdateSidebarPresentation();
        ConfigureAutoRefreshTimer();
    }

    public event Action? LogoutRequested;

    public event Action? PreferencesRequested;

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
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

        SidebarPinButton.ToolTip = _viewModel.IsSidebarPinned ? "Hide sidebar" : "Pin sidebar";
        SidebarPeekButton.ToolTip = _viewModel.IsSidebarPinned ? "Hide sidebar" : "Pin sidebar";
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

    private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e) => PreferencesRequested?.Invoke();

    private void LogoutMenuItem_Click(object sender, RoutedEventArgs e) => LogoutRequested?.Invoke();

    private void SourceLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.Scheme != Uri.UriSchemeHttp && e.Uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The source link could not be opened.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Open source",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ShowRawFeed_Click(object sender, RoutedEventArgs e)
    {
        var article = _viewModel?.SelectedArticle;
        if (article?.FeedId is not { } feedId || _viewModel is null)
        {
            return;
        }

        try
        {
            var rawContent = await _viewModel.GetRawFeedContentAsync(feedId);
            var dialog = new RawFeedWindow(article.Source, rawContent) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Raw feed content could not be loaded.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Show RAW",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private Task<string?> ShowFolderSelectionAsync(
        IReadOnlyList<string> folders,
        IReadOnlyList<string> suggestions,
        Func<string, Task> createFolderAsync)
    {
        var dialog = new FolderSelectionWindow(folders, suggestions, createFolderAsync)
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

        catalogManagement.PrepareFeedEdit(feed);
        ShowCatalogEntry(CatalogEntryKind.Feed, isEditingFeed: true);
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Category);

    private void AddCollection_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Collection);

    private void ShowCatalogEntry(CatalogEntryKind entryKind, bool isEditingFeed = false)
    {
        if (DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var dialog = new CatalogEntryWindow(catalogManagement, entryKind, isEditingFeed)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }
}