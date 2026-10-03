using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
                var window = new RssReader.App.MainWindow(
                    viewModel);
                var preferencesRequested = false;
                var logoutRequested = false;
                window.PreferencesRequested += () => preferencesRequested = true;
                window.LogoutRequested += () => logoutRequested = true;
                window.Show();
                window.UpdateLayout();
                var preferences = new ProfilePreferences(
                    ProfileStartPage.FirstFolder,
                    ProfileArticlePresentation.Magazine,
                    ProfileArticleSort.Newest,
                    true,
                    25,
                    false,
                    60);
                var preferencesWindow = new RssReader.App.PreferencesWindow(preferences) { Owner = window };
                preferencesWindow.Show();
                preferencesWindow.UpdateLayout();
                Assert.IsTrue(((RadioButton)preferencesWindow.FindName("StartFirstFolderOption")).IsChecked);
                Assert.IsTrue(((RadioButton)preferencesWindow.FindName("PresentationMagazineOption")).IsChecked);
                Assert.AreEqual("25", ((TextBox)preferencesWindow.FindName("FolderArticleLimitBox")).Text);
                Assert.IsFalse(((CheckBox)preferencesWindow.FindName("RefreshFeedsWhenOpenedCheckBox")).IsChecked);
                Assert.AreEqual("Every hour", ((ComboBoxItem)((ComboBox)preferencesWindow.FindName("AutoRefreshIntervalComboBox")).SelectedItem).Content);
                preferencesWindow.Close();

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
                null);
            await viewModel.InitializeAsync();

            Assert.AreEqual(1, viewModel.VisibleArticles.Count);
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

            viewModel.ApplyPreferences(new ProfilePreferences(RefreshFeedsWhenOpened: true));
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "folder:News"));
            await WaitForRefreshCompletionAsync(viewModel);

            Assert.AreEqual(3, downloader.RequestedFeedIds.Count);
            CollectionAssert.AreEquivalent(
                new[] { existingFeed.Id, newFeed.Id },
                downloader.RequestedFeedIds.Skip(1).ToArray());
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

    private static async Task WaitForRefreshCompletionAsync(MainWindowViewModel viewModel)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (viewModel.IsRefreshing && DateTimeOffset.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.IsFalse(viewModel.IsRefreshing, "Feed refresh did not complete in time.");
    }

    private sealed class RecordingFeedDownloader : IFeedDownloader
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> RequestedFeedIds { get; } = new();

        public Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default)
        {
            RequestedFeedIds.Enqueue(feed.Id);
            IReadOnlyList<DownloadedFeedItem> items =
            [
                new DownloadedFeedItem($"{feed.Id}-item", "New headline", null, null, "Summary", "Content")
            ];
            return Task.FromResult(items);
        }
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