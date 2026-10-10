using RssReader.Application;
using RssReader.Domain;
using RssReader.Infrastructure;
using Microsoft.Data.Sqlite;

namespace RssReader.Infrastructure.Tests;

[TestClass]
public sealed class SqliteProfileStoreTests
{
    [TestMethod]
    public async Task Initialize_IsIdempotentAndSeedsCatalogMaster()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteProfileStore(database.Path);

        await store.InitializeAsync();
        await store.InitializeAsync();
        var profiles = await store.GetAllAsync();

        Assert.AreEqual(1, profiles.Count);
        Assert.AreEqual(ProfileNameValidator.CatalogMasterName, profiles[0].Name);
        Assert.IsTrue(profiles[0].IsCatalogMaster);
        Assert.IsNull(profiles[0].PasswordHash);
    }

    [TestMethod]
    public async Task AddProfile_PersistsAndFindsProfileWithoutCaseSensitiveNameMatching()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteProfileStore(database.Path);
        await store.InitializeAsync();
        var profile = Profile.CreateRegular("Reader", "hashed-value", "reader@example.com");

        await store.AddAsync(profile);

        Assert.IsTrue(await store.NameExistsAsync("reader"));
        var loaded = await store.GetByIdAsync(profile.Id);
        Assert.AreEqual(profile, loaded);
    }

    [TestMethod]
    public async Task ProfilePreferences_ArePersistedPerProfile()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteProfileStore(database.Path);
        await store.InitializeAsync();
        var firstProfile = Profile.CreateRegular("First");
        var secondProfile = Profile.CreateRegular("Second");
        await store.AddAsync(firstProfile);
        await store.AddAsync(secondProfile);
        var preferences = new ProfilePreferences(
            ProfileStartPage.FirstFolder,
            ProfileArticlePresentation.Magazine,
            ProfileArticleSort.Newest,
            true,
            25,
            false,
            60,
            true,
            false,
            true,
            ReadingLayout: ProfileReadingLayout.SplitPane,
            ArticleListDensity: ProfileArticleListDensity.Compact,
            ReaderTheme: ProfileReaderTheme.Warm,
            ReaderTextSize: ProfileReaderTextSize.Large,
            ReaderLineSpacing: ProfileReaderLineSpacing.Relaxed,
            ReaderFontFamily: ProfileReaderFontFamily.Monospace,
            SplitPaneListRatio: 0.63);

        await store.SavePreferencesAsync(firstProfile.Id, preferences);
        var reopenedStore = new SqliteProfileStore(database.Path);
        await reopenedStore.InitializeAsync();

        Assert.AreEqual(preferences, await reopenedStore.GetPreferencesAsync(firstProfile.Id));
        Assert.AreEqual(new ProfilePreferences(), await reopenedStore.GetPreferencesAsync(secondProfile.Id));
    }

    [TestMethod]
    public async Task Initialize_AddsNewRefreshPreferenceColumnsToExistingDatabase()
    {
        using var database = new TemporaryDatabase();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database.Path }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Profiles (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    IsCatalogMaster INTEGER NOT NULL CHECK (IsCatalogMaster IN (0, 1)),
                    PasswordHash TEXT NULL,
                    RecoveryEmail TEXT NULL
                );
                CREATE TABLE ProfilePreferences (
                    ProfileId TEXT NOT NULL PRIMARY KEY REFERENCES Profiles(Id) ON DELETE CASCADE,
                    StartPage INTEGER NOT NULL CHECK (StartPage BETWEEN 0 AND 2),
                    Presentation INTEGER NOT NULL CHECK (Presentation BETWEEN 0 AND 2),
                    ArticleSort INTEGER NOT NULL CHECK (ArticleSort BETWEEN 0 AND 1),
                    HideReadArticles INTEGER NOT NULL CHECK (HideReadArticles IN (0, 1)),
                    FolderArticleLimitPerFeed INTEGER NOT NULL CHECK (FolderArticleLimitPerFeed BETWEEN 1 AND 100)
                );
                INSERT INTO Profiles (Id, Name, IsCatalogMaster) VALUES ('profile-1', 'Reader', 0);
                INSERT INTO ProfilePreferences (ProfileId, StartPage, Presentation, ArticleSort, HideReadArticles, FolderArticleLimitPerFeed)
                VALUES ('profile-1', 0, 0, 0, 0, 10);
                """;
            await command.ExecuteNonQueryAsync();
        }
        var store = new SqliteProfileStore(database.Path);

        await store.InitializeAsync();

        Assert.AreEqual(new ProfilePreferences(), await store.GetPreferencesAsync("profile-1"));
    }

    [TestMethod]
    public async Task Initialize_AddsMissingPreferencesToExistingProfilePreferences()
    {
        using var database = new TemporaryDatabase();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database.Path }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Profiles (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    IsCatalogMaster INTEGER NOT NULL CHECK (IsCatalogMaster IN (0, 1)),
                    PasswordHash TEXT NULL,
                    RecoveryEmail TEXT NULL
                );
                CREATE TABLE ProfilePreferences (
                    ProfileId TEXT NOT NULL PRIMARY KEY REFERENCES Profiles(Id) ON DELETE CASCADE,
                    StartPage INTEGER NOT NULL CHECK (StartPage BETWEEN 0 AND 2),
                    Presentation INTEGER NOT NULL CHECK (Presentation BETWEEN 0 AND 2),
                    ArticleSort INTEGER NOT NULL CHECK (ArticleSort BETWEEN 0 AND 1),
                    HideReadArticles INTEGER NOT NULL CHECK (HideReadArticles IN (0, 1)),
                    FolderArticleLimitPerFeed INTEGER NOT NULL CHECK (FolderArticleLimitPerFeed BETWEEN 1 AND 100),
                    RefreshFeedsWhenOpened INTEGER NOT NULL DEFAULT 1 CHECK (RefreshFeedsWhenOpened IN (0, 1)),
                    AutoRefreshIntervalMinutes INTEGER NOT NULL DEFAULT 0 CHECK (AutoRefreshIntervalMinutes IN (0, 15, 30, 60, 240))
                );
                INSERT INTO Profiles (Id, Name, IsCatalogMaster) VALUES ('profile-1', 'Reader', 0);
                INSERT INTO ProfilePreferences (ProfileId, StartPage, Presentation, ArticleSort, HideReadArticles, FolderArticleLimitPerFeed)
                VALUES ('profile-1', 0, 0, 0, 0, 10);
                """;
            await command.ExecuteNonQueryAsync();
        }
        var store = new SqliteProfileStore(database.Path);

        await store.InitializeAsync();

        Assert.IsFalse((await store.GetPreferencesAsync("profile-1")).ShowRawFeedButton);
        Assert.IsTrue((await store.GetPreferencesAsync("profile-1")).LimitArticleWidth);
        Assert.IsFalse((await store.GetPreferencesAsync("profile-1")).HideFollowedCatalogFeeds);
        Assert.AreEqual(new ProfilePreferences(), await store.GetPreferencesAsync("profile-1"));
    }

    [TestMethod]
    public async Task AddProfile_RejectsDuplicateNameIgnoringCase()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteProfileStore(database.Path);
        await store.InitializeAsync();
        await store.AddAsync(Profile.CreateRegular("Reader"));

        await Assert.ThrowsExceptionAsync<DuplicateProfileNameException>(
            () => store.AddAsync(Profile.CreateRegular("reader")));
    }

    [TestMethod]
    public async Task DeleteProfileCascadesProfileDataAndKeepsSharedArticles()
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
        await reader.SetFeedFolderAsync(profile.Id, feed.Id, "News");
        await reader.AddFeedTagAsync(profile.Id, feed.Id, "Saved topic");
        var article = new FeedArticle("article-1", feed.Id, "item-1", "Headline", null, null, null, null);
        await reader.SaveArticlesAsync(feed.Id, [article]);
        await reader.SetArticleReadAsync(profile.Id, article.Id, true);
        await reader.SetArticleSavedAsync(profile.Id, article.Id, true);

        await profiles.DeleteAsync(profile.Id);

        Assert.IsNull(await profiles.GetByIdAsync(profile.Id));
        Assert.AreEqual(0, (await reader.GetSubscriptionsAsync(profile.Id)).Count);
        Assert.AreEqual(0, (await reader.GetFoldersAsync(profile.Id)).Count);
        Assert.AreEqual(0, (await reader.GetFeedTagsAsync(profile.Id)).Count);

        await profiles.AddAsync(profile);
        await reader.AddFolderAsync(profile.Id, "News");
        await reader.SubscribeAsync(profile.Id, feed.Id, "News");
        var reopenedArticle = (await reader.GetArticlesAsync(profile.Id)).Single();
        Assert.AreEqual(article.Id, reopenedArticle.Article.Id);
        Assert.IsFalse(reopenedArticle.IsRead);
        Assert.IsFalse(reopenedArticle.IsSaved);
    }

    [TestMethod]
    public void Pbkdf2PasswordHasher_VerifiesPasswordAndRejectsInvalidHashes()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("secret");

        Assert.AreNotEqual("secret", hash);
        Assert.IsTrue(hasher.Verify(hash, "secret"));
        Assert.IsFalse(hasher.Verify(hash, "wrong"));
        Assert.IsFalse(hasher.Verify("not-a-hash", "secret"));
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"rss-reader-{Guid.NewGuid():N}.db");

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