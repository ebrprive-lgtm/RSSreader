using System.IO;
using System.Text;
using RssReader.App.ViewModels;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.Tests;

[TestClass]
public sealed class CatalogManagementViewModelTests
{
    [TestMethod]
    public async Task OpmlImportAddsFeedsAndMapsFoldersToCategories()
    {
        const string opml = "<opml version=\"2.0\"><body><outline text=\"Science\"><outline text=\"NASA\" xmlUrl=\"https://example.com/nasa.xml\" /></outline></body></opml>";
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(new MemoryCatalogStore()));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(opml));

        await viewModel.ImportOpmlAsync(stream);

        Assert.AreEqual(1, viewModel.Feeds.Count);
        Assert.AreEqual("NASA", viewModel.Feeds.Single().Name);
        Assert.AreEqual("Science", viewModel.Feeds.Single().CategoryName);
        Assert.AreEqual("Added 1 feed(s); skipped 0.", viewModel.ImportMessage);
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task OpmlImportKeepsSameNameFeedsDistinctAndFlagsThemForReview()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);

        var result = await service.ImportFeedsAsync(Profile.CreateCatalogMaster(),
        [
            new OpmlFeed("Example Journal", "https://publisher-a.example/feed.xml", null, null, "https://publisher-a.example"),
            new OpmlFeed("example journal", "https://publisher-b.example/feed.xml", null, null, "https://publisher-b.example")
        ]);
        var viewModel = new CatalogManagementViewModel(Profile.CreateCatalogMaster(), service);
        await viewModel.InitializeAsync();

        Assert.AreEqual(2, result.AddedCount);
        Assert.AreEqual(2, viewModel.Feeds.Count);
        Assert.IsTrue(viewModel.Feeds.All(feed => feed.SameNameDisplay == "2 feeds share this name"));
        CollectionAssert.AreEquivalent(
            new[] { "https://publisher-a.example/", "https://publisher-b.example/" },
            viewModel.Feeds.Select(feed => feed.WebsiteUrl).ToArray());
    }

    [TestMethod]
    public async Task StarterPackAddsCuratedFeedsAndSkipsThemWhenRepeated()
    {
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(new MemoryCatalogStore()));

        await viewModel.LoadStarterPackCommand.ExecuteAsync();
        Assert.AreEqual(6, viewModel.Feeds.Count);
        Assert.AreEqual("Added 6 feed(s); skipped 0.", viewModel.ImportMessage);

        await viewModel.LoadStarterPackCommand.ExecuteAsync();

        Assert.AreEqual(6, viewModel.Feeds.Count);
        Assert.AreEqual("Added 0 feed(s); skipped 6.", viewModel.ImportMessage);
    }

    [TestMethod]
    public async Task CatalogMasterCommandsCreateAndOrganizeCatalogEntries()
    {
        var store = new MemoryCatalogStore();
        var viewModel = new CatalogManagementViewModel(Profile.CreateCatalogMaster(), new CatalogService(store));
        await viewModel.InitializeAsync();
        viewModel.CategoryName = "Technology";
        await viewModel.AddCategoryCommand.ExecuteAsync();
        viewModel.FeedName = "The Verge";
        viewModel.FeedUrl = "https://example.com/feed.xml";
        viewModel.SelectedCategory = viewModel.Categories.Single();
        await viewModel.AddFeedCommand.ExecuteAsync();
        viewModel.CollectionName = "Daily reads";
        await viewModel.AddCollectionCommand.ExecuteAsync();
        viewModel.SelectedFeed = viewModel.Feeds.Single();
        viewModel.SelectedCollection = viewModel.Collections.Single();

        await viewModel.AddFeedToCollectionCommand.ExecuteAsync();

        Assert.AreEqual("The Verge", viewModel.Feeds.Single().Name);
        Assert.AreEqual("Technology", viewModel.Feeds.Single().CategoryName);
        Assert.AreEqual(viewModel.Feeds.Single().Id, store.CollectionFeedIds.Single());

        await viewModel.RemoveFeedFromCollectionCommand.ExecuteAsync();

        Assert.AreEqual(0, store.CollectionFeedIds.Count);
    }

    [TestMethod]
    public async Task CatalogMasterFeedAppearsInFollowSourcesImmediatelyAfterCreation()
    {
        var catalogService = new CatalogService(new MemoryCatalogStore());
        var viewModel = new MainWindowViewModel(Profile.CreateCatalogMaster(), catalogService);
        await viewModel.InitializeAsync();
        var catalogManagement = viewModel.CatalogManagement!;
        catalogManagement.FeedName = "New publication";
        catalogManagement.FeedUrl = "https://example.com/feed.xml";

        await catalogManagement.AddFeedCommand.ExecuteAsync();

        viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));
        viewModel.CatalogSearchQuery = "New publication";

        var feed = viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Single();
        Assert.AreEqual("New publication", feed.Name);
        Assert.AreEqual(1, viewModel.CatalogFeeds.Count);
    }

    [TestMethod]
    public async Task CatalogMasterCanEditExistingFeedMetadata()
    {
        var store = new MemoryCatalogStore();
        var catalogService = new CatalogService(store);
        var actor = Profile.CreateCatalogMaster();
        var original = await catalogService.AddFeedAsync(
            actor,
            "Example",
            "https://example.com/feed.xml",
            null,
            null);
        var viewModel = new CatalogManagementViewModel(actor, catalogService);
        await viewModel.InitializeAsync();
        viewModel.PrepareFeedEdit(viewModel.Feeds.Single());
        viewModel.FeedName = "Example Journal";
        viewModel.FeedDescription = "Edited description";
        viewModel.FeedWebsiteUrl = "https://example.com/journal";

        await viewModel.AddFeedCommand.ExecuteAsync();

        var edited = viewModel.Feeds.Single();
        Assert.AreEqual(original.Id, edited.Id);
        Assert.AreEqual("Example Journal", edited.Name);
        Assert.AreEqual("Edited description", edited.Description);
        Assert.AreEqual("https://example.com/journal", edited.WebsiteUrl);
    }

    [TestMethod]
    public async Task CatalogMasterCanRenameCategoryWithoutChangingFeedAssignment()
    {
        var store = new MemoryCatalogStore();
        var category = new CatalogCategory("category-comics", "Comics");
        await store.AddCategoryAsync(category);
        var feed = new CatalogFeed("comic-feed", "Comic", "https://example.com/feed.xml", null, category.Id);
        await store.AddFeedAsync(feed);
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store));
        await viewModel.InitializeAsync();

        viewModel.PrepareCategoryEdit(viewModel.Categories.Single());
        viewModel.CategoryName = "Comics and cartoons";
        await viewModel.AddCategoryCommand.ExecuteAsync();

        Assert.AreEqual(category.Id, viewModel.Categories.Single().Id);
        Assert.AreEqual("Comics and cartoons", viewModel.Categories.Single().Name);
        Assert.AreEqual("Comics and cartoons", viewModel.Feeds.Single().CategoryName);
    }

    [TestMethod]
    public async Task CatalogMasterCanMergeEditedCategoryIntoExistingCategory()
    {
        var store = new MemoryCatalogStore();
        var sourceCategory = new CatalogCategory("category-science", "Science");
        var targetCategory = new CatalogCategory("category-research", "Research");
        await store.AddCategoryAsync(sourceCategory);
        await store.AddCategoryAsync(targetCategory);
        await store.AddFeedAsync(new CatalogFeed(
            "science-feed",
            "Science feed",
            "https://example.com/science.xml",
            null,
            sourceCategory.Id));
        await store.AddFeedAsync(new CatalogFeed(
            "research-feed",
            "Research feed",
            "https://example.com/research.xml",
            null,
            targetCategory.Id));
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store));
        await viewModel.InitializeAsync();

        viewModel.PrepareCategoryEdit(viewModel.Categories.Single(category => category.Id == sourceCategory.Id));
        Assert.IsFalse(viewModel.CanMergeCategory);
        viewModel.CategoryName = " research ";

        Assert.IsTrue(viewModel.CanMergeCategory);
        Assert.AreEqual(targetCategory.Id, viewModel.MergeTargetCategory!.Id);
        Assert.AreEqual("Merge into Research", viewModel.MergeCategoryButtonLabel);
        await viewModel.MergeCategoryCommand.ExecuteAsync();

        Assert.AreEqual(1, viewModel.Categories.Count);
        Assert.AreEqual(targetCategory.Id, viewModel.Categories.Single().Id);
        Assert.IsTrue(viewModel.Feeds.All(feed => feed.CategoryId == targetCategory.Id));
        Assert.IsTrue(viewModel.Feeds.All(feed => feed.CategoryName == targetCategory.Name));
        Assert.IsFalse(viewModel.CanMergeCategory);
    }

    [TestMethod]
    public async Task CatalogMasterPreviewReturnsOnlySanitizedSampleItems()
    {
        var store = new MemoryCatalogStore();
        var feed = new CatalogFeed("preview-feed", "Preview feed", "https://example.com/feed.xml", "Description", null);
        await store.AddFeedAsync(feed);
        var previewService = new CatalogFeedPreviewService(new PreviewFeedDownloader());
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store),
            previewService);
        await viewModel.InitializeAsync();

        var preview = await viewModel.PreviewFeedAsync(viewModel.Feeds.Single());

        Assert.AreEqual("Preview feed", preview.Name);
        Assert.AreEqual(5, preview.Items.Count);
        Assert.IsTrue(preview.Items.All(item => item.Content is null && item.ImageUrl is null));
    }

    [TestMethod]
    public async Task CatalogMasterCanFilterFeedsWithMetadataGaps()
    {
        var store = new MemoryCatalogStore();
        var category = new CatalogCategory("category-comics", "Comics");
        await store.AddCategoryAsync(category);
        var completeFeed = new CatalogFeed(
            "complete-feed",
            "Complete",
            "https://complete.example/feed.xml",
            "A complete feed",
            category.Id,
            "https://complete.example");
        var incompleteFeed = new CatalogFeed(
            "incomplete-feed",
            "Incomplete",
            "https://incomplete.example/feed.xml",
            null,
            null);
        await store.AddFeedAsync(completeFeed);
        await store.AddFeedAsync(incompleteFeed);

        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store));
        await viewModel.InitializeAsync();

        Assert.AreEqual("Metadata gaps: 1", viewModel.MetadataReviewSummary);
        Assert.AreEqual(string.Empty, viewModel.Feeds.Single(feed => feed.Id == completeFeed.Id).MetadataReviewDisplay);
        Assert.AreEqual(
            "Metadata gaps: description, category, publisher website",
            viewModel.Feeds.Single(feed => feed.Id == incompleteFeed.Id).MetadataReviewDisplay);
        Assert.AreEqual(2, viewModel.VisibleFeedCount);

        viewModel.ShowMetadataGapsOnly = true;

        Assert.AreEqual(incompleteFeed.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
        Assert.AreEqual(2, viewModel.Feeds.Count);
        Assert.AreEqual("Metadata gaps: 1", viewModel.MetadataReviewSummary);
    }

    [TestMethod]
    public async Task CatalogMasterSearchAndFiltersComposeAndReportMatchingCounts()
    {
        var store = new MemoryCatalogStore();
        var science = new CatalogCategory("category-science", "Science");
        var technology = new CatalogCategory("category-technology", "Technology");
        var spaceCollection = new CatalogCollection("collection-space", "Space sources");
        await store.AddCategoryAsync(science);
        await store.AddCategoryAsync(technology);
        await store.AddCollectionAsync(spaceCollection);

        var nasaSpace = new CatalogFeed(
            "feed-nasa-space",
            "NASA Space News",
            "https://feeds.example/nasa.xml",
            "Space agency reports",
            science.Id,
            "https://nasa.example");
        var nasaMissions = new CatalogFeed(
            "feed-nasa-missions",
            "NASA Mission Updates",
            "https://publisher-b.example/rss",
            null,
            science.Id);
        var technologyNews = new CatalogFeed(
            "feed-tech-news",
            "Tech Desk",
            "https://tech.example/rss",
            "Hardware news",
            technology.Id,
            "https://tech.example");
        await store.AddFeedAsync(nasaSpace);
        await store.AddFeedAsync(nasaMissions);
        await store.AddFeedAsync(technologyNews);
        await store.AddFeedToCollectionAsync(spaceCollection.Id, nasaSpace.Id);
        await store.AddFeedToCollectionAsync(spaceCollection.Id, nasaMissions.Id);

        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store, new TestFeedDownloader(feed => feed.Id == nasaMissions.Id)));
        await viewModel.InitializeAsync();

        Assert.AreEqual("Showing 3 of 3 feeds", viewModel.FeedResultsSummary);
        CollectionAssert.AreEqual(
            new[] { "NASA Mission Updates", "NASA Space News", "Tech Desk" },
            viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Select(feed => feed.Name).ToArray());

        viewModel.SearchQuery = "  nasa   space ";
        Assert.AreEqual(nasaSpace.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
        Assert.AreEqual("Showing 1 of 3 feeds", viewModel.FeedResultsSummary);

        viewModel.SearchQuery = "no such feed";
        Assert.IsTrue(viewModel.IsFeedResultsEmpty);
        Assert.AreEqual("No feeds match these filters.", viewModel.FeedResultsEmptyMessage);

        viewModel.ClearFiltersCommand.Execute(null);
        viewModel.SelectedCategoryFilter = viewModel.CategoryFilterOptions.Single(option => option.CategoryId == science.Id);
        Assert.AreEqual(2, viewModel.VisibleFeedCount);

        viewModel.SelectedCollectionFilter = viewModel.CollectionFilterOptions.Single(option => option.CollectionId == spaceCollection.Id);
        Assert.AreEqual(2, viewModel.VisibleFeedCount);

        viewModel.ShowMetadataGapsOnly = true;
        Assert.AreEqual(nasaMissions.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);

        viewModel.ClearFiltersCommand.Execute(null);
        await viewModel.CheckFeedHealthCommand.ExecuteAsync(viewModel.Feeds.Single(feed => feed.Id == nasaSpace.Id));
        await viewModel.CheckFeedHealthCommand.ExecuteAsync(viewModel.Feeds.Single(feed => feed.Id == nasaMissions.Id));

        viewModel.SelectedHealthFilter = viewModel.HealthFilterOptions.Single(option => option.Filter == CatalogFeedHealthFilter.Succeeded);
        Assert.AreEqual(nasaSpace.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
        viewModel.SelectedHealthFilter = viewModel.HealthFilterOptions.Single(option => option.Filter == CatalogFeedHealthFilter.Failed);
        Assert.AreEqual(nasaMissions.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
        viewModel.SelectedHealthFilter = viewModel.HealthFilterOptions.Single(option => option.Filter == CatalogFeedHealthFilter.NotChecked);
        Assert.AreEqual(technologyNews.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
    }

    [TestMethod]
    public async Task CatalogMasterCanFilterFiveThousandFeeds()
    {
        var store = new MemoryCatalogStore();
        for (var index = 0; index < 5_000; index++)
        {
            var feedId = $"feed-{index:D5}";
            await store.AddFeedAsync(new CatalogFeed(
                feedId,
                $"Feed {index:D5}",
                $"https://feeds.example/{index:D5}.xml",
                null,
                null));
        }

        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store));
        await viewModel.InitializeAsync();

        Assert.AreEqual(5_000, viewModel.VisibleFeedCount);
        viewModel.SearchQuery = "04999";

        Assert.AreEqual(1, viewModel.VisibleFeedCount);
        Assert.AreEqual("feed-04999", viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
        Assert.AreEqual("Showing 1 of 5000 feeds", viewModel.FeedResultsSummary);
    }

    [TestMethod]
    public async Task CatalogMasterCanCheckFeedAndDisplayTimestampedHealth()
    {
        var store = new MemoryCatalogStore();
        var actor = Profile.CreateCatalogMaster();
        var catalogService = new CatalogService(store, new TestFeedDownloader());
        var feed = await catalogService.AddFeedAsync(
            actor,
            "Checkable",
            "https://example.com/feed.xml",
            null,
            null);
        var viewModel = new CatalogManagementViewModel(actor, catalogService);
        await viewModel.InitializeAsync();
        var item = viewModel.Feeds.Single();

        Assert.AreEqual("Not checked", item.HealthCheckDisplay);

        await viewModel.CheckFeedHealthCommand.ExecuteAsync(item);

        var checkedItem = viewModel.Feeds.Single();
        Assert.IsTrue(checkedItem.LastHealthCheckSucceeded);
        Assert.IsNotNull(checkedItem.LastHealthCheckedAt);
        StringAssert.StartsWith(checkedItem.HealthCheckDisplay, "Feed valid - checked ");
    }

    private sealed class MemoryCatalogStore : ICatalogStore
    {
        private readonly List<CatalogFeed> _feeds = [];
        private readonly List<CatalogCategory> _categories = [];
        private readonly List<CatalogCollection> _collections = [];
        private readonly HashSet<(string CollectionId, string FeedId)> _memberships = [];

        public IReadOnlyList<string> CollectionFeedIds => _memberships.Select(item => item.FeedId).ToArray();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CatalogFeed>> GetFeedsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogFeed>>(_feeds.ToArray());
        public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogCategory>>(_categories.ToArray());
        public Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogCollection>>(_collections.ToArray());
        public Task<IReadOnlyList<string>> GetCollectionFeedIdsAsync(string collectionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(_memberships.Where(item => item.CollectionId == collectionId).Select(item => item.FeedId).ToArray());
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetCollectionFeedIdsByCollectionAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(_memberships.GroupBy(item => item.CollectionId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(item => item.FeedId).ToArray(), StringComparer.Ordinal));

        public Task AddFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default)
        {
            _feeds.Add(feed);
            return Task.CompletedTask;
        }

        public Task UpdateFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default)
        {
            var index = _feeds.FindIndex(item => item.Id == feed.Id);
            if (index >= 0)
            {
                _feeds[index] = feed;
            }

            return Task.CompletedTask;
        }

        public Task DeleteFeedAsync(string feedId, CancellationToken cancellationToken = default)
        {
            _feeds.RemoveAll(item => item.Id == feedId);
            _memberships.RemoveWhere(item => item.FeedId == feedId);
            return Task.CompletedTask;
        }

        public Task AddCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default)
        {
            _categories.Add(category);
            return Task.CompletedTask;
        }

        public Task UpdateCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default)
        {
            var index = _categories.FindIndex(item => item.Id == category.Id);
            if (index >= 0)
            {
                _categories[index] = category;
            }

            return Task.CompletedTask;
        }

        public Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
        {
            _categories.RemoveAll(item => item.Id == categoryId);
            return Task.CompletedTask;
        }

        public Task MergeCategoriesAsync(string sourceCategoryId, string targetCategoryId, CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < _feeds.Count; index++)
            {
                if (_feeds[index].CategoryId == sourceCategoryId)
                {
                    _feeds[index] = _feeds[index] with { CategoryId = targetCategoryId };
                }
            }

            _categories.RemoveAll(category => category.Id == sourceCategoryId);
            return Task.CompletedTask;
        }

        public Task AddCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default)
        {
            _collections.Add(collection);
            return Task.CompletedTask;
        }

        public Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken = default)
        {
            _collections.RemoveAll(item => item.Id == collectionId);
            _memberships.RemoveWhere(item => item.CollectionId == collectionId);
            return Task.CompletedTask;
        }

        public Task AddFeedToCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default)
        {
            _memberships.Add((collectionId, feedId));
            return Task.CompletedTask;
        }

        public Task RemoveFeedFromCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default)
        {
            _memberships.Remove((collectionId, feedId));
            return Task.CompletedTask;
        }
    }

    private sealed class TestFeedDownloader(Func<CatalogFeed, bool>? shouldFail = null) : IFeedDownloader
    {
        public Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default) => shouldFail?.Invoke(feed) == true
                ? Task.FromException<IReadOnlyList<DownloadedFeedItem>>(new InvalidOperationException("Feed check failed."))
                : Task.FromResult<IReadOnlyList<DownloadedFeedItem>>([]);
    }

    private sealed class PreviewFeedDownloader : IFeedDownloader
    {
        public Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadedFeedItem>>(Enumerable.Range(1, 6)
                .Select(index => new DownloadedFeedItem(
                    $"article-{index}",
                    $"Article {index}",
                    $"https://example.com/{index}",
                    DateTimeOffset.UtcNow,
                    "Sample summary",
                    "Full content that should not be previewed",
                    "https://example.com/image.jpg"))
                .ToArray());
    }
}