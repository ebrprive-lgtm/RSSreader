using System.Windows;
using System.Runtime.InteropServices;
using RssReader.App.ViewModels;

namespace RssReader.App;

public partial class CatalogFeedPreviewWindow : Window
{
    private readonly Func<CancellationToken, Task<CatalogFeedPreview>> _loadPreview;
    private readonly Func<CancellationToken, Task<string>>? _loadRawFeedXml;
    private readonly Func<Task>? _deleteFeed;
    private readonly Func<Task>? _checkFeed;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _feedUrl;

    public CatalogFeedPreviewWindow(
        CatalogFeedListItem feed,
        Func<CancellationToken, Task<CatalogFeedPreview>> loadPreview,
        Func<Task>? deleteFeed = null,
        Func<Task>? checkFeed = null,
        Func<CancellationToken, Task<string>>? loadRawFeedXml = null)
    {
        InitializeComponent();
        _loadPreview = loadPreview;
        _loadRawFeedXml = loadRawFeedXml;
        _deleteFeed = deleteFeed;
        _checkFeed = checkFeed;
        _feedUrl = feed.FeedUrl;
        FeedNameText.Text = feed.Name;
        FeedCategoryText.Text = feed.CategoryName ?? "Uncategorized";
        if (deleteFeed is not null)
        {
            DeleteCatalogFeedButton.Visibility = Visibility.Visible;
        }

        if (checkFeed is not null)
        {
            CheckFeedButton.Visibility = Visibility.Visible;
        }

        FeedDescriptionText.Text = feed.Description ?? string.Empty;
        if (string.IsNullOrWhiteSpace(feed.Description))
        {
            FeedDescriptionText.Visibility = Visibility.Collapsed;
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var preview = await _loadPreview(_cancellation.Token);
            PreviewItemsList.ItemsSource = preview.Items;
            EmptyStateText.Visibility = preview.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessageText.Text = $"Could not load feed preview: {exception.Message}";
            ErrorMessageText.Visibility = Visibility.Visible;
            ShowRawXmlButton.Visibility = _loadRawFeedXml is null ? Visibility.Collapsed : Visibility.Visible;
        }
        finally
        {
            LoadingProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void ShowRawXmlButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadRawFeedXml is null)
        {
            return;
        }

        ShowRawXmlButton.IsEnabled = false;
        try
        {
            var rawXml = await _loadRawFeedXml(_cancellation.Token);
            var dialog = new RawFeedWindow(_feedUrl, rawXml) { Owner = this };
            dialog.Show();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessageText.Text += $"{Environment.NewLine}{Environment.NewLine}Could not load feed XML: {exception.Message}";
            ErrorMessageText.Visibility = Visibility.Visible;
        }
        finally
        {
            ShowRawXmlButton.IsEnabled = true;
        }
    }

    private void CopyErrorMessage_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ErrorMessageText.Text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(ErrorMessageText.Text);
        }
        catch (ExternalException exception)
        {
            System.Diagnostics.Trace.TraceError($"Could not copy the feed preview error to the clipboard: {exception}");
        }
    }

    private async void CheckFeedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_checkFeed is null)
        {
            return;
        }

        Close();
        await _checkFeed();
    }

    private async void DeleteFeedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_deleteFeed is null)
        {
            return;
        }

        DeleteCatalogFeedButton.IsEnabled = false;
        try
        {
            await _deleteFeed();
            Close();
        }
        catch (Exception exception)
        {
            ErrorMessageText.Text = $"Could not delete feed from catalog: {exception.Message}";
            ErrorMessageText.Visibility = Visibility.Visible;
            DeleteCatalogFeedButton.IsEnabled = true;
        }
    }
}