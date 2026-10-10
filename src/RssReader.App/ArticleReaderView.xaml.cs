using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using RssReader.App.ViewModels;
using RssReader.Domain;

namespace RssReader.App;

public partial class ArticleReaderView : UserControl
{
    private MainWindowViewModel? _viewModel;
    private bool _isUpdatingTypographyControls;

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
            nameof(MainWindowViewModel.ReaderContentPreferences))
        {
            if (_viewModel?.SelectedArticle is null)
            {
                ReaderTypographyPopup.IsOpen = false;
            }

            UpdateSelectedArticleContent();
        }
    }

    private void UpdateSelectedArticleContent()
    {
        UpdateReaderTypographyControls();
        var article = _viewModel?.SelectedArticle;
        SelectedArticleHtmlViewer.SetArticle(
            article?.Content,
            article?.Summary,
            article?.Link,
            article?.FeedUrl,
            _viewModel?.LimitArticleWidth ?? true,
            article?.CardImageUrl,
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
}
