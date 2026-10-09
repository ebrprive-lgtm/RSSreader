using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using RssReader.App.ViewModels;

namespace RssReader.App;

public partial class ArticleReaderView : UserControl
{
    private MainWindowViewModel? _viewModel;

    public ArticleReaderView()
    {
        InitializeComponent();
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

        _viewModel = e.NewValue as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        UpdateSelectedArticleContent();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SelectedArticle) or
            nameof(MainWindowViewModel.LimitArticleWidth))
        {
            UpdateSelectedArticleContent();
        }
    }

    private void UpdateSelectedArticleContent()
    {
        var article = _viewModel?.SelectedArticle;
        SelectedArticleHtmlViewer.SetArticle(
            article?.Content,
            article?.Summary,
            article?.Link,
            article?.FeedUrl,
            _viewModel?.LimitArticleWidth ?? true,
            article?.CardImageUrl);
    }

    private void SourceLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        SourceLinkRequested?.Invoke(this, e);
        e.Handled = true;
    }

    private void ShowRawFeed_Click(object sender, RoutedEventArgs e) =>
        RawFeedRequested?.Invoke(sender, e);

    private void SelectedArticleHtmlViewer_ExternalLinkRequested(Uri uri) =>
        ExternalLinkRequested?.Invoke(uri);
}
