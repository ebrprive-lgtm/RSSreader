using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RssReader.App.ViewModels;
using RssReader.Domain;

namespace RssReader.App;

public partial class ArticleReaderView : UserControl
{
    private MainWindowViewModel? _viewModel;
    private ArticleRowViewModel? _selectedArticle;
    private bool _isUpdatingTypographyControls;
    private Task? _publisherBrowserInitializationTask;

    public ArticleReaderView()
    {
        InitializeComponent();
        PublisherWebsiteBrowser.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RssReader",
                "PublisherBrowserWebView2")
        };
        DataContextChanged += ArticleReaderView_DataContextChanged;
        SelectedArticleHtmlViewer.ExternalLinkRequested += SelectedArticleHtmlViewer_ExternalLinkRequested;
    }

    public event EventHandler<RequestNavigateEventArgs>? SourceLinkRequested;
    public event RoutedEventHandler? RawFeedRequested;
    public event Action<Uri>? ExternalLinkRequested;

    private void ArticleReaderView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        SetSelectedArticleSubscription(null);
        _viewModel = e.NewValue as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            SetSelectedArticleSubscription(_viewModel.SelectedArticle);
        }

        UpdateSelectedArticleContent();
        if (_viewModel?.IsInAppPublisherBrowserVisible == true)
        {
            _ = OpenPublisherWebsiteAsync();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedArticle))
        {
            SetSelectedArticleSubscription(_viewModel?.SelectedArticle);
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsInAppPublisherBrowserVisible))
        {
            if (_viewModel?.IsInAppPublisherBrowserVisible == true)
            {
                _ = OpenPublisherWebsiteAsync();
            }
            else
            {
                PublisherWebsiteBrowser.CoreWebView2?.Stop();
            }
        }

        if (e.PropertyName is nameof(MainWindowViewModel.SelectedArticle) or
            nameof(MainWindowViewModel.ReaderContentPreferences))
        {
            if (_viewModel?.SelectedArticle is null)
            {
                ReaderTypographyPopup.IsOpen = false;
            }

            UpdateSelectedArticleContent();
        }
    }

    private void SetSelectedArticleSubscription(ArticleRowViewModel? article)
    {
        if (_selectedArticle is not null)
        {
            _selectedArticle.PropertyChanged -= SelectedArticle_PropertyChanged;
        }

        _selectedArticle = article;
        if (_selectedArticle is not null)
        {
            _selectedArticle.PropertyChanged += SelectedArticle_PropertyChanged;
        }
    }

    private void SelectedArticle_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArticleRowViewModel.ReaderContent))
        {
            UpdateSelectedArticleContent();
        }
    }

    private void UpdateSelectedArticleContent()
    {
        UpdateReaderTypographyControls();
        var article = _viewModel?.SelectedArticle;
        var isWaitingForFullArticle = article?.IsWaitingForFullArticle == true;
        if (Uri.TryCreate(article?.Link, UriKind.Absolute, out var articleUri) &&
            (articleUri.DnsSafeHost.Equals("gamesradar.com", StringComparison.OrdinalIgnoreCase) ||
             articleUri.DnsSafeHost.EndsWith(".gamesradar.com", StringComparison.OrdinalIgnoreCase)))
        {
            var contentSource = isWaitingForFullArticle
                ? "waiting for extracted content"
                : article?.IsShowingExtractedContent == true
                    ? "extracted content"
                    : "feed preview";
            Trace.WriteLine(
                $"[GameRadar] Reader input: source={contentSource}; " +
                $"hasExtractedContent={article?.HasExtractedContent}; " +
                $"retrievedAt={article?.ExtractedContentRetrievedAt:O}; " +
                $"htmlChars={article?.ReaderContent?.Length ?? 0}; " +
                $"url={articleUri.GetLeftPart(UriPartial.Path)}.");
        }

        SelectedArticleHtmlViewer.SetArticle(
            isWaitingForFullArticle ? null : article?.ReaderContent,
            isWaitingForFullArticle ? null : article?.Summary,
            article?.Link,
            article?.FeedUrl,
            _viewModel?.LimitArticleWidth ?? true,
            isWaitingForFullArticle ? null : article?.CardImageUrl,
            _viewModel?.ReaderTheme ?? ProfileReaderTheme.Light,
            _viewModel?.ReaderTextSize ?? ProfileReaderTextSize.Medium,
            _viewModel?.ReaderLineSpacing ?? ProfileReaderLineSpacing.Normal,
            _viewModel?.ReaderFontFamily ?? ProfileReaderFontFamily.SansSerif);
    }

    private void ReaderTypographyButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateReaderTypographyControls();
        ReaderTypographyPopup.IsOpen = true;
    }

    private async void ReaderTypographyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingTypographyControls || _viewModel is null)
        {
            return;
        }

        await _viewModel.ApplyReaderTypographyAsync(
            GetSelectedOption<ProfileReaderTextSize>(ReaderTextSizeComboBox),
            GetSelectedOption<ProfileReaderLineSpacing>(ReaderLineSpacingComboBox),
            GetSelectedOption<ProfileReaderFontFamily>(ReaderFontFamilyComboBox));
    }

    private void UpdateReaderTypographyControls()
    {
        if (_viewModel is null)
        {
            return;
        }

        _isUpdatingTypographyControls = true;
        try
        {
            SelectOption(ReaderTextSizeComboBox, _viewModel.ReaderTextSize);
            SelectOption(ReaderLineSpacingComboBox, _viewModel.ReaderLineSpacing);
            SelectOption(ReaderFontFamilyComboBox, _viewModel.ReaderFontFamily);
        }
        finally
        {
            _isUpdatingTypographyControls = false;
        }
    }

    private static void SelectOption<T>(ComboBox comboBox, T value)
        where T : struct, Enum =>
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .Single(item => int.Parse((string)item.Tag, CultureInfo.InvariantCulture) == Convert.ToInt32(value, CultureInfo.InvariantCulture));

    private static T GetSelectedOption<T>(ComboBox comboBox)
        where T : struct, Enum
    {
        if (comboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            throw new InvalidOperationException($"No {typeof(T).Name} option is selected.");
        }

        return (T)Enum.ToObject(
            typeof(T),
            int.Parse(tag, CultureInfo.InvariantCulture));
    }

    private void SourceLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        SourceLinkRequested?.Invoke(this, e);
        e.Handled = true;
    }

    private void ShowRawFeed_Click(object sender, RoutedEventArgs e) =>
        RawFeedRequested?.Invoke(sender, e);

    private void OpenSelectedArticleActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } contextMenu } actionButton)
        {
            contextMenu.PlacementTarget = actionButton;
            contextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void SelectedArticleHtmlViewer_ExternalLinkRequested(Uri uri) =>
        ExternalLinkRequested?.Invoke(uri);

    private async Task OpenPublisherWebsiteAsync()
    {
        var viewModel = _viewModel;
        var selectedArticle = viewModel?.SelectedArticle;
        if (selectedArticle?.Link is not { } articleUrl ||
            !TryCreateWebUri(articleUrl, out var articleUri))
        {
            PublisherWebsiteStatus.Text = "The selected article does not have a valid HTTP or HTTPS address.";
            return;
        }

        PublisherWebsiteStatus.Text = "Opening publisher website...";
        try
        {
            _publisherBrowserInitializationTask ??= InitializePublisherBrowserAsync();
            await _publisherBrowserInitializationTask;
        }
        catch (Exception exception)
        {
            _publisherBrowserInitializationTask = null;
            Trace.WriteLine($"WebView2 publisher browser initialization failed: {exception}");
            PublisherWebsiteStatus.Text =
                "The in-app browser could not be started. Return to the reader and use Open in browser instead.";
            return;
        }

        if (viewModel is null ||
            !viewModel.IsInAppPublisherBrowserVisible ||
            !ReferenceEquals(viewModel.SelectedArticle, selectedArticle) ||
            PublisherWebsiteBrowser.CoreWebView2 is not { } coreWebView)
        {
            return;
        }

        try
        {
            coreWebView.Navigate(articleUri.AbsoluteUri);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WebView2 publisher browser navigation failed: {exception}");
            PublisherWebsiteStatus.Text = "The publisher website could not be opened in the in-app browser.";
        }
    }

    private async Task InitializePublisherBrowserAsync()
    {
        Directory.CreateDirectory(PublisherWebsiteBrowser.CreationProperties.UserDataFolder);
        await PublisherWebsiteBrowser.EnsureCoreWebView2Async();

        var coreWebView = PublisherWebsiteBrowser.CoreWebView2;
        coreWebView.Settings.IsScriptEnabled = true;
        coreWebView.Settings.IsWebMessageEnabled = false;
        coreWebView.Settings.AreDefaultScriptDialogsEnabled = false;
        PublisherWebsiteBrowser.NavigationStarting += PublisherWebsiteBrowser_NavigationStarting;
        coreWebView.NewWindowRequested += PublisherWebsiteBrowser_NewWindowRequested;
        coreWebView.NavigationCompleted += PublisherWebsiteBrowser_NavigationCompleted;
    }

    private void PublisherWebsiteBrowser_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (_viewModel?.IsInAppPublisherBrowserVisible != true)
        {
            e.Cancel = true;
            return;
        }

        if (!TryCreateWebUri(e.Uri, out _))
        {
            e.Cancel = true;
            PublisherWebsiteStatus.Text = "Only HTTP and HTTPS pages can be opened in the in-app browser.";
            return;
        }

        PublisherWebsiteStatus.Text = "Loading publisher website...";
    }

    private void PublisherWebsiteBrowser_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (_viewModel?.IsInAppPublisherBrowserVisible != true ||
            !e.IsUserInitiated ||
            !TryCreateWebUri(e.Uri, out var uri))
        {
            PublisherWebsiteStatus.Text = "The requested popup was blocked; only user-initiated HTTP and HTTPS pages are allowed.";
            return;
        }

        try
        {
            PublisherWebsiteBrowser.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"WebView2 publisher popup navigation failed: {exception}");
            PublisherWebsiteStatus.Text = "The publisher link could not be opened in the in-app browser.";
        }
    }

    private void PublisherWebsiteBrowser_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_viewModel?.IsInAppPublisherBrowserVisible != true)
        {
            return;
        }

        if (e.HttpStatusCode == 403)
        {
            PublisherWebsiteStatus.Text =
                "The publisher returned HTTP 403 in the in-app browser. Access may still be blocked; try your regular browser.";
        }
        else if (e.HttpStatusCode >= 400)
        {
            PublisherWebsiteStatus.Text = $"The publisher returned HTTP {e.HttpStatusCode}.";
        }
        else if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            PublisherWebsiteStatus.Text =
                $"The publisher website could not be loaded ({e.WebErrorStatus}). Try your regular browser.";
        }
        else if (e.IsSuccess)
        {
            PublisherWebsiteStatus.Text = string.Empty;
        }
    }

    private static bool TryCreateWebUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            (string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(candidate.Host))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }
}
