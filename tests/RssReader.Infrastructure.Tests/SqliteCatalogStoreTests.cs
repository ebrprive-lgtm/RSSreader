using RssReader.Domain;
using RssReader.Infrastructure;
using Microsoft.Data.Sqlite;

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
        var lastCheckedAt = new DateTimeOffset(2026, 10, 3, 12, 30, 0, TimeSpan.Zero);
        var feed = new CatalogFeed(
            Guid.NewGuid().ToString("N"),
            "Example",
            "https://example.com/feed.xml",
            null,
            category.Id,
            "https://example.com",
            lastCheckedAt,
            true);
        var collection = new CatalogCollection(Guid.NewGuid().ToString("N"), "Daily reads");
        await store.AddCategoryAsync(category);
        await store.AddFeedAsync(feed);
        await store.UpdateFeedAsync(feed with { Description = "Updated description" });
        await store.AddCollectionAsync(collection);
        await store.AddFeedToCollectionAsync(collection.Id, feed.Id);

        var reloadedStore = new SqliteCatalogStore(database.Path);
        var loadedFeed = (await reloadedStore.GetFeedsAsync()).Single();
        var loadedCollection = (await reloadedStore.GetCollectionsAsync()).Single();
        var members = await reloadedStore.GetCollectionFeedIdsAsync(loadedCollection.Id);

        Assert.AreEqual(category.Id, loadedFeed.CategoryId);
        Assert.AreEqual(feed.WebsiteUrl, loadedFeed.WebsiteUrl);
        Assert.AreEqual(lastCheckedAt, loadedFeed.LastHealthCheckedAt);
        Assert.AreEqual(true, loadedFeed.LastHealthCheckSucceeded);
        Assert.AreEqual("Updated description", loadedFeed.Description);
        Assert.AreEqual(feed.Id, members.Single());
    }

    [TestMethod]
    public async Task InitializeMigratesExistingCatalogFeedsTable()
    {
        using var database = new TemporaryDatabase();
        await using (var connection = new SqliteConnection($"Data Source={database.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE CatalogFeeds (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    FeedUrl TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    Description TEXT NULL,
                    CategoryId TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteCatalogStore(database.Path);
        await store.InitializeAsync();
        var feed = new CatalogFeed(
            "legacy-feed",
            "Legacy feed",
            "https://example.com/feed.xml",
            null,
            null,
            "https://example.com",
            new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero),
            false);
        await store.AddFeedAsync(feed);

        var migratedFeed = (await store.GetFeedsAsync()).Single();
        Assert.AreEqual(feed.WebsiteUrl, migratedFeed.WebsiteUrl);
        Assert.AreEqual(feed.LastHealthCheckedAt, migratedFeed.LastHealthCheckedAt);
        Assert.AreEqual(feed.LastHealthCheckSucceeded, migratedFeed.LastHealthCheckSucceeded);
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