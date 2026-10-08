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
        await store.UpdateCollectionAsync(collection with { Name = "PC Gaming" });

        var reloadedStore = new SqliteCatalogStore(database.Path);
        var loadedFeed = (await reloadedStore.GetFeedsAsync()).Single();
        var loadedCollection = (await reloadedStore.GetCollectionsAsync()).Single();
        var members = await reloadedStore.GetCollectionFeedIdsAsync(loadedCollection.Id);
        var membersByCollection = await reloadedStore.GetCollectionFeedIdsByCollectionAsync();

        Assert.AreEqual(category.Id, loadedFeed.CategoryId);
        Assert.AreEqual(feed.WebsiteUrl, loadedFeed.WebsiteUrl);
        Assert.AreEqual(lastCheckedAt, loadedFeed.LastHealthCheckedAt);
        Assert.AreEqual(true, loadedFeed.LastHealthCheckSucceeded);
        Assert.AreEqual("Updated description", loadedFeed.Description);
        Assert.AreEqual("PC Gaming", loadedCollection.Name);
        Assert.AreEqual(feed.Id, members.Single());
        Assert.AreEqual(feed.Id, membersByCollection[loadedCollection.Id].Single());
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
    public async Task DeletingCategoryAndFeedMaintainsRelationshipsAcrossProfiles()
    {
        using var database = new TemporaryDatabase();
        var profileStore = new SqliteProfileStore(database.Path);
        var store = new SqliteCatalogStore(database.Path);
        var readerStore = new SqliteReaderStore(database.Path);
        await profileStore.InitializeAsync();
        await store.InitializeAsync();
        await readerStore.InitializeAsync();
        var firstProfile = Profile.CreateRegular("First reader");
        var secondProfile = Profile.CreateRegular("Second reader");
        await profileStore.AddAsync(firstProfile);
        await profileStore.AddAsync(secondProfile);
        var category = new CatalogCategory(Guid.NewGuid().ToString("N"), "Gaming");
        var feed = new CatalogFeed(Guid.NewGuid().ToString("N"), "Example", "https://example.com/gaming.xml", null, category.Id);
        var collection = new CatalogCollection(Guid.NewGuid().ToString("N"), "Games");
        await store.AddCategoryAsync(category);
        await store.AddFeedAsync(feed);
        await store.AddCollectionAsync(collection);
        await store.AddFeedToCollectionAsync(collection.Id, feed.Id);
        await readerStore.AddFolderAsync(firstProfile.Id, "Gaming");
        await readerStore.AddFolderAsync(secondProfile.Id, "Gaming");
        await readerStore.SubscribeAsync(firstProfile.Id, feed.Id, "Gaming");
        await readerStore.SubscribeAsync(secondProfile.Id, feed.Id, "Gaming");
        var cachedArticle = new FeedArticle(
            "article-1",
            feed.Id,
            "external-1",
            "Cached headline",
            null,
            DateTimeOffset.UtcNow,
            "Summary",
            "<p>Cached content</p>");
        await readerStore.SaveArticlesAsync(feed.Id, [cachedArticle]);
        await readerStore.SetArticleReadAsync(firstProfile.Id, cachedArticle.Id, true);
        await readerStore.SetArticleSavedAsync(firstProfile.Id, cachedArticle.Id, true);
        await readerStore.AddFeedTagAsync(firstProfile.Id, feed.Id, "Reviews");
        await readerStore.RecordFeedRefreshAttemptAsync(firstProfile.Id, feed.Id, DateTimeOffset.UtcNow);
        await readerStore.RecordFeedRefreshResultAsync(firstProfile.Id, feed.Id, DateTimeOffset.UtcNow, null);

        await store.DeleteCategoryAsync(category.Id);
        var feedAfterCategoryDeletion = (await store.GetFeedsAsync()).Single();
        Assert.IsNull(feedAfterCategoryDeletion.CategoryId);

        await store.DeleteFeedAsync(feed.Id);

        Assert.AreEqual(0, (await store.GetFeedsAsync()).Count);
        Assert.AreEqual(0, (await store.GetCollectionFeedIdsAsync(collection.Id)).Count);
        Assert.AreEqual(0, (await readerStore.GetSubscriptionsAsync(firstProfile.Id)).Count);
        Assert.AreEqual(0, (await readerStore.GetSubscriptionsAsync(secondProfile.Id)).Count);
        Assert.AreEqual(0, (await readerStore.GetArticlesAsync(firstProfile.Id)).Count);
        Assert.AreEqual(0, (await readerStore.GetArticlesAsync(secondProfile.Id)).Count);
        Assert.AreEqual(0, (await readerStore.GetFeedTagsAsync(firstProfile.Id)).Count);
        Assert.AreEqual(0, (await readerStore.GetFeedRefreshStatesAsync(firstProfile.Id)).Count);
    }

    [TestMethod]
    public async Task RenamingCategoryPreservesItsIdAndFeedAssignments()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteCatalogStore(database.Path);
        await store.InitializeAsync();
        var category = new CatalogCategory(Guid.NewGuid().ToString("N"), "Comics");
        var feed = new CatalogFeed(Guid.NewGuid().ToString("N"), "Example", "https://example.com/feed.xml", null, category.Id);
        await store.AddCategoryAsync(category);
        await store.AddFeedAsync(feed);

        await store.UpdateCategoryAsync(category with { Name = "Comics and cartoons" });

        var reloadedStore = new SqliteCatalogStore(database.Path);
        Assert.AreEqual(category.Id, (await reloadedStore.GetCategoriesAsync()).Single().Id);
        Assert.AreEqual("Comics and cartoons", (await reloadedStore.GetCategoriesAsync()).Single().Name);
        Assert.AreEqual(category.Id, (await reloadedStore.GetFeedsAsync()).Single().CategoryId);
    }

    [TestMethod]
    public async Task MergingCategoriesMovesFeedsAndDeletesSourceCategory()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteCatalogStore(database.Path);
        await store.InitializeAsync();
        var sourceCategory = new CatalogCategory(Guid.NewGuid().ToString("N"), "Science");
        var targetCategory = new CatalogCategory(Guid.NewGuid().ToString("N"), "Research");
        var sourceFeed = new CatalogFeed(Guid.NewGuid().ToString("N"), "Science feed", "https://example.com/science.xml", null, sourceCategory.Id);
        var targetFeed = new CatalogFeed(Guid.NewGuid().ToString("N"), "Research feed", "https://example.com/research.xml", null, targetCategory.Id);
        await store.AddCategoryAsync(sourceCategory);
        await store.AddCategoryAsync(targetCategory);
        await store.AddFeedAsync(sourceFeed);
        await store.AddFeedAsync(targetFeed);

        await store.MergeCategoriesAsync(sourceCategory.Id, targetCategory.Id);

        var categories = await store.GetCategoriesAsync();
        var feeds = await store.GetFeedsAsync();
        Assert.AreEqual(1, categories.Count);
        Assert.AreEqual(targetCategory.Id, categories.Single().Id);
        Assert.IsTrue(feeds.All(feed => feed.CategoryId == targetCategory.Id));
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