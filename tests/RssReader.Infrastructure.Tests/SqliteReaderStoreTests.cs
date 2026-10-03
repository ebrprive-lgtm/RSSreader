using Microsoft.Data.Sqlite;
using RssReader.Domain;
using RssReader.Infrastructure;

namespace RssReader.Infrastructure.Tests;

[TestClass]
public sealed class SqliteReaderStoreTests
{
    [TestMethod]
    public async Task SubscriptionsArticlesAndReadingStateAreIsolatedByProfile()
    {
        using var database = new TemporaryDatabase();
        var profiles = new SqliteProfileStore(database.Path);
        var catalog = new SqliteCatalogStore(database.Path);
        var reader = new SqliteReaderStore(database.Path);
        await profiles.InitializeAsync();
        await catalog.InitializeAsync();
        await reader.InitializeAsync();

        var firstProfile = Profile.CreateRegular("First Reader");
        var secondProfile = Profile.CreateRegular("Second Reader");
        await profiles.AddAsync(firstProfile);
        await profiles.AddAsync(secondProfile);
        var feed = new CatalogFeed(Guid.NewGuid().ToString("N"), "Example", "https://example.com/feed.xml", null, null);
        await catalog.AddFeedAsync(feed);
        await reader.AddFolderAsync(firstProfile.Id, "News");
        await reader.AddFolderAsync(secondProfile.Id, "News");
        await reader.SubscribeAsync(firstProfile.Id, feed.Id, "News");
        await reader.SubscribeAsync(secondProfile.Id, feed.Id, "News");
        var article = new FeedArticle("article-1", feed.Id, "item-1", "Headline", "https://example.com/story", DateTimeOffset.UtcNow, "Summary", "Content", "https://example.com/cover.jpg");
        await reader.SaveArticlesAsync(feed.Id, [article]);
        await reader.SetArticleReadAsync(firstProfile.Id, article.Id, true);
        await reader.SetArticleSavedAsync(firstProfile.Id, article.Id, true);

        var firstArticles = await reader.GetArticlesAsync(firstProfile.Id);
        var secondArticles = await reader.GetArticlesAsync(secondProfile.Id);

        Assert.AreEqual(1, firstArticles.Count);
        Assert.AreEqual("https://example.com/cover.jpg", firstArticles[0].Article.ImageUrl);
        Assert.IsTrue(firstArticles[0].IsRead);
        Assert.IsTrue(firstArticles[0].IsSaved);
        Assert.AreEqual(1, secondArticles.Count);
        Assert.IsFalse(secondArticles[0].IsRead);
        Assert.IsFalse(secondArticles[0].IsSaved);
    }

    [TestMethod]
    public async Task FoldersAndFeedTagsAreProfileScopedAndAssignedFoldersCannotBeDeleted()
    {
        using var database = new TemporaryDatabase();
        var profiles = new SqliteProfileStore(database.Path);
        var catalog = new SqliteCatalogStore(database.Path);
        var reader = new SqliteReaderStore(database.Path);
        await profiles.InitializeAsync();
        await catalog.InitializeAsync();
        await reader.InitializeAsync();
        var profile = Profile.CreateRegular("Reader");
        var otherProfile = Profile.CreateRegular("Other Reader");
        await profiles.AddAsync(profile);
        await profiles.AddAsync(otherProfile);
        var feed = new CatalogFeed(Guid.NewGuid().ToString("N"), "Example", "https://example.com/feed.xml", null, null);
        await catalog.AddFeedAsync(feed);
        await reader.AddFolderAsync(profile.Id, "Gaming");
        await reader.AddFolderAsync(profile.Id, "Personal");
        await reader.AddFolderAsync(otherProfile.Id, "Gaming");
        await reader.SubscribeAsync(profile.Id, feed.Id, "Gaming");
        await reader.SubscribeAsync(otherProfile.Id, feed.Id, "Gaming");
        await reader.SetFeedFolderAsync(profile.Id, feed.Id, "Gaming");
        await reader.AddFeedTagAsync(profile.Id, feed.Id, "Reviews");

        var firstTags = await reader.GetFeedTagsAsync(profile.Id);
        var otherTags = await reader.GetFeedTagsAsync(otherProfile.Id);
        Assert.AreEqual("Reviews", firstTags.Single().Name);
        Assert.AreEqual(0, otherTags.Count);
        Assert.AreEqual("Gaming", (await reader.GetSubscriptionsAsync(profile.Id)).Single().FolderName);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => reader.DeleteFolderAsync(profile.Id, "Gaming"));
        await reader.SetFeedFolderAsync(profile.Id, feed.Id, "Personal");
        await reader.DeleteFolderAsync(profile.Id, "Gaming");

        Assert.AreEqual("Personal", (await reader.GetSubscriptionsAsync(profile.Id)).Single().FolderName);
        Assert.AreEqual("Personal", (await reader.GetFoldersAsync(profile.Id)).Single());
    }

    [TestMethod]
    public async Task InitializeAddsImageUrlColumnToAnExistingArticleTable()
    {
        using var database = new TemporaryDatabase();
        var profiles = new SqliteProfileStore(database.Path);
        var catalog = new SqliteCatalogStore(database.Path);
        var reader = new SqliteReaderStore(database.Path);
        await profiles.InitializeAsync();
        await catalog.InitializeAsync();
        await reader.InitializeAsync();

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database.Path
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE Articles DROP COLUMN ImageUrl;";
            await command.ExecuteNonQueryAsync();
        }

        await reader.InitializeAsync();
        var profile = Profile.CreateRegular("Reader");
        var feed = new CatalogFeed(Guid.NewGuid().ToString("N"), "Example", "https://example.com/feed.xml", null, null);
        await profiles.AddAsync(profile);
        await catalog.AddFeedAsync(feed);
        await reader.AddFolderAsync(profile.Id, "News");
        await reader.SubscribeAsync(profile.Id, feed.Id, "News");
        var article = new FeedArticle(
            "article-1",
            feed.Id,
            "item-1",
            "Headline",
            "https://example.com/story",
            DateTimeOffset.UtcNow,
            null,
            null,
            "https://example.com/cover.jpg");

        await reader.SaveArticlesAsync(feed.Id, [article]);

        Assert.AreEqual("https://example.com/cover.jpg", (await reader.GetArticlesAsync(profile.Id)).Single().Article.ImageUrl);
    }

    [TestMethod]
    public async Task InitializeMovesLegacyFolderlessSubscriptionsIntoUnfiled()
    {
        using var database = new TemporaryDatabase();
        var profiles = new SqliteProfileStore(database.Path);
        var catalog = new SqliteCatalogStore(database.Path);
        var reader = new SqliteReaderStore(database.Path);
        await profiles.InitializeAsync();
        await catalog.InitializeAsync();
        await reader.InitializeAsync();
        var profile = Profile.CreateRegular("Reader");
        var feed = new CatalogFeed("feed-1", "Example", "https://example.com/feed.xml", null, null);
        await profiles.AddAsync(profile);
        await catalog.AddFeedAsync(feed);

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database.Path
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO ProfileSubscriptions (ProfileId, FeedId, FolderName) VALUES ($profileId, $feedId, NULL);";
            command.Parameters.AddWithValue("$profileId", profile.Id);
            command.Parameters.AddWithValue("$feedId", feed.Id);
            await command.ExecuteNonQueryAsync();
        }

        await reader.InitializeAsync();

        Assert.AreEqual("Unfiled", (await reader.GetSubscriptionsAsync(profile.Id)).Single().FolderName);
        Assert.AreEqual("Unfiled", (await reader.GetFoldersAsync(profile.Id)).Single());
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"rss-reader-store-{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
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