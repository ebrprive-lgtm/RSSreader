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
        await reader.SubscribeAsync(firstProfile.Id, feed.Id);
        await reader.SubscribeAsync(secondProfile.Id, feed.Id);
        var article = new FeedArticle("article-1", feed.Id, "item-1", "Headline", "https://example.com/story", DateTimeOffset.UtcNow, "Summary", "Content");
        await reader.SaveArticlesAsync(feed.Id, [article]);
        await reader.SetArticleReadAsync(firstProfile.Id, article.Id, true);
        await reader.SetArticleSavedAsync(firstProfile.Id, article.Id, true);

        var firstArticles = await reader.GetArticlesAsync(firstProfile.Id);
        var secondArticles = await reader.GetArticlesAsync(secondProfile.Id);

        Assert.AreEqual(1, firstArticles.Count);
        Assert.IsTrue(firstArticles[0].IsRead);
        Assert.IsTrue(firstArticles[0].IsSaved);
        Assert.AreEqual(1, secondArticles.Count);
        Assert.IsFalse(secondArticles[0].IsRead);
        Assert.IsFalse(secondArticles[0].IsSaved);
    }

    [TestMethod]
    public async Task FoldersAndFeedTagsAreProfileScopedAndDeleteClearsFolderAssignment()
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
        await reader.SubscribeAsync(profile.Id, feed.Id);
        await reader.SubscribeAsync(otherProfile.Id, feed.Id);
        await reader.AddFolderAsync(profile.Id, "Gaming");
        await reader.SetFeedFolderAsync(profile.Id, feed.Id, "Gaming");
        await reader.AddFeedTagAsync(profile.Id, feed.Id, "Reviews");

        var firstTags = await reader.GetFeedTagsAsync(profile.Id);
        var otherTags = await reader.GetFeedTagsAsync(otherProfile.Id);
        Assert.AreEqual("Reviews", firstTags.Single().Name);
        Assert.AreEqual(0, otherTags.Count);
        Assert.AreEqual("Gaming", (await reader.GetSubscriptionsAsync(profile.Id)).Single().FolderName);

        await reader.DeleteFolderAsync(profile.Id, "Gaming");

        Assert.IsNull((await reader.GetSubscriptionsAsync(profile.Id)).Single().FolderName);
        Assert.AreEqual(0, (await reader.GetFoldersAsync(profile.Id)).Count);
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