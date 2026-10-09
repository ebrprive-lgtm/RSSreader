using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RssReader.App.ViewModels;

namespace RssReader.App;

public partial class ArticleListView : UserControl
{
    private MainWindowViewModel? _viewModel;

    public ArticleListView()
    {
        InitializeComponent();
        DataContextChanged += ArticleListView_DataContextChanged;
        AttachViewModel(DataContext as MainWindowViewModel);
    }

    private void ArticleListView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        AttachViewModel(e.NewValue as MainWindowViewModel);

    private void AttachViewModel(MainWindowViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ArticleListScrollToTopRequest))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(ScrollArticleListsToTop));
        }
    }

    private void ScrollArticleListsToTop()
    {
        foreach (var articleList in new[]
                 {
                     ArticleRowsList,
                     ArticleMagazineList,
                     ArticleCardsList,
                     ArticleFolderCardsList
                 })
        {
            articleList.ApplyTemplate();
            if (VisualTreeSearch.FindDescendant<ScrollViewer>(articleList) is { } scrollViewer)
            {
                scrollViewer.ScrollToTop();
            }
        }
    }

    public void RestoreArticleFocus(ArticleRowViewModel article)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsArticleListVisible)
        {
            return;
        }

        var articleList = new[]
        {
            ArticleRowsList,
            ArticleMagazineList,
            ArticleCardsList,
            ArticleFolderCardsList
        }.FirstOrDefault(list => list.IsVisible);
        if (articleList is null)
        {
            return;
        }

        if (!viewModel.VisibleArticles.Contains(article))
        {
            articleList.Focus();
            return;
        }

        articleList.ScrollIntoView(article);
        articleList.UpdateLayout();
        if (articleList.ItemContainerGenerator.ContainerFromItem(article) is ListBoxItem articleContainer)
        {
            var articleAction = VisualTreeSearch.FindDescendant<Button>(articleContainer);
            if (articleAction?.Focus() != true)
            {
                articleList.Focus();
            }
        }
        else
        {
            articleList.Focus();
        }
    }
}
