using Microsoft.Data.Sqlite;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

public sealed class SqliteReaderStore : IReaderStore
{
    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _connections;
    private readonly SqliteProfileLibraryStore _profileLibrary;
    private readonly SqliteFeedRefreshStore _feedRefresh;
    private readonly SqliteArticleStore _articles;

    public SqliteReaderStore(string databasePath)
    {
        _databasePath = databasePath;
        _connections = new SqliteConnectionFactory(databasePath);
        _profileLibrary = new SqliteProfileLibraryStore(_connections);
        _feedRefresh = new SqliteFeedRefreshStore(_connections);
        _articles = new SqliteArticleStore(_connections);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ProfileSubscriptions (
                ProfileId TEXT NOT NULL REFERENCES Profiles(Id) ON DELETE CASCADE,
                FeedId TEXT NOT NULL REFERENCES CatalogFeeds(Id) ON DELETE CASCADE,
                FolderName TEXT NULL,
                PRIMARY KEY (ProfileId, FeedId)
            );
            CREATE TABLE IF NOT EXISTS ProfileFolders (
                ProfileId TEXT NOT NULL REFERENCES Profiles(Id) ON DELETE CASCADE,
                Name TEXT NOT NULL COLLATE NOCASE,
                PRIMARY KEY (ProfileId, Name)
            );
            CREATE TABLE IF NOT EXISTS ProfileFeedTags (
                ProfileId TEXT NOT NULL,
                FeedId TEXT NOT NULL,
                Name TEXT NOT NULL COLLATE NOCASE,
                PRIMARY KEY (ProfileId, FeedId, Name),
                FOREIGN KEY (ProfileId, FeedId) REFERENCES ProfileSubscriptions(ProfileId, FeedId) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS Articles (
                Id TEXT NOT NULL PRIMARY KEY,
                FeedId TEXT NOT NULL REFERENCES CatalogFeeds(Id) ON DELETE CASCADE,
                ExternalId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Link TEXT NULL,
                PublishedAt TEXT NULL,
                Summary TEXT NULL,
                Content TEXT NULL,
                ImageUrl TEXT NULL,
                Author TEXT NULL,
                UNIQUE (FeedId, ExternalId)
            );
            CREATE TABLE IF NOT EXISTS ArticleCategories (
                ArticleId TEXT NOT NULL REFERENCES Articles(Id) ON DELETE CASCADE,
                Term TEXT NOT NULL COLLATE NOCASE,
                Scheme TEXT NOT NULL COLLATE NOCASE DEFAULT '',
                Label TEXT NULL,
                PRIMARY KEY (ArticleId, Term, Scheme)
            );
            CREATE TABLE IF NOT EXISTS ProfileArticleStates (
                ProfileId TEXT NOT NULL REFERENCES Profiles(Id) ON DELETE CASCADE,
                ArticleId TEXT NOT NULL REFERENCES Articles(Id) ON DELETE CASCADE,
                IsRead INTEGER NOT NULL DEFAULT 0,
                IsSaved INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (ProfileId, ArticleId)
            );
            CREATE TABLE IF NOT EXISTS ProfileFeedRefreshStates (
                ProfileId TEXT NOT NULL,
                FeedId TEXT NOT NULL,
                LastAttemptAt TEXT NOT NULL,
                LastSuccessfulAt TEXT NULL,
                LastFailure TEXT NULL,
                PRIMARY KEY (ProfileId, FeedId),
                FOREIGN KEY (ProfileId, FeedId) REFERENCES ProfileSubscriptions(ProfileId, FeedId) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_Articles_Feed_Published ON Articles(FeedId, PublishedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_ArticleCategories_Term_Scheme_Article
                ON ArticleCategories(Term COLLATE NOCASE, Scheme COLLATE NOCASE, ArticleId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info(Articles);";
        await using var columnsReader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
        var hasImageUrl = false;
        var hasAuthor = false;
        while (await columnsReader.ReadAsync(cancellationToken))
        {
            hasImageUrl |= columnsReader.GetString(1) == "ImageUrl";
            hasAuthor |= columnsReader.GetString(1) == "Author";
        }

        await columnsReader.DisposeAsync();
        if (!hasImageUrl)
        {
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText = "ALTER TABLE Articles ADD COLUMN ImageUrl TEXT NULL;";
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!hasAuthor)
        {
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText = "ALTER TABLE Articles ADD COLUMN Author TEXT NULL;";
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var folderMigrationCommand = connection.CreateCommand();
        folderMigrationCommand.CommandText = """
            INSERT OR IGNORE INTO ProfileFolders (ProfileId, Name)
            SELECT DISTINCT ProfileId, 'Unfiled' FROM ProfileSubscriptions WHERE FolderName IS NULL;
            UPDATE ProfileSubscriptions SET FolderName = 'Unfiled' WHERE FolderName IS NULL;
            """;
        await folderMigrationCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ProfileSubscription>> GetSubscriptionsAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        _profileLibrary.GetSubscriptionsAsync(profileId, cancellationToken);

    public Task<IReadOnlyList<ProfileFeedRefreshState>> GetFeedRefreshStatesAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        _feedRefresh.GetFeedRefreshStatesAsync(profileId, cancellationToken);

    public Task RecordFeedRefreshAttemptAsync(
        string profileId,
        string feedId,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken = default) =>
        _feedRefresh.RecordFeedRefreshAttemptAsync(profileId, feedId, attemptedAt, cancellationToken);

    public Task RecordFeedRefreshResultAsync(
        string profileId,
        string feedId,
        DateTimeOffset? successfulAt,
        string? failure,
        CancellationToken cancellationToken = default) =>
        _feedRefresh.RecordFeedRefreshResultAsync(profileId, feedId, successfulAt, failure, cancellationToken);

    public Task SubscribeAsync(
        string profileId,
        string feedId,
        string folderName,
        CancellationToken cancellationToken = default) =>
        _profileLibrary.SubscribeAsync(profileId, feedId, folderName, cancellationToken);

    public Task UnsubscribeAsync(
        string profileId,
        string feedId,
        CancellationToken cancellationToken = default) =>
        _profileLibrary.UnsubscribeAsync(profileId, feedId, cancellationToken);

    public Task<IReadOnlyList<string>> GetFoldersAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        _profileLibrary.GetFoldersAsync(profileId, cancellationToken);

    public Task AddFolderAsync(
        string profileId,
        string name,
        CancellationToken cancellationToken = default) =>
        _profileLibrary.AddFolderAsync(profileId, name, cancellationToken);

    public Task DeleteFolderAsync(
        string profileId,
        string name,
        CancellationToken cancellationToken = default) =>
        _profileLibrary.DeleteFolderAsync(profileId, name, cancellationToken);

    public Task SetFeedFolderAsync(
        string profileId,
        string feedId,
        string folderName,
        CancellationToken cancellationToken = default) =>
        _profileLibrary.SetFeedFolderAsync(profileId, feedId, folderName, cancellationToken);

    public Task<IReadOnlyList<ProfileFeedTag>> GetFeedTagsAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        _profileLibrary.GetFeedTagsAsync(profileId, cancellationToken);

    public Task AddFeedTagAsync(
        string profileId,
        string feedId,
        string tagName,
        CancellationToken cancellationToken = default) =>
        _profileLibrary.AddFeedTagAsync(profileId, feedId, tagName, cancellationToken);

    public Task RemoveFeedTagAsync(
        string profileId,
        string feedId,
        string tagName,
        CancellationToken cancellationToken = default) =>
        _profileLibrary.RemoveFeedTagAsync(profileId, feedId, tagName, cancellationToken);

    public Task<IReadOnlyList<ArticleForProfile>> GetArticlesAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        _articles.GetArticlesAsync(profileId, cancellationToken);

    public Task<int> SaveArticlesAsync(
        string feedId,
        IReadOnlyList<FeedArticle> articles,
        CancellationToken cancellationToken = default) =>
        _articles.SaveArticlesAsync(feedId, articles, cancellationToken);

    public Task SetArticleReadAsync(
        string profileId,
        string articleId,
        bool isRead,
        CancellationToken cancellationToken = default) =>
        _articles.SetArticleReadAsync(profileId, articleId, isRead, cancellationToken);

    public Task SetArticleSavedAsync(
        string profileId,
        string articleId,
        bool isSaved,
        CancellationToken cancellationToken = default) =>
        _articles.SetArticleSavedAsync(profileId, articleId, isSaved, cancellationToken);

    private Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        _connections.OpenConnectionAsync(cancellationToken);
}