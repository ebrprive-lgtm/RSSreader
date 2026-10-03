using RssReader.Domain;
using RssReader.Infrastructure;

namespace RssReader.Infrastructure.Tests;

[TestClass]
public sealed class SqliteCatalogStoreTests
{
    [TestMethod]
    public async Task CatalogEntriesAndCollectionMembershipPersist()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteCatalogStore(database.Path);
        await store.InitializeAsync();
        var category = new CatalogCategory(Guid.NewGuid().ToString("N"), "Technology");
        var feed = new CatalogFeed(Guid.NewGuid().ToString("N"), "Example", "https://example.com/feed.xml", null, category.Id);
        var collection = new CatalogCollection(Guid.NewGuid().ToString("N"), "Daily reads");
        await store.AddCategoryAsync(category);
        await store.AddFeedAsync(feed);
        await store.AddCollectionAsync(collection);
        await store.AddFeedToCollectionAsync(collection.Id, feed.Id);

        var reloadedStore = new SqliteCatalogStore(database.Path);
        var loadedFeed = (await reloadedStore.GetFeedsAsync()).Single();
        var loadedCollection = (await reloadedStore.GetCollectionsAsync()).Single();
        var members = await reloadedStore.GetCollectionFeedIdsAsync(loadedCollection.Id);

        Assert.AreEqual(category.Id, loadedFeed.CategoryId);
        Assert.AreEqual(feed.Id, members.Single());
    }

    [TestMethod]
    public async Task DeletingCategoryAndFeedMaintainsRelationships()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteCatalogStore(database.Path);
        await store.InitializeAsync();
        var category = new CatalogCategory(Guid.NewGuid().ToString("N"), "Gaming");
        var feed = new CatalogFeed(Guid.NewGuid().ToString("N"), "Example", "https://example.com/gaming.xml", null, category.Id);
        var collection = new CatalogCollection(Guid.NewGuid().ToString("N"), "Games");
        await store.AddCategoryAsync(category);
        await store.AddFeedAsync(feed);
        await store.AddCollectionAsync(collection);
        await store.AddFeedToCollectionAsync(collection.Id, feed.Id);

        await store.DeleteCategoryAsync(category.Id);
        var feedAfterCategoryDeletion = (await store.GetFeedsAsync()).Single();
        Assert.IsNull(feedAfterCategoryDeletion.CategoryId);

        await store.DeleteFeedAsync(feed.Id);

        Assert.AreEqual(0, (await store.GetFeedsAsync()).Count);
        Assert.AreEqual(0, (await store.GetCollectionFeedIdsAsync(collection.Id)).Count);
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"rss-catalog-{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var path in new[] { Path, $"{Path}-shm", $"{Path}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}