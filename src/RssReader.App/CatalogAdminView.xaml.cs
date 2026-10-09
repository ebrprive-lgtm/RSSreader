using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Navigation;
using System.Windows.Threading;
using RssReader.App.ViewModels;
using RssReader.Domain;

namespace RssReader.App;

public partial class CatalogAdminView : UserControl
{
    public CatalogAdminView()
    {
        InitializeComponent();
    }

    public event EventHandler<RequestNavigateEventArgs>? SourceLinkRequested;

    private void CatalogActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void CopyCatalogFeedCheckError_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TextBlock errorText } ||
            string.IsNullOrEmpty(errorText.Text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(errorText.Text);
        }
        catch (ExternalException exception)
        {
            Trace.TraceError($"Could not copy the catalog feed-check error to the clipboard: {exception}");
        }
    }

    private void SourceLink_RequestNavigate(object? sender, RequestNavigateEventArgs e) =>
        SourceLinkRequested?.Invoke(this, e);

    private async void ImportOpml_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var dialog = new OpenFileDialog
        {
            Title = "Import feed list",
            Filter = "OPML files (*.opml;*.xml)|*.opml;*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        using var stream = dialog.OpenFile();
        await catalogManagement.ImportOpmlAsync(stream);
    }

    private void AddFeed_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Feed);

    private void EditFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFeedListItem feed } ||
            DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var feedList = (ListBox?)FindName("CatalogManagementFeedList");
        var scrollOffset = feedList is null
            ? null
            : VisualTreeSearch.FindDescendant<ScrollViewer>(feedList)?.VerticalOffset;
        catalogManagement.PrepareFeedEdit(feed);
        ShowCatalogEntry(CatalogEntryKind.Feed, isEditingFeed: true);

        if (scrollOffset is { } offset)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (FindName("CatalogManagementFeedList") is ListBox updatedFeedList)
                {
                    VisualTreeSearch.FindDescendant<ScrollViewer>(updatedFeedList)?.ScrollToVerticalOffset(offset);
                }
            }));
        }
    }

    private void PreviewManagedFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFeedListItem feed } ||
            DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var dialog = new CatalogFeedPreviewWindow(
            feed,
            token => catalogManagement.PreviewFeedAsync(feed, token),
            () => catalogManagement.DeleteFeedCommand.ExecuteAsync(feed),
            () => catalogManagement.CheckFeedHealthCommand.ExecuteAsync(feed),
            token => catalogManagement.GetRawFeedXmlAsync(feed, token))
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    private void RenameCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogCategory category } ||
            DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        catalogManagement.PrepareCategoryEdit(category);
        ShowCatalogEntry(CatalogEntryKind.Category, isEditingCategory: true);
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Category);

    private void AddCollection_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Collection);

    private void RenameCollection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogCollection collection } ||
            DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        catalogManagement.PrepareCollectionEdit(collection);
        ShowCatalogEntry(CatalogEntryKind.Collection, isEditingCollection: true);
    }

    private async void ManageFeedCollections_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFeedListItem feed } ||
            DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var memberships = catalogManagement.GetCollectionIdsForFeed(feed.Id);
        var choices = catalogManagement.Collections
            .OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase)
            .Select(collection =>
            {
                var count = catalogManagement.GetFeedIdsForCollection(collection.Id).Count;
                var feedLabel = count == 1 ? "1 feed" : $"{count} feeds";
                return new CatalogMembershipPickerItem(
                    collection.Id,
                    collection.Name,
                    feedLabel,
                    memberships.Contains(collection.Id));
            });
        var picker = new CatalogMembershipPickerWindow(
            $"Collections for {feed.Name}",
            "Choose every collection that should include this feed. Categories and subscriptions are unchanged.",
            choices,
            owner => CreateCollectionPickerItemAsync(catalogManagement, owner))
        {
            Owner = Window.GetWindow(this)
        };
        if (picker.ShowDialog() != true ||
            await catalogManagement.UpdateFeedCollectionsAsync(feed.Id, picker.SelectedIds))
        {
            return;
        }

        MessageDialogWindow.Show(
            Window.GetWindow(this),
            catalogManagement.ErrorMessage,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private Task<CatalogMembershipPickerItem?> CreateCollectionPickerItemAsync(
        CatalogManagementViewModel catalogManagement,
        Window owner)
    {
        var existingCollectionIds = catalogManagement.Collections
            .Select(collection => collection.Id)
            .ToHashSet(StringComparer.Ordinal);
        var dialog = new CatalogEntryWindow(catalogManagement, CatalogEntryKind.Collection)
        {
            Owner = owner
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.FromResult<CatalogMembershipPickerItem?>(null);
        }

        var createdCollection = catalogManagement.Collections
            .FirstOrDefault(collection => !existingCollectionIds.Contains(collection.Id));
        if (createdCollection is null)
        {
            MessageDialogWindow.Show(
                owner,
                "The collection dialog closed successfully, but the new collection could not be found.",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return Task.FromResult<CatalogMembershipPickerItem?>(null);
        }

        return Task.FromResult<CatalogMembershipPickerItem?>(new CatalogMembershipPickerItem(
            createdCollection.Id,
            createdCollection.Name,
            "0 feeds",
            isSelected: false));
    }

    private async void ManageCollectionFeeds_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogCollection collection } ||
            DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var memberships = catalogManagement.GetFeedIdsForCollection(collection.Id);
        var choices = catalogManagement.Feeds
            .OrderBy(feed => feed.Name, StringComparer.OrdinalIgnoreCase)
            .Select(feed =>
            {
                var details = string.Join(
                    " · ",
                    new[] { feed.CategoryName ?? "Uncategorized", feed.Description, feed.WebsiteUrl }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                return new CatalogMembershipPickerItem(
                    feed.Id,
                    feed.Name,
                    details,
                    memberships.Contains(feed.Id));
            });
        var picker = new CatalogMembershipPickerWindow(
            $"Feeds in {collection.Name}",
            "Select every feed that belongs in this collection. Search, then select or clear all visible results.",
            choices)
        {
            Owner = Window.GetWindow(this)
        };
        if (picker.ShowDialog() != true ||
            await catalogManagement.UpdateCollectionFeedsAsync(collection.Id, picker.SelectedIds))
        {
            return;
        }

        MessageDialogWindow.Show(
            Window.GetWindow(this),
            catalogManagement.ErrorMessage,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void ShowCatalogEntry(
        CatalogEntryKind entryKind,
        bool isEditingFeed = false,
        bool isEditingCategory = false,
        bool isEditingCollection = false)
    {
        if (DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var dialog = new CatalogEntryWindow(
            catalogManagement,
            entryKind,
            isEditingFeed,
            isEditingCategory,
            isEditingCollection)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }
}
