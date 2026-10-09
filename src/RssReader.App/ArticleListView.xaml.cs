using System.Windows;
using System.Windows.Controls;
using RssReader.App.ViewModels;

namespace RssReader.App;

public partial class ArticleListView : UserControl
{
    public ArticleListView()
    {
        InitializeComponent();
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
