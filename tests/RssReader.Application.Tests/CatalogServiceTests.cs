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
        Assert.AreEqual(category.Id, feed.CategoryId);
        Assert.AreEqual(feed.Id, (await service.GetCollectionFeedIdsAsync(collection.Id)).Single());
    }

    [TestMethod]
    public async Task FeedUrlsNormalizeForDuplicateDetectionButSameNameFeedsAreAllowed()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);
        var actor = Profile.CreateCatalogMaster();
        const string feedName = "Same publisher";

        var original = await service.AddFeedAsync(
            actor,
            feedName,
            "https://EXAMPLE.com:443/feed.xml",
            null,
            null);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.AddFeedAsync(
            actor,
            "Renamed duplicate",
            "https://example.com/feed.xml",
            null,
            null));

        var distinctFeed = await service.AddFeedAsync(
            actor,
            feedName,
            "https://different.example/feed.xml",
            null,
            null);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.UpdateFeedAsync(
            actor,
            distinctFeed.Id,
            distinctFeed.Name,
            "https://EXAMPLE.com:443/feed.xml",
            distinctFeed.Description,
            distinctFeed.CategoryId));

        var feeds = await service.GetFeedsAsync();
        Assert.AreEqual(2, feeds.Count);
        Assert.AreEqual(original.Id, feeds[0].Id);
        Assert.AreEqual(distinctFeed.Id, feeds[1].Id);
        Assert.AreEqual(feedName, feeds[0].Name);
        Assert.AreEqual(feedName, feeds[1].Name);
    }

    [TestMethod]
    public async Task CatalogMasterCanUpdateFeedMetadataWithoutChangingIdentity()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);
        var actor = Profile.CreateCatalogMaster();
        var original = await service.AddFeedAsync(
            actor,
            "Example",
            "https://example.com/feed.xml",
            null,
            null);

        var updated = await service.UpdateFeedAsync(
            actor,
            original.Id,
            "Example Journal",
            original.FeedUrl,
            "A better description",
            null,
            websiteUrl: "https://example.com/journal");

        Assert.AreEqual(original.Id, updated.Id);
        Assert.AreEqual("Example Journal", updated.Name);
        Assert.AreEqual("A better description", updated.Description);
        Assert.AreEqual("https://example.com/journal", updated.WebsiteUrl);
        Assert.AreEqual(1, (await service.GetFeedsAsync()).Count);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.UpdateFeedAsync(
            actor,
            original.Id,
            updated.Name,
            updated.FeedUrl,
            updated.Description,
            null,
            websiteUrl: "file:///C:/publisher"));
    }

    [TestMethod]
    public async Task CatalogMasterFeedHealthCheckPersistsSuccessAndFailureWithTimestamps()
    {
        var store = new MemoryCatalogStore();
        var feed = new CatalogFeed("health-feed", "Health feed", "https://example.com/feed.xml", null, null);
        await store.AddFeedAsync(feed);
        var actor = Profile.CreateCatalogMaster();
        var successfulService = new CatalogService(store, new TestFeedDownloader((_, _) =>
            Task.FromResult<IReadOnlyList<DownloadedFeedItem>>([])));

        var successfulCheck = await successfulService.CheckFeedHealthAsync(actor, feed.Id);
        var persistedSuccess = (await store.GetFeedsAsync()).Single();

        Assert.IsTrue(successfulCheck.IsSuccessful);
        Assert.IsNull(successfulCheck.ErrorMessage);
        Assert.AreEqual(successfulCheck.CheckedAt, persistedSuccess.LastHealthCheckedAt);
        Assert.AreEqual(true, persistedSuccess.LastHealthCheckSucceeded);

        var failedService = new CatalogService(store, new TestFeedDownloader((_, _) =>
            Task.FromException<IReadOnlyList<DownloadedFeedItem>>(new InvalidOperationException("offline"))));
        var failedCheck = await failedService.CheckFeedHealthAsync(actor, feed.Id);
        var persistedFailure = (await store.GetFeedsAsync()).Single();

        Assert.IsFalse(failedCheck.IsSuccessful);
        StringAssert.Contains(failedCheck.ErrorMessage, "offline");
        Assert.AreEqual(failedCheck.CheckedAt, persistedFailure.LastHealthCheckedAt);
        Assert.AreEqual(false, persistedFailure.LastHealthCheckSucceeded);
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
            successfulService.CheckFeedHealthAsync(Profile.CreateRegular("Reader"), feed.Id));
    }

    [TestMethod]
    public async Task CancelledFeedHealthCheckDoesNotReplacePreviousResult()
    {
        var store = new MemoryCatalogStore();
        var checkedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var feed = new CatalogFeed(
            "health-feed",
            "Health feed",
            "https://example.com/feed.xml",
            null,
            null,
            null,
            checkedAt,
            true);
        await store.AddFeedAsync(feed);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var downloader = new TestFeedDownloader((_, token) =>
            Task.FromCanceled<IReadOnlyList<DownloadedFeedItem>>(token));
        var service = new CatalogService(store, downloader);

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            service.CheckFeedHealthAsync(Profile.CreateCatalogMaster(), feed.Id, cancellation.Token));

        var unchangedFeed = (await store.GetFeedsAsync()).Single();
        Assert.AreEqual(checkedAt, unchangedFeed.LastHealthCheckedAt);
        Assert.AreEqual(true, unchangedFeed.LastHealthCheckSucceeded);
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
    public async Task CatalogMasterCanRenameCollectionAndPreservesMemberships()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);
        var actor = Profile.CreateCatalogMaster();
        var collection = await service.AddCollectionAsync(actor, "Gaming");
        await service.AddCollectionAsync(actor, "News");
        var feed = new CatalogFeed("pc-game-feed", "PC games", "https://example.com/games.xml", null, null);
        await store.AddFeedAsync(feed);
        await service.AddFeedToCollectionAsync(actor, collection.Id, feed.Id);

        var renamed = await service.UpdateCollectionAsync(actor, collection.Id, " PC Gaming ");

        Assert.AreEqual(collection.Id, renamed.Id);
        Assert.AreEqual("PC Gaming", renamed.Name);
        Assert.AreEqual(feed.Id, (await store.GetCollectionFeedIdsAsync(collection.Id)).Single());
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.UpdateCollectionAsync(actor, collection.Id, "news"));
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
            () => service.UpdateCollectionAsync(Profile.CreateRegular("Reader"), collection.Id, "Reading"));
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
            new OpmlFeed("The Verge", "https://example.com/feed.xml", null, "technology", "https://www.theverge.com"),
            new OpmlFeed("Duplicate", "https://EXAMPLE.com/feed.xml", null, "Technology"),
            new OpmlFeed("the verge", "https://different.example/feed.xml", null, "Technology"),
            new OpmlFeed("Invalid URL", "file:///feed.xml", null, null),
            new OpmlFeed(" ", "https://example.com/unnamed.xml", null, null)
        ]);

        Assert.AreEqual(2, result.AddedCount);
        Assert.AreEqual(3, result.SkippedCount);
        Assert.AreEqual(2, result.DuplicateCandidates.Count);
        Assert.IsTrue(result.DuplicateCandidates.Any(candidate =>
            candidate.Kind == FeedDuplicateKind.ExactUrl && candidate.ImportedName == "Duplicate"));
        Assert.IsTrue(result.DuplicateCandidates.Any(candidate =>
            candidate.Kind == FeedDuplicateKind.SameNameDifferentUrl && candidate.ImportedName == "the verge"));
        var importedFeed = (await service.GetFeedsAsync()).Single(feed => feed.FeedUrl == "https://example.com/feed.xml");
        Assert.AreEqual(category.Id, importedFeed.CategoryId);
        Assert.AreEqual("https://www.theverge.com/", importedFeed.WebsiteUrl);
        Assert.AreEqual(2, (await service.GetFeedsAsync()).Count);
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

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetCollectionFeedIdsByCollectionAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(_memberships
                .GroupBy(pair => pair.CollectionId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group.Select(pair => pair.FeedId).ToArray(),
                    StringComparer.Ordinal));

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
            _feeds.RemoveAll(feed => feed.Id == feedId);
            _memberships.RemoveWhere(pair => pair.FeedId == feedId);
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
            _categories.RemoveAll(category => category.Id == categoryId);
            return Task.CompletedTask;
        }

        public Task MergeCategoriesAsync(string sourceCategoryId, string targetCategoryId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default)
        {
            _collections.Add(collection);
            return Task.CompletedTask;
        }

        public Task UpdateCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default)
        {
            var index = _collections.FindIndex(item => item.Id == collection.Id);
            if (index >= 0)
            {
                _collections[index] = collection;
            }

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

    private sealed class TestFeedDownloader(
        Func<CatalogFeed, CancellationToken, Task<IReadOnlyList<DownloadedFeedItem>>> download) : IFeedDownloader
    {
        public Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default) => download(feed, cancellationToken);
    }
}