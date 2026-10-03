using System.Globalization;
using Microsoft.Data.Sqlite;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

public sealed class SqliteReaderStore(string databasePath) : IReaderStore
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
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
                UNIQUE (FeedId, ExternalId)
            );
            CREATE TABLE IF NOT EXISTS ProfileArticleStates (
                ProfileId TEXT NOT NULL REFERENCES Profiles(Id) ON DELETE CASCADE,
                ArticleId TEXT NOT NULL REFERENCES Articles(Id) ON DELETE CASCADE,
                IsRead INTEGER NOT NULL DEFAULT 0,
                IsSaved INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (ProfileId, ArticleId)
            );
            CREATE INDEX IF NOT EXISTS IX_Articles_Feed_Published ON Articles(FeedId, PublishedAt DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info(Articles);";
        await using var columnsReader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
        var hasImageUrl = false;
        while (await columnsReader.ReadAsync(cancellationToken))
        {
            hasImageUrl |= columnsReader.GetString(1) == "ImageUrl";
        }

        await columnsReader.DisposeAsync();
        if (!hasImageUrl)
        {
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText = "ALTER TABLE Articles ADD COLUMN ImageUrl TEXT NULL;";
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

    public async Task<IReadOnlyList<ProfileSubscription>> GetSubscriptionsAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT subscription.ProfileId, feed.Id, feed.Name, feed.FeedUrl, subscription.FolderName
            FROM ProfileSubscriptions AS subscription
            INNER JOIN CatalogFeeds AS feed ON feed.Id = subscription.FeedId
            WHERE subscription.ProfileId = $profileId
            ORDER BY feed.Name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$profileId", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var subscriptions = new List<ProfileSubscription>();
        while (await reader.ReadAsync(cancellationToken))
        {
            subscriptions.Add(new ProfileSubscription(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return subscriptions;
    }

    public Task SubscribeAsync(
        string profileId,
        string feedId,
        string folderName,
        CancellationToken cancellationToken = default) => ExecuteAsync(
        "INSERT OR IGNORE INTO ProfileSubscriptions (ProfileId, FeedId, FolderName) VALUES ($profileId, $feedId, $folder);",
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId),
        ("$folder", folderName));

    public Task UnsubscribeAsync(string profileId, string feedId, CancellationToken cancellationToken = default) => ExecuteAsync(
        "DELETE FROM ProfileSubscriptions WHERE ProfileId = $profileId AND FeedId = $feedId;",
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId));

    public async Task<IReadOnlyList<string>> GetFoldersAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM ProfileFolders WHERE ProfileId = $profileId ORDER BY Name COLLATE NOCASE;";
        command.Parameters.AddWithValue("$profileId", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var folders = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            folders.Add(reader.GetString(0));
        }

        return folders;
    }

    public Task AddFolderAsync(string profileId, string name, CancellationToken cancellationToken = default) => ExecuteAsync(
        "INSERT INTO ProfileFolders (ProfileId, Name) VALUES ($profileId, $name);",
        cancellationToken,
        ("$profileId", profileId),
        ("$name", name.Trim()));

    public async Task DeleteFolderAsync(string profileId, string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT EXISTS(SELECT 1 FROM ProfileSubscriptions WHERE ProfileId = $profileId AND FolderName = $name COLLATE NOCASE);";
            check.Parameters.AddWithValue("$profileId", profileId);
            check.Parameters.AddWithValue("$name", name);
            if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) != 0)
            {
                throw new InvalidOperationException("Move or unfollow the sources in this folder before deleting it.");
            }
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM ProfileFolders WHERE ProfileId = $profileId AND Name = $name;";
            delete.Parameters.AddWithValue("$profileId", profileId);
            delete.Parameters.AddWithValue("$name", name);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task SetFeedFolderAsync(
        string profileId,
        string feedId,
        string folderName,
        CancellationToken cancellationToken = default) => ExecuteAsync(
        "UPDATE ProfileSubscriptions SET FolderName = $folder WHERE ProfileId = $profileId AND FeedId = $feedId;",
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId),
        ("$folder", folderName));

    public async Task<IReadOnlyList<ProfileFeedTag>> GetFeedTagsAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProfileId, FeedId, Name FROM ProfileFeedTags WHERE ProfileId = $profileId ORDER BY Name COLLATE NOCASE;";
        command.Parameters.AddWithValue("$profileId", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tags = new List<ProfileFeedTag>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tags.Add(new ProfileFeedTag(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return tags;
    }

    public Task AddFeedTagAsync(
        string profileId,
        string feedId,
        string tagName,
        CancellationToken cancellationToken = default) => ExecuteAsync(
        "INSERT OR IGNORE INTO ProfileFeedTags (ProfileId, FeedId, Name) VALUES ($profileId, $feedId, $name);",
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId),
        ("$name", tagName.Trim()));

    public Task RemoveFeedTagAsync(
        string profileId,
        string feedId,
        string tagName,
        CancellationToken cancellationToken = default) => ExecuteAsync(
        "DELETE FROM ProfileFeedTags WHERE ProfileId = $profileId AND FeedId = $feedId AND Name = $name;",
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId),
        ("$name", tagName));

    public async Task<IReadOnlyList<ArticleForProfile>> GetArticlesAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT article.Id, article.FeedId, article.ExternalId, article.Title, article.Link,
                     article.PublishedAt, article.Summary, article.Content, article.ImageUrl, feed.Name,
                   subscription.FolderName, state.IsRead, state.IsSaved
            FROM Articles AS article
            INNER JOIN ProfileSubscriptions AS subscription ON subscription.FeedId = article.FeedId
            INNER JOIN CatalogFeeds AS feed ON feed.Id = article.FeedId
            LEFT JOIN ProfileArticleStates AS state
                ON state.ArticleId = article.Id AND state.ProfileId = subscription.ProfileId
            WHERE subscription.ProfileId = $profileId
            ORDER BY article.PublishedAt DESC;
            """;
        command.Parameters.AddWithValue("$profileId", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var articles = new List<ArticleForProfile>();
        while (await reader.ReadAsync(cancellationToken))
        {
            DateTimeOffset? publishedAt = reader.IsDBNull(5)
                ? null
                : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var article = new FeedArticle(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                publishedAt,
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8));
            articles.Add(new ArticleForProfile(
                article,
                reader.GetString(9),
                reader.GetString(10),
                !reader.IsDBNull(11) && reader.GetInt64(11) == 1,
                !reader.IsDBNull(12) && reader.GetInt64(12) == 1));
        }

        return articles;
    }

    public async Task SaveArticlesAsync(
        string feedId,
        IReadOnlyList<FeedArticle> articles,
        CancellationToken cancellationToken = default)
    {
        if (articles.Any(article => article.FeedId != feedId))
        {
            throw new ArgumentException("All articles must belong to the specified feed.", nameof(articles));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        foreach (var article in articles)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Articles (Id, FeedId, ExternalId, Title, Link, PublishedAt, Summary, Content, ImageUrl)
                VALUES ($id, $feedId, $externalId, $title, $link, $publishedAt, $summary, $content, $imageUrl)
                ON CONFLICT(Id) DO UPDATE SET
                    Title = excluded.Title,
                    Link = excluded.Link,
                    PublishedAt = excluded.PublishedAt,
                    Summary = excluded.Summary,
                    Content = excluded.Content,
                    ImageUrl = excluded.ImageUrl;
                """;
            command.Parameters.AddWithValue("$id", article.Id);
            command.Parameters.AddWithValue("$feedId", feedId);
            command.Parameters.AddWithValue("$externalId", article.ExternalId);
            command.Parameters.AddWithValue("$title", article.Title);
            command.Parameters.AddWithValue("$link", (object?)article.Link ?? DBNull.Value);
            command.Parameters.AddWithValue("$publishedAt", article.PublishedAt?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$summary", (object?)article.Summary ?? DBNull.Value);
            command.Parameters.AddWithValue("$content", (object?)article.Content ?? DBNull.Value);
            command.Parameters.AddWithValue("$imageUrl", (object?)article.ImageUrl ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task SetArticleReadAsync(
        string profileId,
        string articleId,
        bool isRead,
        CancellationToken cancellationToken = default) => ExecuteAsync(
        """
        INSERT INTO ProfileArticleStates (ProfileId, ArticleId, IsRead, IsSaved)
        VALUES ($profileId, $articleId, $isRead, 0)
        ON CONFLICT(ProfileId, ArticleId) DO UPDATE SET IsRead = excluded.IsRead;
        """,
        cancellationToken,
        ("$profileId", profileId),
        ("$articleId", articleId),
        ("$isRead", isRead ? 1 : 0));

    public Task SetArticleSavedAsync(
        string profileId,
        string articleId,
        bool isSaved,
        CancellationToken cancellationToken = default) => ExecuteAsync(
        """
        INSERT INTO ProfileArticleStates (ProfileId, ArticleId, IsRead, IsSaved)
        VALUES ($profileId, $articleId, 0, $isSaved)
        ON CONFLICT(ProfileId, ArticleId) DO UPDATE SET IsSaved = excluded.IsSaved;
        """,
        cancellationToken,
        ("$profileId", profileId),
        ("$articleId", articleId),
        ("$isSaved", isSaved ? 1 : 0));

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private async Task ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}