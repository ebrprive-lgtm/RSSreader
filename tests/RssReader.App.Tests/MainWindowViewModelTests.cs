using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RssReader.App.ViewModels;
using RssReader.Application;
using RssReader.Domain;
using RssReader.Infrastructure;
using Microsoft.Data.Sqlite;
using System.Threading;

namespace RssReader.App.Tests;

[TestClass]
public sealed class MainWindowViewModelTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void MainWindowCanBeConstructedForARegularProfile()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new RssReader.App.App();
                app.InitializeComponent();
                var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                var accessibilityFeed = new CatalogFeedListItem(
                    "feed-accessibility",
                    "Example comic feed",
                    "https://example.com/comics.xml",
                    "A sample comic feed",
                    "Comics",
                    "category-comics");
                viewModel.CatalogFeeds.Add(accessibilityFeed);
                var window = new RssReader.App.MainWindow(
                    viewModel);
                var preferencesRequested = false;
                var logoutRequested = false;
                window.PreferencesRequested += () => preferencesRequested = true;
                window.LogoutRequested += () => logoutRequested = true;
                window.Show();
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, ((Border)window.FindName("CatalogFeedPreviewPanel")).Visibility);
                viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));
                window.UpdateLayout();
                var catalogSearchBox = (TextBox)window.FindName("CatalogFeedSearchBox");
                catalogSearchBox.Text = "comic";
                Assert.AreEqual("comic", viewModel.CatalogSearchQuery);
                var categoryFilter = (ComboBox)window.FindName("CatalogCategoryFilter");
                Assert.AreEqual("All categories (0)", ((CatalogCategoryOption)categoryFilter.SelectedItem).Name);
                var collectionFilter = (ComboBox)window.FindName("CatalogCollectionFilter");
                Assert.AreEqual("All collections (0)", ((CatalogCollectionOption)collectionFilter.SelectedItem).Name);
                var hideFollowedCheckBox = (CheckBox)window.FindName("HideFollowedCatalogCheckBox");
                hideFollowedCheckBox.IsChecked = true;
                Assert.IsTrue(viewModel.HideFollowedCatalogFeeds);
                var catalogResults = (ListBox)window.FindName("CatalogFeedResultsList");
                Assert.AreEqual("Catalog feed results", AutomationProperties.GetName(catalogResults));
                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(catalogResults));
                Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(catalogResults));
                catalogResults.UpdateLayout();
                var feedContainer = catalogResults.ItemContainerGenerator.ContainerFromItem(accessibilityFeed);
                Assert.IsNotNull(feedContainer);
                var feedCheckBox = FindVisualChild<CheckBox>(feedContainer!)
                    ?? throw new AssertFailedException("The catalog feed selection control was not created.");
                var feedButtons = FindVisualChildren<Button>(feedContainer!).ToArray();
                var previewButton = feedButtons.Single(button => Equals(button.Content, "Preview"));
                var subscriptionButton = feedButtons.Single(button => Equals(button.Content, "Follow"));
                Assert.AreEqual("Select Example comic feed for follow", AutomationProperties.GetName(feedCheckBox));
                Assert.IsTrue(feedCheckBox.IsTabStop);
                Assert.AreEqual("Preview Example comic feed", AutomationProperties.GetName(previewButton));
                Assert.IsTrue(previewButton.IsTabStop);
                Assert.AreEqual("Follow Example comic feed", AutomationProperties.GetName(subscriptionButton));
                accessibilityFeed.IsSubscribed = true;
                window.UpdateLayout();
                Assert.AreEqual("Unfollow Example comic feed", AutomationProperties.GetName(subscriptionButton));
                var batchFollowButton = (Button)window.FindName("BatchFollowSelectedButton");
                Assert.AreEqual("Follow selected (0)", batchFollowButton.Content);
                Assert.IsFalse(batchFollowButton.IsEnabled);
                Assert.IsFalse(((Button)window.FindName("ClearSelectedCatalogFeedsButton")).IsEnabled);
                viewModel.ClearCatalogFiltersCommand.Execute(null);
                window.UpdateLayout();

                var preferences = new ProfilePreferences(
                    ProfileStartPage.FirstFolder,
                    ProfileArticlePresentation.Magazine,
                    ProfileArticleSort.Newest,
                    true,
                    25,
                    false,
                    60,
                    true);
                var preferencesWindow = new RssReader.App.PreferencesWindow(preferences) { Owner = window };
                preferencesWindow.Show();
                preferencesWindow.UpdateLayout();
                Assert.IsTrue(((RadioButton)preferencesWindow.FindName("StartFirstFolderOption")).IsChecked);
                Assert.IsTrue(((RadioButton)preferencesWindow.FindName("PresentationMagazineOption")).IsChecked);
                Assert.AreEqual("25", ((TextBox)preferencesWindow.FindName("FolderArticleLimitBox")).Text);
                Assert.IsFalse(((CheckBox)preferencesWindow.FindName("RefreshFeedsWhenOpenedCheckBox")).IsChecked);
                Assert.AreEqual("Every hour", ((ComboBoxItem)((ComboBox)preferencesWindow.FindName("AutoRefreshIntervalComboBox")).SelectedItem).Content);
                Assert.IsTrue(((CheckBox)preferencesWindow.FindName("ShowRawFeedButtonCheckBox")).IsChecked);
                preferencesWindow.Close();
                var rawWindow = new RssReader.App.RawFeedWindow("Test feed", "<?xml version=\"1.0\"?><rss />") { Owner = window };
                rawWindow.Show();
                rawWindow.UpdateLayout();
                Assert.AreEqual("<?xml version=\"1.0\"?><rss />", ((TextBox)rawWindow.FindName("RawContentTextBox")).Text);
                rawWindow.Close();

                var profileButton = (Button)window.FindName("ProfileMenuButton");
                profileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, profileButton));
                Assert.IsTrue(profileButton.ContextMenu?.IsOpen);
                Assert.IsFalse(logoutRequested);
                var menuItems = profileButton.ContextMenu!.Items.OfType<MenuItem>().ToArray();
                menuItems.Single(item => Equals(item.Header, "Preferences"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.IsTrue(preferencesRequested);
                Assert.IsFalse(logoutRequested);

                profileButton.ContextMenu.IsOpen = false;
                profileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, profileButton));
                menuItems.Single(item => Equals(item.Header, "Log Out"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.IsTrue(logoutRequested);
                profileButton.ContextMenu.IsOpen = false;

                var sidebarPanel = (Border)window.FindName("SidebarPanel");
                var sidebarPeekButton = (Button)window.FindName("SidebarPeekButton");
                var shellGrid = (Grid)window.FindName("ShellGrid");
                viewModel.ToggleSidebarCommand.Execute(null);
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, sidebarPeekButton.Visibility);
                Assert.AreEqual(Visibility.Collapsed, sidebarPanel.Visibility);
                Assert.AreEqual(0, shellGrid.ColumnDefinitions[0].ActualWidth);

                sidebarPeekButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                {
                    RoutedEvent = UIElement.MouseEnterEvent
                });
                Assert.AreEqual(Visibility.Visible, sidebarPanel.Visibility);
                Assert.AreEqual(0, shellGrid.ColumnDefinitions[0].ActualWidth);

                viewModel.ToggleSidebarCommand.Execute(null);
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, sidebarPeekButton.Visibility);
                Assert.AreEqual(Visibility.Visible, sidebarPanel.Visibility);
                Assert.AreEqual(286, shellGrid.ColumnDefinitions[0].ActualWidth);

                var comicArticle = new ArticleRowViewModel(
                    "9 Chickweed Lane",
                    "Arcamax",
                    DateTimeOffset.UtcNow,
                    "Comics",
                    [],
                    "Source",
                    feedId: "comic-feed",
                    link: "https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031",
                    imageUrl: "https://resources.arcamax.com/newspics/396/39604/3960483.gif");
                viewModel.SelectArticleCommand.Execute(comicArticle);
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, ((Image)window.FindName("SelectedArticleImage")).Visibility);
                Assert.AreEqual(Visibility.Collapsed, ((TextBlock)window.FindName("SelectedArticleSummary")).Visibility);
                Assert.AreEqual(Visibility.Visible, ((TextBlock)window.FindName("ArticleSourceHyperlink")).Visibility);
                Assert.AreEqual(
                    new Uri("https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031"),
                    ((Hyperlink)((TextBlock)window.FindName("ArticleSourceHyperlink")).Inlines.Single()).NavigateUri);
                viewModel.BackToListCommand.Execute(null);
                window.UpdateLayout();

                viewModel.IsMagazineView = true;
                window.UpdateLayout();
                viewModel.IsListView = true;
                window.UpdateLayout();
                viewModel.IsCardsView = true;
                window.UpdateLayout();
                window.Close();
                app.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject =>
        FindVisualChildren<T>(parent).FirstOrDefault();

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var childIndex = 0; childIndex < childCount; childIndex++)
        {
            var child = VisualTreeHelper.GetChild(parent, childIndex);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    [TestMethod]
    public async Task InitializeLoadsPersistedArticlesAndProfileNavigation()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var feed = new CatalogFeed("feed-1", "Gaming News", "https://example.com/feed.xml", "News", null);
            await catalogStore.AddFeedAsync(feed);
            await readerStore.AddFolderAsync(profile.Id, "Gaming");
            await readerStore.SubscribeAsync(profile.Id, feed.Id, "Gaming");
            await readerStore.AddFeedTagAsync(profile.Id, feed.Id, "Reviews");
            await readerStore.SaveArticlesAsync(feed.Id,
            [
                new FeedArticle("article-1", feed.Id, "item-1", "Handheld review", null, DateTimeOffset.UtcNow, "Portable hardware", "Full story")
            ]);

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null,
                new ProfilePreferences(ShowRawFeedButton: true));
            await viewModel.InitializeAsync();

            Assert.AreEqual(1, viewModel.VisibleArticles.Count);
            Assert.IsFalse(viewModel.IsRawFeedButtonVisible);
            viewModel.SelectArticleCommand.Execute(viewModel.VisibleArticles[0]);
            Assert.IsTrue(viewModel.IsRawFeedButtonVisible);
            Assert.AreEqual("Handheld review", viewModel.VisibleArticles[0].Title);
            Assert.AreEqual("Full story", viewModel.VisibleArticles[0].Content);
            Assert.IsTrue(viewModel.CatalogFeeds.Single().IsSubscribed);
            Assert.IsTrue(viewModel.FeedLinks.Any(link => link.Route == "folder:Gaming"));
            Assert.IsTrue(viewModel.FeedLinks.Any(link => link.Route == "feed:feed-1"));
            var allLink = viewModel.FeedLinks.Single(link => link.Route == "All");
            var folderLink = viewModel.FeedLinks.Single(link => link.Route == "folder:Gaming");
            var feedLink = viewModel.FeedLinks.Single(link => link.Route == "feed:feed-1");
            Assert.IsTrue(allLink.IndentMargin.Left < folderLink.IndentMargin.Left);
            Assert.IsTrue(folderLink.IndentMargin.Left < feedLink.IndentMargin.Left);
            Assert.IsTrue(viewModel.TagLinks.Any(link => link.Route == "tag:Reviews"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task CatalogBrowserFiltersBySearchCategoryAndFollowedStateAndCanClearFilters()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-catalog-filter-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Catalog Reader");
            await profileStore.AddAsync(profile);
            var comics = new CatalogCategory("category-comics", "Comics");
            var science = new CatalogCategory("category-science", "Science");
            await catalogStore.AddCategoryAsync(comics);
            await catalogStore.AddCategoryAsync(science);
            var collection = new CatalogCollection("collection-editors-picks", "Editor's picks");
            await catalogStore.AddCollectionAsync(collection);

            var chickweed = new CatalogFeed(
                "feed-chickweed",
                "9 Chickweed Lane by Brooke McEldowney",
                "https://feeds.example.com/chickweed.xml",
                "Daily comic strip",
                comics.Id,
                "https://www.9chickweedlane.com");
            var dilbert = new CatalogFeed(
                "feed-dilbert",
                "Dilbert by Scott Adams",
                "https://feeds.example.com/dilbert.xml",
                "Office satire",
                comics.Id);
            var spaceNews = new CatalogFeed(
                "feed-space-news",
                "Space News",
                "https://science.example.com/feed.xml",
                "Daily astronomy updates",
                science.Id);
            await catalogStore.AddFeedAsync(chickweed);
            await catalogStore.AddFeedAsync(dilbert);
            await catalogStore.AddFeedAsync(spaceNews);
            await catalogStore.AddFeedToCollectionAsync(collection.Id, chickweed.Id);
            await catalogStore.AddFeedToCollectionAsync(collection.Id, spaceNews.Id);
            await readerStore.AddFolderAsync(profile.Id, "Comics");
            await readerStore.SubscribeAsync(profile.Id, dilbert.Id, "Comics");

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();

            CollectionAssert.AreEqual(
                new[] { chickweed.Name, dilbert.Name, spaceNews.Name },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Name).ToArray());
            Assert.AreEqual("Editor's picks (2)", viewModel.CatalogCollectionOptions.Single(option => option.CollectionId == collection.Id).Name);

            viewModel.CatalogSearchQuery = "  brooke   chickweed ";
            CollectionAssert.AreEqual(
                new[] { chickweed.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());

            viewModel.CatalogSearchQuery = "9chickweedlane";
            CollectionAssert.AreEqual(
                new[] { chickweed.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());

            viewModel.CatalogSearchQuery = "office satire";
            CollectionAssert.AreEqual(
                new[] { dilbert.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());

            viewModel.CatalogSearchQuery = string.Empty;
            viewModel.SelectedCatalogCategory = viewModel.CatalogCategoryOptions.Single(option => option.CategoryId == comics.Id);
            Assert.AreEqual(2, viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Count());

            viewModel.SelectedCatalogCollection = viewModel.CatalogCollectionOptions.Single(option => option.CollectionId == collection.Id);
            Assert.AreEqual("Curated by Catalog Master", viewModel.SelectedCatalogCollectionCuratorLabel);
            CollectionAssert.AreEqual(
                new[] { chickweed.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());
            viewModel.SelectedCatalogCollection = viewModel.CatalogCollectionOptions.Single(option => option.CollectionId is null);
            Assert.AreEqual(string.Empty, viewModel.SelectedCatalogCollectionCuratorLabel);
            viewModel.SelectedCatalogCollection = viewModel.CatalogCollectionOptions.Single(option => option.CollectionId is null);

            viewModel.HideFollowedCatalogFeeds = true;
            CollectionAssert.AreEqual(
                new[] { chickweed.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());

            viewModel.CatalogSearchQuery = "no such feed";
            Assert.IsTrue(viewModel.IsCatalogResultsEmpty);
            Assert.AreEqual("0 of 3 feeds", viewModel.CatalogResultsSummary);
            Assert.AreEqual("No feeds match these filters.", viewModel.CatalogResultsEmptyMessage);

            viewModel.ClearCatalogFiltersCommand.Execute(null);
            Assert.IsFalse(viewModel.IsCatalogFilterActive);
            Assert.IsFalse(viewModel.IsCatalogResultsEmpty);
            Assert.AreEqual(3, viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Count());
            Assert.AreEqual("3 feeds", viewModel.CatalogResultsSummary);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task CatalogPreviewCachesFiveLightweightItemsUntilRefreshed()
    {
        var previewItems = Enumerable.Range(1, 7)
            .Select(index => new DownloadedFeedItem(
                $"item-{index}",
                $"Preview headline {index}",
                $"https://example.com/articles/{index}",
                DateTimeOffset.UtcNow.AddDays(-index),
                $"Summary {index}",
                "Long article content",
                $"https://example.com/images/{index}.jpg"))
            .ToArray();
        var downloader = new RecordingFeedDownloader { ItemsToReturn = previewItems };
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Preview Reader"),
            null,
            null,
            null,
            catalogFeedPreviewService: new CatalogFeedPreviewService(downloader));
        var feed = new CatalogFeedListItem(
            "preview-feed",
            "Preview feed",
            "https://example.com/feed.xml",
            "Feed description",
            "Technology",
            "category-tech");
        viewModel.CatalogFeeds.Add(feed);

        await viewModel.PreviewCatalogFeedAsync(feed);

        Assert.IsTrue(viewModel.IsCatalogFeedPreviewVisible);
        Assert.IsFalse(viewModel.IsCatalogFeedPreviewLoading);
        Assert.AreEqual(5, viewModel.ActiveCatalogFeedPreview!.Items.Count);
        Assert.AreEqual("Preview headline 1", viewModel.ActiveCatalogFeedPreview.Items[0].Title);
        Assert.IsTrue(viewModel.ActiveCatalogFeedPreview.Items.All(item => item.Content is null && item.ImageUrl is null));
        Assert.IsFalse(feed.IsSubscribed);
        Assert.AreEqual(1, downloader.RequestedFeedIds.Count);

        await viewModel.PreviewCatalogFeedAsync(feed);
        Assert.AreEqual(1, downloader.RequestedFeedIds.Count);

        await viewModel.PreviewCatalogFeedAsync(feed, forceRefresh: true);
        Assert.AreEqual(2, downloader.RequestedFeedIds.Count);

        viewModel.CloseCatalogFeedPreviewCommand.Execute(null);
        await viewModel.PreviewCatalogFeedAsync(feed);
        Assert.AreEqual(2, downloader.RequestedFeedIds.Count);
        Assert.IsTrue(viewModel.IsCatalogFeedPreviewVisible);

        downloader.DownloadHandler = _ => Task.FromException<IReadOnlyList<DownloadedFeedItem>>(
            new System.Net.Http.HttpRequestException("The feed is offline."));
        await viewModel.PreviewCatalogFeedAsync(feed, forceRefresh: true);
        Assert.IsTrue(viewModel.HasCatalogFeedPreviewError);
        StringAssert.Contains(viewModel.CatalogFeedPreviewErrorMessage, "The feed is offline.");
        Assert.IsFalse(viewModel.IsCatalogFeedPreviewEmpty);

        downloader.DownloadHandler = null;
        await viewModel.PreviewCatalogFeedAsync(feed, forceRefresh: true);
        Assert.IsFalse(viewModel.HasCatalogFeedPreviewError);
        Assert.IsNotNull(viewModel.ActiveCatalogFeedPreview);
    }

    [TestMethod]
    public async Task CatalogPreviewCancelsRequestWhenNavigatingAway()
    {
        var downloadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedToken = CancellationToken.None;
        var downloader = new RecordingFeedDownloader
        {
            DownloadHandler = async cancellationToken =>
            {
                observedToken = cancellationToken;
                downloadStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Array.Empty<DownloadedFeedItem>();
            }
        };
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Preview Reader"),
            null,
            null,
            null,
            catalogFeedPreviewService: new CatalogFeedPreviewService(downloader));
        var feed = new CatalogFeedListItem(
            "cancel-feed",
            "Cancel feed",
            "https://example.com/cancel.xml",
            null,
            null);
        viewModel.CatalogFeeds.Add(feed);

        var previewTask = viewModel.PreviewCatalogFeedAsync(feed);
        await downloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(viewModel.IsCatalogFeedPreviewLoading);

        viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));
        await previewTask;

        Assert.IsTrue(observedToken.IsCancellationRequested);
        Assert.IsFalse(viewModel.IsCatalogFeedPreviewVisible);
        Assert.IsFalse(viewModel.IsCatalogFeedPreviewLoading);
        Assert.AreEqual("All", viewModel.ActiveRoute);
    }

    [TestMethod]
    public async Task CatalogBrowsePreviewFollowAndReturnWorksEndToEnd()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-catalog-workflow-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Discovery Workflow Reader");
            await profileStore.AddAsync(profile);
            var category = new CatalogCategory("category-comics", "Comics");
            var feed = new CatalogFeed(
                "feed-indie-comic",
                "Indie Webcomic",
                "https://example.com/comic.xml",
                "An independent comic",
                category.Id,
                "https://example.com");
            await catalogStore.AddCategoryAsync(category);
            await catalogStore.AddFeedAsync(feed);
            await readerStore.AddFolderAsync(profile.Id, "Comics");

            var downloader = new RecordingFeedDownloader();
            var catalogService = new CatalogService(catalogStore);
            var viewModel = new MainWindowViewModel(
                profile,
                catalogService,
                new ReadingService(readerStore, catalogStore),
                new FeedRefreshService(readerStore, catalogStore, downloader),
                new ProfilePreferences(RefreshFeedsWhenOpened: false),
                new CatalogFeedPreviewService(downloader));
            await viewModel.InitializeAsync();
            viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));
            viewModel.CatalogSearchQuery = "indie comics";

            var catalogItem = viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Single();
            await viewModel.PreviewCatalogFeedAsync(catalogItem);
            Assert.AreEqual("New headline", viewModel.ActiveCatalogFeedPreview?.Items.Single().Title);
            Assert.AreEqual(1, downloader.RequestedFeedIds.Count);

            viewModel.FolderSelectionRequested = (_, _, _) => Task.FromResult<string?>("Comics");
            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(catalogItem);
            Assert.IsTrue(catalogItem.IsSubscribed);
            Assert.AreEqual("Comics", (await readerStore.GetSubscriptionsAsync(profile.Id)).Single().FolderName);
            Assert.AreEqual("indie comics", viewModel.CatalogSearchQuery);

            viewModel.HideFollowedCatalogFeeds = true;
            Assert.IsTrue(viewModel.CatalogFeedListView.IsEmpty);
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));
            Assert.IsFalse(viewModel.IsCatalogFeedPreviewVisible);
            viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));
            viewModel.HideFollowedCatalogFeeds = false;

            Assert.AreEqual("indie comics", viewModel.CatalogSearchQuery);
            Assert.AreEqual(feed.Id, viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Single().Id);
            Assert.IsTrue(viewModel.CatalogFeeds.Single().IsSubscribed);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [DataTestMethod]
    [DataRow(575)]
    [DataRow(5000)]
    [DataRow(25000)]
    public void CatalogBrowserFiltersGeneratedLargeCatalog(int catalogSize)
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Large Catalog Reader"));
        var scienceCount = (catalogSize + 1) / 2;
        var scienceCategory = new CatalogCategoryOption("category-science", $"Science ({scienceCount:N0})");
        viewModel.CatalogCategoryOptions.Add(scienceCategory);
        viewModel.CatalogCategoryOptions.Add(new CatalogCategoryOption("category-comics", $"Comics ({catalogSize / 2:N0})"));

        var populationTimer = Stopwatch.StartNew();
        for (var feedIndex = 0; feedIndex < catalogSize; feedIndex++)
        {
            var isScienceFeed = feedIndex % 2 == 0;
            var categoryName = isScienceFeed ? "Science" : "Comics";
            var categoryId = isScienceFeed ? "category-science" : "category-comics";
            viewModel.CatalogFeeds.Add(new CatalogFeedListItem(
                $"feed-{feedIndex:D4}",
                $"Feed {feedIndex:D4}",
                $"https://feeds.example.com/{feedIndex:D4}.xml",
                isScienceFeed ? "Research and astronomy" : "Daily strip",
                categoryName,
                categoryId));
        }
        populationTimer.Stop();

        var matchingFeedIndex = catalogSize - 1;
        if (matchingFeedIndex % 2 != 0)
        {
            matchingFeedIndex--;
        }

        var filterTimer = Stopwatch.StartNew();
        viewModel.SelectedCatalogCategory = scienceCategory;
        viewModel.CatalogSearchQuery = $"science {matchingFeedIndex:D4}";
        var visibleFeedIds = viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray();
        filterTimer.Stop();

        CollectionAssert.AreEqual(
            new[] { $"feed-{matchingFeedIndex:D4}" },
            visibleFeedIds);
        Assert.AreEqual($"1 of {catalogSize} feeds", viewModel.CatalogResultsSummary);
        TestContext.WriteLine(
            $"Catalog size {catalogSize:N0}: populate {populationTimer.Elapsed.TotalMilliseconds:F1} ms; filter {filterTimer.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [TestMethod]
    public async Task CatalogBrowserShowsRecoverableLoadFailureAndRetries()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-catalog-load-{Guid.NewGuid():N}.db");
        await File.WriteAllTextAsync(databasePath, "invalid database content");
        try
        {
            var catalogStore = new SqliteCatalogStore(databasePath);
            var viewModel = new MainWindowViewModel(
                Profile.CreateRegular("Catalog Recovery Reader"),
                new CatalogService(catalogStore));

            await viewModel.InitializeAsync();

            Assert.IsFalse(viewModel.IsCatalogLoading);
            Assert.IsFalse(viewModel.IsCatalogLoaded);
            Assert.IsTrue(viewModel.HasCatalogLoadError);
            Assert.IsFalse(viewModel.IsCatalogResultsEmpty);
            StringAssert.StartsWith(viewModel.CatalogLoadErrorMessage, "Could not load the feed catalog:");

            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            await catalogStore.InitializeAsync();
            await catalogStore.AddFeedAsync(new CatalogFeed(
                "recovered-feed",
                "Recovered feed",
                "https://example.com/recovered.xml",
                null,
                null));

            await viewModel.RetryCatalogLoadCommand.ExecuteAsync();

            Assert.IsFalse(viewModel.IsCatalogLoading);
            Assert.IsTrue(viewModel.IsCatalogLoaded);
            Assert.IsFalse(viewModel.HasCatalogLoadError);
            Assert.AreEqual("recovered-feed", viewModel.CatalogFeeds.Single().Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task BatchFollowUsesOneFolderChoiceAndKeepsSelectionWhenCancelled()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-catalog-batch-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Batch Reader");
            await profileStore.AddAsync(profile);
            var firstFeed = new CatalogFeed("feed-first", "First comic", "https://example.com/first.xml", null, null);
            var secondFeed = new CatalogFeed("feed-second", "Second comic", "https://example.com/second.xml", null, null);
            await catalogStore.AddFeedAsync(firstFeed);
            await catalogStore.AddFeedAsync(secondFeed);
            await readerStore.AddFolderAsync(profile.Id, "Comics");

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();
            var selectedFeeds = viewModel.CatalogFeeds.ToArray();
            selectedFeeds[0].IsSelectedForFollow = true;
            selectedFeeds[1].IsSelectedForFollow = true;
            Assert.AreEqual(2, viewModel.SelectedCatalogFeedCount);
            Assert.IsTrue(viewModel.FollowSelectedCatalogFeedsCommand.CanExecute(null));

            var pickerCallCount = 0;
            viewModel.FolderSelectionRequested = (_, _, _) =>
            {
                pickerCallCount++;
                return Task.FromResult<string?>(null);
            };
            await viewModel.FollowSelectedCatalogFeedsCommand.ExecuteAsync();

            Assert.AreEqual(1, pickerCallCount);
            Assert.AreEqual(0, (await readerStore.GetSubscriptionsAsync(profile.Id)).Count);
            Assert.AreEqual(2, viewModel.SelectedCatalogFeedCount);

            viewModel.FolderSelectionRequested = (_, _, _) =>
            {
                pickerCallCount++;
                return Task.FromResult<string?>("Comics");
            };
            await viewModel.FollowSelectedCatalogFeedsCommand.ExecuteAsync();

            Assert.AreEqual(2, pickerCallCount);
            var subscriptions = await readerStore.GetSubscriptionsAsync(profile.Id);
            Assert.AreEqual(2, subscriptions.Count);
            Assert.IsTrue(subscriptions.All(subscription => subscription.FolderName == "Comics"));
            Assert.AreEqual(0, viewModel.SelectedCatalogFeedCount);
            Assert.AreEqual("Following 2 feeds in Comics.", viewModel.StatusMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task BatchFollowKeepsFailedFeedSelectedWhenCatalogChanges()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-catalog-batch-partial-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Batch Reader");
            await profileStore.AddAsync(profile);
            var availableFeed = new CatalogFeed("feed-available", "Available source", "https://example.com/available.xml", null, null);
            var removedFeed = new CatalogFeed("feed-removed", "Removed source", "https://example.com/removed.xml", null, null);
            await catalogStore.AddFeedAsync(availableFeed);
            await catalogStore.AddFeedAsync(removedFeed);
            await readerStore.AddFolderAsync(profile.Id, "News");

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();
            var availableItem = viewModel.CatalogFeeds.Single(feed => feed.Id == availableFeed.Id);
            var removedItem = viewModel.CatalogFeeds.Single(feed => feed.Id == removedFeed.Id);
            availableItem.IsSelectedForFollow = true;
            removedItem.IsSelectedForFollow = true;
            await catalogStore.DeleteFeedAsync(removedFeed.Id);
            viewModel.FolderSelectionRequested = (_, _, _) => Task.FromResult<string?>("News");

            await viewModel.FollowSelectedCatalogFeedsCommand.ExecuteAsync();

            var subscriptions = await readerStore.GetSubscriptionsAsync(profile.Id);
            Assert.AreEqual(1, subscriptions.Count);
            Assert.AreEqual(availableFeed.Id, subscriptions.Single().FeedId);
            Assert.IsFalse(availableItem.IsSelectedForFollow);
            Assert.IsTrue(availableItem.IsSubscribed);
            Assert.IsTrue(removedItem.IsSelectedForFollow);
            Assert.AreEqual(1, viewModel.SelectedCatalogFeedCount);
            Assert.AreEqual("Followed 1 feeds; 1 failed: Removed source.", viewModel.StatusMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task FirstOpenRefreshesNewFeedAndOpenPreferenceRefreshesFolderFeeds()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var existingFeed = new CatalogFeed("feed-existing", "Existing", "https://example.com/existing.xml", null, null);
            var newFeed = new CatalogFeed("feed-new", "New", "https://example.com/new.xml", null, null);
            await catalogStore.AddFeedAsync(existingFeed);
            await catalogStore.AddFeedAsync(newFeed);
            await readerStore.AddFolderAsync(profile.Id, "News");
            await readerStore.SubscribeAsync(profile.Id, existingFeed.Id, "News");
            await readerStore.SaveArticlesAsync(existingFeed.Id,
            [
                new FeedArticle("existing-article", existingFeed.Id, "existing-item", "Existing article", null, DateTimeOffset.UtcNow, null, null)
            ]);

            var downloader = new RecordingFeedDownloader();
            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                new FeedRefreshService(readerStore, catalogStore, downloader),
                new ProfilePreferences(RefreshFeedsWhenOpened: false));
            await viewModel.InitializeAsync();
            viewModel.FolderSelectionRequested = (_, _, _) => Task.FromResult<string?>("News");
            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(
                viewModel.CatalogFeeds.Single(feed => feed.Id == newFeed.Id));

            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == $"feed:{newFeed.Id}"));
            await WaitForRefreshCompletionAsync(viewModel);

            CollectionAssert.AreEqual(new[] { newFeed.Id }, downloader.RequestedFeedIds.ToArray());
            Assert.AreEqual(1, viewModel.VisibleArticles.Count);
            Assert.AreEqual("New headline", viewModel.VisibleArticles[0].Title);

            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == $"feed:{newFeed.Id}"));
            await WaitForRefreshCompletionAsync(viewModel);
            Assert.AreEqual(1, downloader.RequestedFeedIds.Count);

            viewModel.ApplyPreferences(new ProfilePreferences(RefreshFeedsWhenOpened: true, ShowRawFeedButton: true));
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "folder:News"));
            await WaitForRefreshCompletionAsync(viewModel);

            Assert.AreEqual(3, downloader.RequestedFeedIds.Count);
            CollectionAssert.AreEquivalent(
                new[] { existingFeed.Id, newFeed.Id },
                downloader.RequestedFeedIds.Skip(1).ToArray());

            var newFeedArticle = viewModel.VisibleArticles.Single(article => article.FeedId == newFeed.Id);
            viewModel.SelectArticleCommand.Execute(newFeedArticle);
            Assert.IsTrue(viewModel.IsRawFeedButtonVisible);
            Assert.AreEqual("<rss><channel><title>New</title></channel></rss>", await viewModel.GetRawFeedContentAsync(newFeed.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task FollowUsesPickerFolderAndCancelLeavesFeedUnfollowed()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var selectedFeed = new CatalogFeed("feed-selected", "DIY Source", "https://example.com/diy.xml", null, null);
            var cancelledFeed = new CatalogFeed("feed-cancelled", "Other Source", "https://example.com/other.xml", null, null);
            await catalogStore.AddFeedAsync(selectedFeed);
            await catalogStore.AddFeedAsync(cancelledFeed);

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();
            viewModel.FolderSelectionRequested = async (folders, _, createFolderAsync) =>
            {
                Assert.AreEqual(0, folders.Count);
                await createFolderAsync("DIY");
                return "DIY";
            };

            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(
                viewModel.CatalogFeeds.Single(feed => feed.Id == selectedFeed.Id));

            var subscription = (await readerStore.GetSubscriptionsAsync(profile.Id)).Single();
            Assert.AreEqual(selectedFeed.Id, subscription.FeedId);
            Assert.AreEqual("DIY", subscription.FolderName);
            Assert.IsTrue(viewModel.FeedLinks.Any(link => link.Route == "folder:DIY"));

            viewModel.FolderSelectionRequested = (_, _, _) => Task.FromResult<string?>(null);
            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(
                viewModel.CatalogFeeds.Single(feed => feed.Id == cancelledFeed.Id));

            Assert.AreEqual(1, (await readerStore.GetSubscriptionsAsync(profile.Id)).Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public void NavigationToFolderFiltersArticleRows()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        viewModel.NavigateCommand.Execute(viewModel.FeedLinks[1]);

        Assert.AreEqual("Gaming", viewModel.WorkspaceTitle);
        Assert.AreEqual(2, viewModel.VisibleArticles.Count);
        Assert.IsTrue(viewModel.IsArticleListVisible);
    }

    [TestMethod]
    public async Task FolderRouteShowsAtMostTenNewestArticlesPerFeedOnlyWhenSortedByFolder()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var feeds = new[]
            {
                new CatalogFeed("feed-one", "Source One", "https://example.com/one.xml", null, null),
                new CatalogFeed("feed-two", "Source Two", "https://example.com/two.xml", null, null)
            };
            await readerStore.AddFolderAsync(profile.Id, "Gaming");
            foreach (var feed in feeds)
            {
                await catalogStore.AddFeedAsync(feed);
                await readerStore.SubscribeAsync(profile.Id, feed.Id, "Gaming");
                await readerStore.SaveArticlesAsync(feed.Id, Enumerable.Range(0, 12)
                    .Select(index => new FeedArticle(
                        $"{feed.Id}-article-{index}",
                        feed.Id,
                        $"item-{index}",
                        $"{feed.Name} article {index}",
                        null,
                        DateTimeOffset.UtcNow.AddMinutes(-index),
                        null,
                        null))
                    .ToArray());
            }

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "folder:Gaming"));

            Assert.AreEqual(20, viewModel.VisibleArticles.Count);
            var feedGroups = viewModel.VisibleArticles.GroupBy(article => article.FeedId).ToArray();
            Assert.AreEqual(2, feedGroups.Length);
            foreach (var group in feedGroups)
            {
                Assert.AreEqual(10, group.Count());
                Assert.IsTrue(group.Any(article => article.Title.EndsWith("article 0", StringComparison.Ordinal)));
                Assert.IsFalse(group.Any(article => article.Title.EndsWith("article 10", StringComparison.Ordinal)));
                Assert.IsFalse(group.Any(article => article.Title.EndsWith("article 11", StringComparison.Ordinal)));
            }

            viewModel.ApplyPreferences(new ProfilePreferences(
                ProfileStartPage.FirstFolder,
                FolderArticleLimitPerFeed: 5));

            Assert.AreEqual(10, viewModel.VisibleArticles.Count);

            viewModel.IsSortByDate = true;

            Assert.AreEqual(24, viewModel.VisibleArticles.Count);

            viewModel.IsSortByFolder = true;

            Assert.AreEqual(10, viewModel.VisibleArticles.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public void ArticleViewModeCanSwitchBetweenCardsTitleOnlyAndMagazine()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        Assert.IsTrue(viewModel.IsCardsView);
        Assert.IsFalse(viewModel.IsListView);

        viewModel.IsListView = true;

        Assert.IsFalse(viewModel.IsCardsView);
        Assert.IsTrue(viewModel.IsListView);

        viewModel.IsMagazineView = true;

        Assert.IsFalse(viewModel.IsCardsView);
        Assert.IsFalse(viewModel.IsListView);
        Assert.IsTrue(viewModel.IsMagazineView);

        viewModel.IsCardsView = true;

        Assert.IsTrue(viewModel.IsCardsView);
        Assert.IsFalse(viewModel.IsListView);
        Assert.IsFalse(viewModel.IsMagazineView);
    }

    [TestMethod]
    public void CatalogFeedSubscriptionLabelReflectsItsFollowingState()
    {
        var feed = new CatalogFeedListItem(
            "feed-1",
            "Example",
            "https://example.com/feed.xml",
            null,
            null);

        Assert.AreEqual("Follow", feed.SubscriptionLabel);

        feed.IsSubscribed = true;
        Assert.AreEqual("Unfollow", feed.SubscriptionLabel);
    }

    [TestMethod]
    public void ArticleCountSubtitleHidesOnFollowSources()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var followSources = viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources");

        Assert.IsTrue(viewModel.IsArticleCountVisible);

        viewModel.NavigateCommand.Execute(followSources);

        Assert.IsFalse(viewModel.IsArticleCountVisible);
    }

    [TestMethod]
    public void SearchFiltersByTitleSourceAndSummary()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var searchLink = viewModel.PrimaryLinks.Single(link => link.Route == "Search");
        viewModel.NavigateCommand.Execute(searchLink);

        viewModel.SearchQuery = "handheld";

        Assert.AreEqual(1, viewModel.VisibleArticles.Count);
        StringAssert.Contains(viewModel.VisibleArticles[0].Title, "handheld");
    }

    [TestMethod]
    public void SelectingArticleReplacesListAndBackReturnsToList()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var article = viewModel.VisibleArticles[0];

        viewModel.SelectArticleCommand.Execute(article);

        Assert.IsFalse(viewModel.IsArticleListVisible);
        Assert.IsTrue(viewModel.IsReadingViewVisible);
        Assert.AreSame(article, viewModel.SelectedArticle);

        viewModel.BackToListCommand.Execute(null);

        Assert.IsTrue(viewModel.IsArticleListVisible);
        Assert.IsFalse(viewModel.IsReadingViewVisible);
        Assert.AreSame(article, viewModel.VisibleArticles[0]);
    }

    [TestMethod]
    public void ReadLaterRouteShowsSavedArticles()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var readLaterLink = viewModel.ReadingLinks.Single(link => link.Route == "Read later");

        viewModel.NavigateCommand.Execute(readLaterLink);

        Assert.AreEqual(2, viewModel.VisibleArticles.Count);
        Assert.IsTrue(viewModel.VisibleArticles.All(article => article.IsSaved));
    }

    [TestMethod]
    public void GroupedArticlesAreSortedByFolderAndNewestFirst()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        Assert.IsTrue(viewModel.IsSortByFolder);
        var groups = viewModel.ArticleListView.Groups;

        Assert.IsNotNull(groups);
        Assert.AreEqual(2, groups.Count);
        var gamingGroup = (CollectionViewGroup)groups[0];
        var techGroup = (CollectionViewGroup)groups[1];
        Assert.AreEqual("Gaming", gamingGroup.Name);
        Assert.AreEqual("tech", techGroup.Name);

        var gamingArticles = gamingGroup.Items.Cast<ArticleRowViewModel>().ToArray();
        Assert.AreEqual("The next generation of handheld gaming is here", gamingArticles[0].Title);
        Assert.AreEqual("A new chapter for the world of Hyrule", gamingArticles[1].Title);
    }

    [TestMethod]
    public void DateSortShowsAFlatNewestFirstListWithFolderData()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        viewModel.IsSortByDate = true;

        Assert.IsTrue(viewModel.IsSortByDate);
        Assert.IsFalse(viewModel.IsSortByFolder);
        Assert.IsNull(viewModel.ArticleListView.Groups);
        var articles = viewModel.ArticleListView.Cast<ArticleRowViewModel>().ToArray();
        Assert.AreEqual(5, articles.Length);
        Assert.AreEqual("Gaming", articles[0].Folder);
        Assert.AreEqual("tech", articles[2].Folder);
        for (var index = 1; index < articles.Length; index++)
        {
            Assert.IsTrue(articles[index - 1].PublishedAt >= articles[index].PublishedAt);
        }
    }

    [TestMethod]
    public void UnreadAndSavedFiltersCombineWithFolderAndSearchRoutes()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        viewModel.NavigateCommand.Execute(viewModel.FeedLinks[1]);
        viewModel.SavedOnly = true;
        Assert.AreEqual(0, viewModel.VisibleArticles.Count);

        viewModel.SavedOnly = false;
        viewModel.UnreadOnly = true;
        Assert.AreEqual(2, viewModel.VisibleArticles.Count);

        viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Search"));
        viewModel.SearchQuery = "handheld";
        Assert.AreEqual(1, viewModel.VisibleArticles.Count);
        viewModel.SavedOnly = true;
        Assert.AreEqual(0, viewModel.VisibleArticles.Count);
    }

    [TestMethod]
    public void CatalogMasterStartsInCatalogManagementMode()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateCatalogMaster());

        Assert.IsTrue(viewModel.IsCatalogAdminVisible);
        Assert.AreEqual(1, viewModel.AdminLinks.Count);
        Assert.AreEqual("Manage catalog", viewModel.WorkspaceTitle);
    }

    [TestMethod]
    public void SidebarTogglePinsAtTheLeftOrCollapsesToAnOverlayTrigger()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        Assert.IsTrue(viewModel.IsSidebarPinned);
        Assert.AreEqual(286, viewModel.SidebarColumnWidth.Value);
        Assert.AreEqual(1, viewModel.SidebarColumnSpan);

        viewModel.ToggleSidebarCommand.Execute(null);

        Assert.IsFalse(viewModel.IsSidebarPinned);
        Assert.AreEqual(0, viewModel.SidebarColumnWidth.Value);
        Assert.AreEqual(2, viewModel.SidebarColumnSpan);

        viewModel.ToggleSidebarCommand.Execute(null);

        Assert.IsTrue(viewModel.IsSidebarPinned);
        Assert.AreEqual(286, viewModel.SidebarColumnWidth.Value);
        Assert.AreEqual(1, viewModel.SidebarColumnSpan);
    }

    [TestMethod]
    public void FormatAgeUsesRelativeTimeForRecentItems()
    {
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        var age = ArticleRowViewModel.FormatAge(now.AddHours(-3), now);

        Assert.AreEqual("3h ago", age);
    }

    [TestMethod]
    public void ArticleRowMakesSourceSummaryClickableAndExposesEmbeddedImage()
    {
        var article = new ArticleRowViewModel(
            "9 Chickweed Lane",
            "Arcamax",
            DateTimeOffset.UtcNow,
            "Comics",
            [],
            "Source",
            articleId: "article-1",
            feedId: "feed-1",
            link: "https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031",
            imageUrl: "https://resources.arcamax.com/newspics/396/39604/3960483.gif");

        Assert.IsFalse(article.HasReadableSummary);
        Assert.IsTrue(article.IsSourceLinkVisible);
        Assert.IsTrue(article.IsImageVisible);
    }

    private static async Task WaitForRefreshCompletionAsync(MainWindowViewModel viewModel)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (viewModel.IsRefreshing && DateTimeOffset.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.IsFalse(viewModel.IsRefreshing, "Feed refresh did not complete in time.");
    }

    private sealed class RecordingFeedDownloader : IFeedDownloader, IRawFeedContentDownloader
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> RequestedFeedIds { get; } = new();

        public IReadOnlyList<DownloadedFeedItem>? ItemsToReturn { get; set; }

        public Func<CancellationToken, Task<IReadOnlyList<DownloadedFeedItem>>>? DownloadHandler { get; set; }

        public async Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default)
        {
            RequestedFeedIds.Enqueue(feed.Id);
            if (DownloadHandler is not null)
            {
                return await DownloadHandler(cancellationToken);
            }

            return ItemsToReturn ??
            [
                new DownloadedFeedItem($"{feed.Id}-item", "New headline", null, null, "Summary", "Content")
            ];
        }

        public Task<string> DownloadRawContentAsync(CatalogFeed feed, CancellationToken cancellationToken = default) =>
            Task.FromResult($"<rss><channel><title>{feed.Name}</title></channel></rss>");
    }
}

[TestClass]
public sealed class ProfileChooserViewModelTests
{
    [TestMethod]
    public async Task CatalogMasterIsOnlyListedWhenRevealed()
    {
        var profileService = new ProfileService(new EmptyProfileStore(), new FakeHasher());
        await profileService.InitializeAsync();
        var viewModel = new ProfileChooserViewModel(profileService);
        await viewModel.InitializeAsync();

        Assert.IsTrue(viewModel.IsEmptyStateVisible);
        Assert.AreEqual(0, viewModel.Profiles.Count);

        await viewModel.SetCatalogMasterRevealedAsync(true);

        Assert.IsTrue(viewModel.IsProfileListVisible);
        Assert.AreEqual(ProfileNameValidator.CatalogMasterName, viewModel.Profiles.Single().Name);

        await viewModel.SetCatalogMasterRevealedAsync(false);

        Assert.IsTrue(viewModel.IsEmptyStateVisible);
        Assert.AreEqual(0, viewModel.Profiles.Count);
    }

    [TestMethod]
    public async Task DeleteProfileRemovesItFromTheChooser()
    {
        var store = new EmptyProfileStore();
        var profileService = new ProfileService(store, new FakeHasher());
        await profileService.InitializeAsync();
        var profile = await profileService.CreateProfileAsync("Reader", null, null);
        var viewModel = new ProfileChooserViewModel(profileService);
        await viewModel.InitializeAsync();
        viewModel.IsDeleteModeActive = true;

        await viewModel.DeleteProfileAsync(profile);

        Assert.IsTrue(viewModel.IsEmptyStateVisible);
        Assert.AreEqual(0, viewModel.Profiles.Count);
    }

    private sealed class EmptyProfileStore : IProfileStore
    {
        private readonly List<Profile> _profiles = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _profiles.Add(Profile.CreateCatalogMaster());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Profile>>(_profiles.ToArray());

        public Task<ProfilePreferences> GetPreferencesAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfilePreferences());

        public Task SavePreferencesAsync(string profileId, ProfilePreferences preferences, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Profile?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_profiles.SingleOrDefault(profile => profile.Id == id));

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_profiles.Any(profile => string.Equals(
                profile.Name,
                name,
                StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(Profile profile, CancellationToken cancellationToken = default)
        {
            _profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            _profiles.RemoveAll(profile => profile.Id == id && !profile.IsCatalogMaster);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => password;

        public bool Verify(string encodedHash, string password) => encodedHash == password;
    }
}