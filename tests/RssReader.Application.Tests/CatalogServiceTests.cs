using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Application.Tests;

[TestClass]
public sealed class CatalogServiceTests
{
    [TestMethod]
    public async Task RegularProfileCannotModifyCatalog()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);
        var regularProfile = Profile.CreateRegular("Reader");

        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
            () => service.AddCategoryAsync(regularProfile, "Technology"));
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
            () => service.AddFeedAsync(regularProfile, "The Verge", "https://example.com/feed.xml", null, null));

        Assert.AreEqual(0, (await store.GetCategoriesAsync()).Count);
        Assert.AreEqual(0, (await store.GetFeedsAsync()).Count);
    }

    [TestMethod]
    public async Task CatalogMasterCanCreateCategoriesFeedsAndCollections()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);
        var catalogMaster = Profile.CreateCatalogMaster();

        var category = await service.AddCategoryAsync(catalogMaster, "Technology");
        var feed = await service.AddFeedAsync(
            catalogMaster,
            "The Verge",
            "https://example.com/feed.xml",
            "Technology coverage",
            category.Id);
        var collection = await service.AddCollectionAsync(catalogMaster, "Daily reads");
        await service.AddFeedToCollectionAsync(catalogMaster, collection.Id, feed.Id);

        Assert.AreEqual(1, (await service.GetFeedsAsync()).Count);
        Assert.AreEqual(feed.Id, (await service.GetCollectionFeedIdsAsync(collection.Id)).Single());
    }

    [TestMethod]
    public async Task CatalogMasterAuthorizationRequiresReservedIdentity()
    {
        var service = new CatalogService(new MemoryCatalogStore());
        var forgedProfile = new Profile("not-catalog-master", "Catalog Master", true);

        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
            () => service.AddCollectionAsync(forgedProfile, "Featured"));
    }

    [TestMethod]
    public async Task AddFeedRejectsNonHttpUrls()
    {
        var service = new CatalogService(new MemoryCatalogStore());

        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => service.AddFeedAsync(
                Profile.CreateCatalogMaster(),
                "Example",
                "file:///C:/feed.xml",
                null,
                null));
    }

    [TestMethod]
    public async Task ImportFeedsMapsCategoriesAndSkipsInvalidOrDuplicateEntries()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);
        var catalogMaster = Profile.CreateCatalogMaster();
        var category = await service.AddCategoryAsync(catalogMaster, "Technology");

        var result = await service.ImportFeedsAsync(catalogMaster,
        [
            new OpmlFeed("The Verge", "https://example.com/feed.xml", null, "technology"),
            new OpmlFeed("Duplicate", "https://EXAMPLE.com/feed.xml", null, "Technology"),
            new OpmlFeed("Invalid URL", "file:///feed.xml", null, null),
            new OpmlFeed(" ", "https://example.com/unnamed.xml", null, null)
        ]);

        Assert.AreEqual(1, result.AddedCount);
        Assert.AreEqual(3, result.SkippedCount);
        Assert.AreEqual(category.Id, (await service.GetFeedsAsync()).Single().CategoryId);
        Assert.AreEqual(1, (await service.GetCategoriesAsync()).Count);
    }

    [TestMethod]
    public async Task RegularProfileCannotImportFeedsIntoSharedCatalog()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);

        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
            () => service.ImportFeedsAsync(
                Profile.CreateRegular("Reader"),
                [new OpmlFeed("Example", "https://example.com/feed.xml", null, null)]));

        Assert.AreEqual(0, (await store.GetFeedsAsync()).Count);
    }

    private sealed class MemoryCatalogStore : ICatalogStore
    {
        private readonly List<CatalogFeed> _feeds = [];
        private readonly List<CatalogCategory> _categories = [];
        private readonly List<CatalogCollection> _collections = [];
        private readonly HashSet<(string CollectionId, string FeedId)> _memberships = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<CatalogFeed>> GetFeedsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogFeed>>(_feeds.ToArray());

        public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogCategory>>(_categories.ToArray());

        public Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogCollection>>(_collections.ToArray());

        public Task<IReadOnlyList<string>> GetCollectionFeedIdsAsync(
            string collectionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(_memberships
                .Where(pair => pair.CollectionId == collectionId)
                .Select(pair => pair.FeedId)
                .ToArray());

        public Task AddFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default)
        {
            _feeds.Add(feed);
            return Task.CompletedTask;
        }

        public Task DeleteFeedAsync(string feedId, CancellationToken cancellationToken = default)
        {
            _feeds.RemoveAll(feed => feed.Id == feedId);
            _memberships.RemoveWhere(pair => pair.FeedId == feedId);
            return Task.CompletedTask;
        }

        public Task AddCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default)
        {
            _categories.Add(category);
            return Task.CompletedTask;
        }

        public Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
        {
            _categories.RemoveAll(category => category.Id == categoryId);
            return Task.CompletedTask;
        }

        public Task AddCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default)
        {
            _collections.Add(collection);
            return Task.CompletedTask;
        }

        public Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken = default)
        {
            _collections.RemoveAll(collection => collection.Id == collectionId);
            _memberships.RemoveWhere(pair => pair.CollectionId == collectionId);
            return Task.CompletedTask;
        }

        public Task AddFeedToCollectionAsync(
            string collectionId,
            string feedId,
            CancellationToken cancellationToken = default)
        {
            _memberships.Add((collectionId, feedId));
            return Task.CompletedTask;
        }

        public Task RemoveFeedFromCollectionAsync(
            string collectionId,
            string feedId,
            CancellationToken cancellationToken = default)
        {
            _memberships.Remove((collectionId, feedId));
            return Task.CompletedTask;
        }
    }
}