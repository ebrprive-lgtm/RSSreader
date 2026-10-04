using System.Windows;
using RssReader.App.ViewModels;

namespace RssReader.App;

internal enum CatalogEntryKind
{
    Feed,
    Category,
    Collection
}

public partial class CatalogEntryWindow : Window
{
    private readonly CatalogManagementViewModel _catalogManagement;
    private readonly CatalogEntryKind _entryKind;

    internal CatalogEntryWindow(
        CatalogManagementViewModel catalogManagement,
        CatalogEntryKind entryKind,
        bool isEditingFeed = false,
        bool isEditingCategory = false)
    {
        InitializeComponent();
        _catalogManagement = catalogManagement;
        _entryKind = entryKind;
        if (!isEditingFeed && !isEditingCategory)
        {
            _catalogManagement.ResetEntryForm();
        }

        DataContext = catalogManagement;

        var (heading, buttonText) = isEditingFeed && entryKind == CatalogEntryKind.Feed
            ? ("Edit feed", "Save changes")
            : isEditingCategory && entryKind == CatalogEntryKind.Category
                ? ("Rename category", "Save changes")
            : entryKind switch
        {
            CatalogEntryKind.Feed => ("Add feed", "Add feed"),
            CatalogEntryKind.Category => ("Add category", "Add category"),
            CatalogEntryKind.Collection => ("Add collection", "Add collection"),
            _ => throw new ArgumentOutOfRangeException(nameof(entryKind))
        };
        DialogHeading.Text = heading;
        SubmitButton.Content = buttonText;

        FeedFields.Visibility = entryKind == CatalogEntryKind.Feed ? Visibility.Visible : Visibility.Collapsed;
        CategoryFields.Visibility = entryKind == CatalogEntryKind.Category ? Visibility.Visible : Visibility.Collapsed;
        CollectionFields.Visibility = entryKind == CatalogEntryKind.Collection ? Visibility.Visible : Visibility.Collapsed;
        if (isEditingFeed)
        {
            FeedNameBox.Text = catalogManagement.FeedName;
            FeedUrlBox.Text = catalogManagement.FeedUrl;
            FeedWebsiteUrlBox.Text = catalogManagement.FeedWebsiteUrl;
            FeedDescriptionBox.Text = catalogManagement.FeedDescription;
            FeedCategoryBox.SelectedItem = catalogManagement.SelectedCategory;
        }

        if (isEditingCategory)
        {
            CategoryNameBox.Text = catalogManagement.CategoryName;
        }
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_entryKind)
        {
            case CatalogEntryKind.Feed:
                _catalogManagement.FeedName = FeedNameBox.Text.Trim();
                _catalogManagement.FeedUrl = FeedUrlBox.Text.Trim();
                _catalogManagement.FeedWebsiteUrl = FeedWebsiteUrlBox.Text.Trim();
                _catalogManagement.FeedDescription = FeedDescriptionBox.Text.Trim();
                _catalogManagement.SelectedCategory = FeedCategoryBox.SelectedItem as Domain.CatalogCategory;
                await _catalogManagement.AddFeedCommand.ExecuteAsync();
                break;
            case CatalogEntryKind.Category:
                _catalogManagement.CategoryName = CategoryNameBox.Text.Trim();
                await _catalogManagement.AddCategoryCommand.ExecuteAsync();
                break;
            case CatalogEntryKind.Collection:
                _catalogManagement.CollectionName = CollectionNameBox.Text.Trim();
                await _catalogManagement.AddCollectionCommand.ExecuteAsync();
                break;
        }

        if (string.IsNullOrWhiteSpace(_catalogManagement.ErrorMessage))
        {
            DialogResult = true;
        }
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void MergeCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        await _catalogManagement.MergeCategoryCommand.ExecuteAsync();
        if (string.IsNullOrWhiteSpace(_catalogManagement.ErrorMessage))
        {
            DialogResult = true;
        }
    }
}