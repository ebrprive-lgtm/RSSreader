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

        public Task AddFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default)
        {
            _feeds.Add(feed);
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

        public Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
        {
            _categories.RemoveAll(item => item.Id == categoryId);
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
}