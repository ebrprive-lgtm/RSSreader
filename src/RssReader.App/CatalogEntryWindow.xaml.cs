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

    internal CatalogEntryWindow(CatalogManagementViewModel catalogManagement, CatalogEntryKind entryKind)
    {
        InitializeComponent();
        _catalogManagement = catalogManagement;
        _entryKind = entryKind;
        _catalogManagement.ResetEntryForm();
        DataContext = catalogManagement;

        var (heading, buttonText) = entryKind switch
        {
            CatalogEntryKind.Feed => ("Add feed", "Add feed"),
            CatalogEntryKind.Category => ("Add category", "Add category"),
            CatalogEntryKind.Collection => ("Add collection", "Add collection"),
            _ => throw new ArgumentOutOfRangeException(nameof(entryKind))
        };
        Title = heading;
        DialogHeading.Text = heading;
        SubmitButton.Content = buttonText;
        FeedFields.Visibility = entryKind == CatalogEntryKind.Feed ? Visibility.Visible : Visibility.Collapsed;
        CategoryFields.Visibility = entryKind == CatalogEntryKind.Category ? Visibility.Visible : Visibility.Collapsed;
        CollectionFields.Visibility = entryKind == CatalogEntryKind.Collection ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_entryKind)
        {
            case CatalogEntryKind.Feed:
                _catalogManagement.FeedName = FeedNameBox.Text.Trim();
                _catalogManagement.FeedUrl = FeedUrlBox.Text.Trim();
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
}