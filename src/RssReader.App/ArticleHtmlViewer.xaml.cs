using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RssReader.Domain;

namespace RssReader.App;

public partial class ArticleHtmlViewer : UserControl
{
    private readonly DispatcherTimer _resizeRedrawTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private string _document = ArticleHtmlDocumentBuilder.Build(null, null, null, null);
    private string? _fallbackText;
    private bool _isInitialized;
    private bool _isInitializing;
    private bool _isUnloaded;
    private Task _initializationTask = Task.CompletedTask;
    private bool _isArticleDocumentNavigationPending;
    private ulong? _articleDocumentNavigationId;
    private readonly TaskCompletionSource _navigationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ArticleHtmlViewer()
    {
        InitializeComponent();
        Browser.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RssReader",
                "WebView2")
        };
        _resizeRedrawTimer.Tick += ResizeRedrawTimer_Tick;
        SizeChanged += ArticleHtmlViewer_SizeChanged;
        Loaded += ArticleHtmlViewer_Loaded;
        Unloaded += ArticleHtmlViewer_Unloaded;
    }

    public event Action<Uri>? ExternalLinkRequested;
    internal event Action? ArticleDocumentNavigationCompleted;

    internal string CurrentDocument => _document;
    internal Task InitializationTask => _initializationTask;
    internal Exception? InitializationError { get; private set; }
    internal Task NavigationCompletion => _navigationCompletion.Task;
    internal string? LastNavigationUriScheme { get; private set; }
    internal bool? LastNavigationSucceeded { get; private set; }
    internal CoreWebView2WebErrorStatus? LastNavigationErrorStatus { get; private set; }
    internal int DocumentNavigationCount { get; private set; }

    public void SetArticle(
        string? content,
        string? summary,
        string? articleUrl,
        string? feedUrl,
        bool limitArticleWidth = true,
        string? imageUrl = null,
        ProfileReaderTheme readerTheme = ProfileReaderTheme.Light,
        ProfileReaderTextSize textSize = ProfileReaderTextSize.Medium,
        ProfileReaderLineSpacing lineSpacing = ProfileReaderLineSpacing.Normal,
        ProfileReaderFontFamily fontFamily = ProfileReaderFontFamily.SansSerif)
    {
        _fallbackText = summary;
        var document = ArticleHtmlDocumentBuilder.Build(
            content,
            summary,
            articleUrl,
            feedUrl,
            limitArticleWidth,
            imageUrl,
            readerTheme,
            textSize,
            lineSpacing,
            fontFamily);
        if (string.Equals(_document, document, StringComparison.Ordinal))
        {
            return;
        }

        _document = document;
        if (_isInitialized && !_isUnloaded)
        {
            _resizeRedrawTimer.Stop();
            NavigateArticleDocument();
        }
    }

    private void ArticleHtmlViewer_Loaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        if (_isInitialized)
        {
            NavigateArticleDocument();
            return;
        }

        if (_isInitializing)
        {
            return;
        }

        _initializationTask = InitializeBrowserAsync();
    }

    private async Task InitializeBrowserAsync()
    {
        _isInitializing = true;
        try
        {
            Directory.CreateDirectory(Browser.CreationProperties.UserDataFolder);
            await Browser.EnsureCoreWebView2Async();
            if (_isUnloaded)
            {
                return;
            }

            var core = Browser.CoreWebView2;
            core.Settings.IsScriptEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            Browser.NavigationStarting += Browser_NavigationStarting;
            core.NewWindowRequested += CoreWebView2_NewWindowRequested;
            core.NavigationCompleted += CoreWebView2_NavigationCompleted;

            _isInitialized = true;
            Browser.Visibility = Visibility.Visible;
            StatusPanel.Visibility = Visibility.Collapsed;
            NavigateArticleDocument();
        }
        catch (Exception exception)
        {
            InitializationError = exception;
            Trace.WriteLine($"WebView2 article viewer initialization failed: {exception}");
            StatusMessage.ToolTip = exception.Message;
            ShowFallback("The full article viewer is unavailable.");
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void ArticleHtmlViewer_Unloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _resizeRedrawTimer.Stop();
        _isArticleDocumentNavigationPending = false;
        _articleDocumentNavigationId = null;
        if (_isInitialized)
        {
            Browser.CoreWebView2?.Stop();
        }
    }

    private void ArticleHtmlViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || !_isInitialized || _isUnloaded)
        {
            return;
        }

        _resizeRedrawTimer.Stop();
        _resizeRedrawTimer.Start();
    }

    private void ResizeRedrawTimer_Tick(object? sender, EventArgs e)
    {
        _resizeRedrawTimer.Stop();
        if (_isInitialized && !_isUnloaded && Browser.CoreWebView2 is not null)
        {
            NavigateArticleDocument();
        }
    }

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        LastNavigationUriScheme = Uri.TryCreate(e.Uri, UriKind.Absolute, out var navigationUri)
            ? navigationUri.Scheme
            : null;
        Trace.WriteLine($"WebView2 article navigation starting: {LastNavigationUriScheme}");
        if (_isArticleDocumentNavigationPending &&
            !e.IsUserInitiated &&
            string.Equals(LastNavigationUriScheme, "data", StringComparison.OrdinalIgnoreCase))
        {
            _isArticleDocumentNavigationPending = false;
            _articleDocumentNavigationId = e.NavigationId;
            return;
        }

        if (string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        if (e.IsUserInitiated && TryCreateWebUri(e.Uri, out var uri))
        {
            ExternalLinkRequested?.Invoke(uri);
        }
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.IsUserInitiated && TryCreateWebUri(e.Uri, out var uri))
        {
            ExternalLinkRequested?.Invoke(uri);
        }
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_isUnloaded || _articleDocumentNavigationId != e.NavigationId)
        {
            return;
        }

        _articleDocumentNavigationId = null;
        LastNavigationSucceeded = e.IsSuccess;
        LastNavigationErrorStatus = e.WebErrorStatus;
        _navigationCompletion.TrySetResult();
        ArticleDocumentNavigationCompleted?.Invoke();
        HandleNavigationResult(e.IsSuccess, e.WebErrorStatus);
    }

    internal void HandleNavigationResult(bool isSuccess, CoreWebView2WebErrorStatus webErrorStatus)
    {
        if (!isSuccess && webErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            return;
        }

        if (!isSuccess &&
            webErrorStatus == CoreWebView2WebErrorStatus.ConnectionAborted &&
            (_isArticleDocumentNavigationPending || _resizeRedrawTimer.IsEnabled))
        {
            return;
        }

        if (!isSuccess)
        {
            var message = $"The article content could not be loaded ({webErrorStatus}).";
            Trace.WriteLine($"WebView2 article navigation failed: {message}; scheme: {LastNavigationUriScheme}");
            StatusMessage.ToolTip = message;
            ShowFallback("The article content could not be loaded.");
            return;
        }

        Browser.Visibility = Visibility.Visible;
        StatusPanel.Visibility = Visibility.Collapsed;
        StatusMessage.ToolTip = null;
    }

    private void ShowFallback(string message)
    {
        Browser.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
        StatusMessage.Text = message;
        FallbackText.Text = _fallbackText;
        FallbackText.Visibility = string.IsNullOrWhiteSpace(_fallbackText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void NavigateArticleDocument()
    {
        _articleDocumentNavigationId = null;
        _isArticleDocumentNavigationPending = true;
        DocumentNavigationCount++;
        try
        {
            Browser.CoreWebView2.NavigateToString(_document);
        }
        catch
        {
            _isArticleDocumentNavigationPending = false;
            throw;
        }
    }

    private static bool TryCreateWebUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsedUri) &&
            (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsedUri;
            return true;
        }

        uri = null!;
        return false;
    }
}