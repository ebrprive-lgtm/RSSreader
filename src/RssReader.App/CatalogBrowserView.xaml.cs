using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using RssReader.App.ViewModels;
using RssReader.Domain;

namespace RssReader.App;

public partial class CatalogBrowserView : UserControl
{
    private CancellationTokenSource? _selectedFeedPreviewCancellation;

    public CatalogBrowserView()
    {
        InitializeComponent();
    }

    public event EventHandler<RequestNavigateEventArgs>? SourceLinkRequested;

    private async void AddPersonalFeed_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsPersonalFeedManagementVisible)
        {
            return;
        }

        var dialog = new ProfileFeedEntryWindow(viewModel.FolderNames) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await viewModel.AddPersonalFeedAsync(
                dialog.FeedName,
                dialog.FeedUrl,
                categoryName: dialog.CategoryName);
        }
        catch (Exception exception)
        {
            MessageDialogWindow.Show(
                Window.GetWindow(this),
                $"The personal feed could not be added.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ImportPersonalOpml_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsPersonalFeedManagementVisible)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var dialog = new OpenFileDialog
        {
            Title = "Import feeds to your profile",
            Filter = "OPML files (*.opml;*.xml)|*.opml;*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            await using var stream = dialog.OpenFile();
            var result = await viewModel.ImportPersonalFeedsAsync(stream);
            MessageDialogWindow.Show(
                owner,
                $"Added {result.AddedCount} feed(s). Skipped {result.DuplicateCount} duplicate(s) and {result.SkippedCount} invalid or unsupported outline(s).",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageDialogWindow.Show(
                owner,
                $"The OPML file could not be imported.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportPersonalOpml_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsPersonalFeedManagementVisible)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var dialog = new SaveFileDialog
        {
            Title = "Export your feeds",
            Filter = "OPML files (*.opml)|*.opml",
            DefaultExt = ".opml",
            AddExtension = true,
            FileName = "rss-reader-feeds.opml"
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            var opml = await viewModel.ExportPersonalFeedsAsync();
            await File.WriteAllTextAsync(dialog.FileName, opml, new UTF8Encoding(false));
        }
        catch (Exception exception)
        {
            MessageDialogWindow.Show(
                owner,
                $"Your feeds could not be exported.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
            token => viewModel.LoadCatalogFeedPreviewAsync(feed, token),
            loadRawFeedXml: token => viewModel.LoadCatalogFeedRawXmlAsync(feed, token))
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    private async void CatalogFeedResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await LoadSelectedFeedPreviewAsync(viewModel, viewModel.SelectedCatalogFeed);
    }

    private async Task LoadSelectedFeedPreviewAsync(
        MainWindowViewModel viewModel,
        CatalogFeedListItem? feed,
        bool forceRefresh = false)
    {
        var previousCancellation = _selectedFeedPreviewCancellation;
        _selectedFeedPreviewCancellation = null;
        previousCancellation?.Cancel();

        SelectedFeedPreviewItems.ItemsSource = null;
        SelectedFeedPreviewEmptyText.Visibility = Visibility.Collapsed;
        SelectedFeedPreviewUnavailableText.Visibility = Visibility.Collapsed;
        SelectedFeedPreviewErrorPanel.Visibility = Visibility.Collapsed;
        SelectedFeedPreviewLoadingBar.Visibility = Visibility.Collapsed;

        if (feed is null)
        {
            return;
        }

        if (!viewModel.IsCatalogFeedPreviewAvailable)
        {
            SelectedFeedPreviewUnavailableText.Visibility = Visibility.Visible;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _selectedFeedPreviewCancellation = cancellation;
        SelectedFeedPreviewLoadingBar.Visibility = Visibility.Visible;
        try
        {
            var preview = await viewModel.LoadCatalogFeedPreviewAsync(
                feed,
                cancellation.Token,
                forceRefresh);
            if (cancellation.IsCancellationRequested ||
                !ReferenceEquals(viewModel.SelectedCatalogFeed, feed))
            {
                return;
            }

            SelectedFeedPreviewItems.ItemsSource = preview.Items;
            SelectedFeedPreviewEmptyText.Visibility = preview.Items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(viewModel.SelectedCatalogFeed, feed))
            {
                SelectedFeedPreviewErrorText.Text = $"Could not load recent articles: {exception.Message}";
                SelectedFeedPreviewErrorPanel.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (ReferenceEquals(_selectedFeedPreviewCancellation, cancellation))
            {
                _selectedFeedPreviewCancellation = null;
                SelectedFeedPreviewLoadingBar.Visibility = Visibility.Collapsed;
            }

            cancellation.Dispose();
        }
    }

    private async void RetrySelectedFeedPreview_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.SelectedCatalogFeed is { } feed)
        {
            await LoadSelectedFeedPreviewAsync(viewModel, feed, forceRefresh: true);
        }
    }

    private void CopyFeedUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFeedListItem feed })
        {
            return;
        }

        CopyFeedUrl(feed);
    }

    private void CopySelectedFeedUrl_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { SelectedCatalogFeed: { } feed })
        {
            CopyFeedUrl(feed);
        }
    }

    private void CopyFeedUrl(CatalogFeedListItem feed)
    {
        try
        {
            Clipboard.SetText(feed.FeedUrl);
        }
        catch (ExternalException exception)
        {
            MessageDialogWindow.Show(
                Window.GetWindow(this),
                $"The feed URL could not be copied.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SourceLink_RequestNavigate(object? sender, RequestNavigateEventArgs e) =>
        SourceLinkRequested?.Invoke(this, e);
}
