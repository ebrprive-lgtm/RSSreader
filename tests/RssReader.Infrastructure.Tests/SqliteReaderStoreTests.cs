using Microsoft.Data.Sqlite;
using RssReader.Domain;
using RssReader.Infrastructure;

namespace RssReader.Infrastructure.Tests;

[TestClass]
public sealed class SqliteReaderStoreTests
{
    [TestMethod]
    public async Task InitializeMigratesSourceXmlColumnForExistingDatabases()
    {
        using var database = new TemporaryDatabase();
        await new SqliteProfileStore(database.Path).InitializeAsync();
        await new SqliteCatalogStore(database.Path).InitializeAsync();
        await using (var connection = new SqliteConnection($"Data Source={database.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Articles (
                    Id TEXT NOT NULL PRIMARY KEY,
                    FeedId TEXT NOT NULL,
                    ExternalId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Link TEXT NULL,
                    PublishedAt TEXT NULL,
                    Summary TEXT NULL,
                    Content TEXT NULL,
                    ImageUrl TEXT NULL,
                    Author TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var reader = new SqliteReaderStore(database.Path);
        await reader.InitializeAsync();

        await using var migratedConnection = new SqliteConnection($"Data Source={database.Path}");
        await migratedConnection.OpenAsync();
        await using var migratedCommand = migratedConnection.CreateCommand();
        migratedCommand.CommandText = "PRAGMA table_info(Articles);";
        await using var migratedColumns = await migratedCommand.ExecuteReaderAsync();
        var hasSourceXml = false;
        while (await migratedColumns.ReadAsync())
        {
            hasSourceXml |= migratedColumns.GetString(1) == "SourceXml";
        }

        Assert.IsTrue(hasSourceXml);
    }

    [TestMethod]
    public async Task SaveArticlesReportsOnlyNewRowsAndStillUpdatesExistingRows()
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
        await reader.AddFolderAsync(profile.Id, "News");
        await reader.SubscribeAsync(profile.Id, feed.Id, "News");
        var article = new FeedArticle("article-1", feed.Id, "item-1", "Headline", null, DateTimeOffset.UtcNow, null, null)
        {
            SourceXml = "<item><guid>item-1</guid></item>"
        };

        Assert.AreEqual(1, await reader.SaveArticlesAsync(feed.Id, [article]));
        Assert.AreEqual(0, await reader.SaveArticlesAsync(feed.Id, [article with { Title = "Updated headline" }]));
        var storedArticle = (await reader.GetArticlesAsync(profile.Id)).Single().Article;
        Assert.AreEqual("Updated headline", storedArticle.Title);
        Assert.AreEqual(article.SourceXml, storedArticle.SourceXml);
    }

    [TestMethod]
    public async Task SaveArticlesReplacesArticleCategoriesOnRefresh()
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
        await reader.AddFolderAsync(profile.Id, "News");
        await reader.SubscribeAsync(profile.Id, feed.Id, "News");
        var article = new FeedArticle("article-1", feed.Id, "item-1", "Headline", null, DateTimeOffset.UtcNow, null, null)
        {
            Categories =
            [
                new ArticleCategory("Press Releases", "https://example.com/topics"),
                new ArticleCategory("Government")
            ]
        };

        await reader.SaveArticlesAsync(feed.Id, [article]);
        var initialCategories = (await reader.GetArticlesAsync(profile.Id)).Single().Article.Categories;
        Assert.AreEqual(2, initialCategories.Count);
        Assert.AreEqual("https://example.com/topics", initialCategories[1].Scheme);

        await reader.SaveArticlesAsync(feed.Id, [article with
        {
            Title = "Updated headline",
            Categories = [new ArticleCategory("Announcements", Label: "Official announcements")]
        }]);
        var updatedArticle = (await reader.GetArticlesAsync(profile.Id)).Single().Article;

        Assert.AreEqual("Updated headline", updatedArticle.Title);
        Assert.AreEqual(1, updatedArticle.Categories.Count);
        Assert.AreEqual("Announcements", updatedArticle.Categories[0].Term);
        Assert.AreEqual("Official announcements", updatedArticle.Categories[0].Label);

        await reader.SaveArticlesAsync(feed.Id, [article with { Categories = [] }]);

        Assert.AreEqual(0, (await reader.GetArticlesAsync(profile.Id)).Single().Article.Categories.Count);
    }

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
        var publishedAt = DateTimeOffset.UtcNow;
        var article = new FeedArticle("article-1", feed.Id, "item-1", "Headline", "https://example.com/story", publishedAt, "<p>Summary &amp; <strong>details</strong></p>", "<p>Body<br/>second line</p>", "https://example.com/cover.jpg")
        {
            Author = "Example Author",
            SourceXml = "<item><guid>item-1</guid></item>",
            Categories = [new ArticleCategory("Press Releases")]
        };
        await reader.SaveArticlesAsync(feed.Id, [article]);
        await reader.SetArticleReadAsync(firstProfile.Id, article.Id, true);
        await reader.SetArticleSavedAsync(firstProfile.Id, article.Id, true);

        var reopenedReader = new SqliteReaderStore(database.Path);
        await reopenedReader.InitializeAsync();
        var firstArticles = await reopenedReader.GetArticlesAsync(firstProfile.Id);
        var secondArticles = await reopenedReader.GetArticlesAsync(secondProfile.Id);

        Assert.AreEqual(1, firstArticles.Count);
        Assert.AreEqual("Headline", firstArticles[0].Article.Title);
        Assert.AreEqual("https://example.com/story", firstArticles[0].Article.Link);
        Assert.AreEqual(publishedAt, firstArticles[0].Article.PublishedAt);
        Assert.AreEqual("Summary & details", firstArticles[0].Article.Summary);
        Assert.AreEqual("<p>Body<br/>second line</p>", firstArticles[0].Article.Content);
        Assert.AreEqual("https://example.com/cover.jpg", firstArticles[0].Article.ImageUrl);
        Assert.AreEqual("Example Author", firstArticles[0].Article.Author);
        Assert.AreEqual(article.SourceXml, firstArticles[0].Article.SourceXml);
        Assert.AreEqual("Press Releases", firstArticles[0].Article.Categories.Single().Term);
        Assert.IsTrue(firstArticles[0].IsRead);
        Assert.IsTrue(firstArticles[0].IsSaved);
        Assert.AreEqual(1, secondArticles.Count);
        Assert.IsFalse(secondArticles[0].IsRead);
        Assert.IsFalse(secondArticles[0].IsSaved);
    }

    [TestMethod]
    public async Task FeedRefreshStateIsProfileScopedAndPreservesLastSuccessAfterFailure()
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
        var feed = new CatalogFeed("feed-1", "Example", "https://example.com/feed.xml", null, null);
        await profiles.AddAsync(firstProfile);
        await profiles.AddAsync(secondProfile);
        await catalog.AddFeedAsync(feed);
        await reader.AddFolderAsync(firstProfile.Id, "News");
        await reader.AddFolderAsync(secondProfile.Id, "News");
        await reader.SubscribeAsync(firstProfile.Id, feed.Id, "News");
        await reader.SubscribeAsync(secondProfile.Id, feed.Id, "News");

        var firstAttempt = new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);
        var firstSuccess = firstAttempt.AddMinutes(2);
        var failedRetry = firstSuccess.AddHours(1);
        await reader.RecordFeedRefreshAttemptAsync(firstProfile.Id, feed.Id, firstAttempt);
        await reader.RecordFeedRefreshResultAsync(firstProfile.Id, feed.Id, firstSuccess, null);
        await reader.RecordFeedRefreshAttemptAsync(firstProfile.Id, feed.Id, failedRetry);
        await reader.RecordFeedRefreshResultAsync(firstProfile.Id, feed.Id, null, "Feed unavailable");

        var firstProfileState = (await reader.GetFeedRefreshStatesAsync(firstProfile.Id)).Single();
        Assert.AreEqual(failedRetry, firstProfileState.LastAttemptAt);
        Assert.AreEqual(firstSuccess, firstProfileState.LastSuccessfulAt);
        Assert.AreEqual("Feed unavailable", firstProfileState.LastFailure);
        Assert.AreEqual(0, (await reader.GetFeedRefreshStatesAsync(secondProfile.Id)).Count);

        await reader.ClearFeedRefreshFailureAsync(firstProfile.Id, feed.Id);
        firstProfileState = (await reader.GetFeedRefreshStatesAsync(firstProfile.Id)).Single();
        Assert.IsNull(firstProfileState.LastFailure);
        Assert.AreEqual(firstSuccess, firstProfileState.LastSuccessfulAt);

        await reader.RecordFeedRefreshResultAsync(firstProfile.Id, feed.Id, null, "Feed unavailable again");
        await reader.ClearFeedRefreshFailuresAsync(firstProfile.Id);
        firstProfileState = (await reader.GetFeedRefreshStatesAsync(firstProfile.Id)).Single();
        Assert.IsNull(firstProfileState.LastFailure);
        Assert.AreEqual(0, (await reader.GetFeedRefreshStatesAsync(secondProfile.Id)).Count);
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
        var feed = new CatalogFeed(
            Guid.NewGuid().ToString("N"),
            "Example",
            "https://example.com/feed.xml",
            null,
            null,
            WebsiteUrl: "https://example.com");
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
        Assert.AreEqual("https://example.com", (await reader.GetSubscriptionsAsync(profile.Id)).Single().WebsiteUrl);

        await reader.DeleteFolderAsync(profile.Id, "Gaming");

        Assert.AreEqual(0, (await reader.GetSubscriptionsAsync(profile.Id)).Count);
        Assert.AreEqual(feed.Id, (await reader.GetSubscriptionsAsync(otherProfile.Id)).Single().FeedId);
        Assert.AreEqual(0, (await reader.GetFeedTagsAsync(profile.Id)).Count);
        Assert.AreEqual("Personal", (await reader.GetFoldersAsync(profile.Id)).Single());
    }

    [TestMethod]
    public async Task InitializeAddsImageUrlAndAuthorColumnsToAnExistingArticleTable()
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
            command.CommandText = "ALTER TABLE Articles DROP COLUMN ImageUrl; ALTER TABLE Articles DROP COLUMN Author;";
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
            "https://example.com/cover.jpg",
            "Example Author");

        await reader.SaveArticlesAsync(feed.Id, [article]);

        Assert.AreEqual("https://example.com/cover.jpg", (await reader.GetArticlesAsync(profile.Id)).Single().Article.ImageUrl);
        Assert.AreEqual("Example Author", (await reader.GetArticlesAsync(profile.Id)).Single().Article.Author);
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