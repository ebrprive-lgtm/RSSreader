using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using RssReader.App.ViewModels;

namespace RssReader.App;

public partial class CatalogFeedScanProgressWindow : Window
{
    private readonly int _totalFeeds;
    private readonly Func<IProgress<CatalogFeedScanProgress>, CancellationToken, Task<int>> _scanFeeds;
    private readonly string _feedScope;
    private readonly CancellationTokenSource _cancellation = new();
    private int _completedCount;
    private bool _scanStarted;
    private bool _scanCompleted;

    public CatalogFeedScanProgressWindow(
        int totalFeeds,
        Func<IProgress<CatalogFeedScanProgress>, CancellationToken, Task<int>> scanFeeds,
        string feedScope = "visible feed sources")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalFeeds);
        ArgumentNullException.ThrowIfNull(scanFeeds);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedScope);
        InitializeComponent();
        _totalFeeds = totalFeeds;
        _scanFeeds = scanFeeds;
        _feedScope = feedScope;
        TitleText.Text = string.Equals(feedScope, "visible feed sources", StringComparison.Ordinal)
            ? "Check all visible feed sources"
            : $"Check {feedScope}";
        ScanProgressBar.Maximum = Math.Max(1, totalFeeds);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_scanStarted)
        {
            return;
        }

        _scanStarted = true;
        try
        {
            var progress = new Progress<CatalogFeedScanProgress>(UpdateProgress);
            var disabledCount = await _scanFeeds(progress, _cancellation.Token);
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Value = _totalFeeds;
            StatusText.Text = $"Checked {_totalFeeds} {_feedScope}; disabled {disabledCount} globally.";
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Value = _completedCount;
            StatusText.Text = $"Scan cancelled after checking {_completedCount} of {_totalFeeds} {_feedScope}.";
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Could not check catalog feeds ({_feedScope}): {exception}");
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Value = _completedCount;
            StatusText.Text = $"Scan stopped after checking {_completedCount} of {_totalFeeds} {_feedScope}.";
            ErrorText.Text = $"Could not complete the feed scan: {exception.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            _scanCompleted = true;
            CancelButton.Content = "Close";
            AutomationProperties.SetName(CancelButton, "Close feed scan");
            CancelButton.IsEnabled = true;
            CancelButton.Focus();
        }
    }

    private void UpdateProgress(CatalogFeedScanProgress progress)
    {
        _completedCount = progress.CompletedCount;
        ScanProgressBar.IsIndeterminate = false;
        ScanProgressBar.Maximum = Math.Max(1, progress.TotalCount);
        ScanProgressBar.Value = progress.CompletedCount;
        StatusText.Text =
            $"Checking feed {progress.CurrentFeedNumber} of {progress.TotalCount}: {progress.CurrentFeedName}";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanCompleted)
        {
            Close();
            return;
        }

        CancelScan();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_scanCompleted)
        {
            e.Cancel = true;
            CancelScan();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _cancellation.Dispose();
    }

    private void CancelScan()
    {
        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling feed scan...";
        _cancellation.Cancel();
    }
}
