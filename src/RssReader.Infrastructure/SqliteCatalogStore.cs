using Microsoft.Data.Sqlite;
using System.Globalization;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

public sealed class SqliteCatalogStore(string databasePath) : ICatalogStore
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
            CREATE TABLE IF NOT EXISTS CatalogCategories (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE
            );
            CREATE TABLE IF NOT EXISTS CatalogCollections (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE
            );
            CREATE TABLE IF NOT EXISTS CatalogFeeds (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                FeedUrl TEXT NOT NULL COLLATE NOCASE UNIQUE,
                Description TEXT NULL,
                CategoryId TEXT NULL REFERENCES CatalogCategories(Id) ON DELETE SET NULL,
                WebsiteUrl TEXT NULL,
                LastHealthCheckedAt TEXT NULL,
                LastHealthCheckSucceeded INTEGER NULL,
                IsSharedCatalog INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS CatalogCollectionFeeds (
                CollectionId TEXT NOT NULL REFERENCES CatalogCollections(Id) ON DELETE CASCADE,
                FeedId TEXT NOT NULL REFERENCES CatalogFeeds(Id) ON DELETE CASCADE,
                PRIMARY KEY (CollectionId, FeedId)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureCatalogFeedColumnAsync(connection, "WebsiteUrl", "TEXT NULL", cancellationToken);
        await EnsureCatalogFeedColumnAsync(connection, "LastHealthCheckedAt", "TEXT NULL", cancellationToken);
        await EnsureCatalogFeedColumnAsync(connection, "LastHealthCheckSucceeded", "INTEGER NULL", cancellationToken);
        await EnsureCatalogFeedColumnAsync(connection, "IsSharedCatalog", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await using var profileFeedsCommand = connection.CreateCommand();
        profileFeedsCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS ProfileFeedOwners (
                ProfileId TEXT NOT NULL REFERENCES Profiles(Id) ON DELETE CASCADE,
                FeedId TEXT NOT NULL REFERENCES CatalogFeeds(Id) ON DELETE CASCADE,
                PRIMARY KEY (ProfileId, FeedId)
            );
            CREATE TRIGGER IF NOT EXISTS TR_ProfileFeedOwners_Cleanup
            AFTER DELETE ON ProfileFeedOwners
            BEGIN
                DELETE FROM CatalogFeeds
                WHERE Id = OLD.FeedId
                  AND IsSharedCatalog = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM ProfileFeedOwners WHERE FeedId = OLD.FeedId
                  );
            END;
            """;
        await profileFeedsCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureCatalogFeedColumnAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        var hasWebsiteUrl = false;
        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandText = "PRAGMA table_info(CatalogFeeds);";
            await using var columnsReader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
            while (await columnsReader.ReadAsync(cancellationToken))
            {
                hasWebsiteUrl |= string.Equals(columnsReader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!hasWebsiteUrl)
        {
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText = $"ALTER TABLE CatalogFeeds ADD COLUMN {columnName} {columnDefinition};";
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CatalogFeed>> GetFeedsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, FeedUrl, Description, CategoryId, WebsiteUrl, LastHealthCheckedAt, LastHealthCheckSucceeded
            FROM CatalogFeeds
            WHERE IsSharedCatalog = 1
            ORDER BY Name COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var feeds = new List<CatalogFeed>();
        while (await reader.ReadAsync(cancellationToken))
        {
            feeds.Add(ReadCatalogFeed(reader));
        }

        return feeds;
    }

    public async Task<IReadOnlyList<CatalogFeed>> GetFeedsForProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT feed.Id, feed.Name, feed.FeedUrl, feed.Description, feed.CategoryId, feed.WebsiteUrl,
                   feed.LastHealthCheckedAt, feed.LastHealthCheckSucceeded
            FROM CatalogFeeds AS feed
            WHERE feed.IsSharedCatalog = 1
               OR EXISTS (
                    SELECT 1 FROM ProfileFeedOwners AS owner
                    WHERE owner.FeedId = feed.Id AND owner.ProfileId = $profileId
               )
            ORDER BY feed.Name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$profileId", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var feeds = new List<CatalogFeed>();
        while (await reader.ReadAsync(cancellationToken))
        {
            feeds.Add(ReadCatalogFeed(reader));
        }

        return feeds;
    }

    public async Task<CatalogFeed> AddProfileFeedAsync(
        string profileId,
        CatalogFeed feed,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        string feedId;
        var isSharedCatalog = false;
        var needsInsert = true;
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = """
                SELECT Id, IsSharedCatalog
                FROM CatalogFeeds
                WHERE FeedUrl = $feedUrl COLLATE NOCASE;
                """;
            find.Parameters.AddWithValue("$feedUrl", feed.FeedUrl);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                feedId = reader.GetString(0);
                isSharedCatalog = reader.GetInt64(1) == 1;
                needsInsert = false;
            }
            else
            {
                feedId = feed.Id;
            }
        }

        if (needsInsert)
        {
            await using var insertFeed = connection.CreateCommand();
            insertFeed.Transaction = transaction;
            insertFeed.CommandText = """
                INSERT INTO CatalogFeeds (
                    Id, Name, FeedUrl, Description, CategoryId, WebsiteUrl, IsSharedCatalog)
                VALUES ($id, $name, $feedUrl, $description, NULL, $websiteUrl, 0);
                """;
            insertFeed.Parameters.AddWithValue("$id", feed.Id);
            insertFeed.Parameters.AddWithValue("$name", feed.Name);
            insertFeed.Parameters.AddWithValue("$feedUrl", feed.FeedUrl);
            insertFeed.Parameters.AddWithValue("$description", (object?)feed.Description ?? DBNull.Value);
            insertFeed.Parameters.AddWithValue("$websiteUrl", (object?)feed.WebsiteUrl ?? DBNull.Value);
            await insertFeed.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!isSharedCatalog)
        {
            await using var addOwner = connection.CreateCommand();
            addOwner.Transaction = transaction;
            addOwner.CommandText = """
                INSERT OR IGNORE INTO ProfileFeedOwners (ProfileId, FeedId)
                VALUES ($profileId, $feedId);
                """;
            addOwner.Parameters.AddWithValue("$profileId", profileId);
            addOwner.Parameters.AddWithValue("$feedId", feedId);
            await addOwner.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT Id, Name, FeedUrl, Description, CategoryId, WebsiteUrl, LastHealthCheckedAt, LastHealthCheckSucceeded
            FROM CatalogFeeds WHERE Id = $feedId;
            """;
        select.Parameters.AddWithValue("$feedId", feedId);
        await using var selectedReader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await selectedReader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The feed could not be added to this profile.");
        }

        var result = ReadCatalogFeed(selectedReader);
        await selectedReader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> FeedUrlExistsAsync(
        string feedUrl,
        string? exceptFeedId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM CatalogFeeds
                WHERE FeedUrl = $feedUrl COLLATE NOCASE
                  AND ($exceptFeedId IS NULL OR Id <> $exceptFeedId)
            );
            """;
        command.Parameters.AddWithValue("$feedUrl", feedUrl);
        command.Parameters.AddWithValue("$exceptFeedId", (object?)exceptFeedId ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static CatalogFeed ReadCatalogFeed(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            reader.IsDBNull(7) ? null : reader.GetInt64(7) == 1);

    public async Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM CatalogCategories ORDER BY Name COLLATE NOCASE;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var categories = new List<CatalogCategory>();
        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(new CatalogCategory(reader.GetString(0), reader.GetString(1)));
        }

        return categories;
    }

    public async Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM CatalogCollections ORDER BY Name COLLATE NOCASE;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var collections = new List<CatalogCollection>();
        while (await reader.ReadAsync(cancellationToken))
        {
            collections.Add(new CatalogCollection(reader.GetString(0), reader.GetString(1)));
        }

        return collections;
    }

    public async Task<IReadOnlyList<string>> GetCollectionFeedIdsAsync(
        string collectionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT FeedId FROM CatalogCollectionFeeds WHERE CollectionId = $collectionId;";
        command.Parameters.AddWithValue("$collectionId", collectionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var feedIds = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            feedIds.Add(reader.GetString(0));
        }

        return feedIds;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetCollectionFeedIdsByCollectionAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CollectionId, FeedId FROM CatalogCollectionFeeds ORDER BY CollectionId;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var feedIdsByCollection = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var collectionId = reader.GetString(0);
            if (!feedIdsByCollection.TryGetValue(collectionId, out var feedIds))
            {
                feedIds = [];
                feedIdsByCollection.Add(collectionId, feedIds);
            }

            feedIds.Add(reader.GetString(1));
        }

        return feedIdsByCollection.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    public Task AddFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default) => ExecuteAsync(
        """
        INSERT INTO CatalogFeeds (Id, Name, FeedUrl, Description, CategoryId, WebsiteUrl, LastHealthCheckedAt, LastHealthCheckSucceeded)
        VALUES ($id, $name, $feedUrl, $description, $categoryId, $websiteUrl, $lastHealthCheckedAt, $lastHealthCheckSucceeded);
        """,
        cancellationToken,
        ("$id", feed.Id),
        ("$name", feed.Name),
        ("$feedUrl", feed.FeedUrl),
        ("$description", (object?)feed.Description ?? DBNull.Value),
        ("$categoryId", (object?)feed.CategoryId ?? DBNull.Value),
        ("$websiteUrl", (object?)feed.WebsiteUrl ?? DBNull.Value),
        ("$lastHealthCheckedAt", (object?)feed.LastHealthCheckedAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value),
        ("$lastHealthCheckSucceeded", feed.LastHealthCheckSucceeded is { } insertSucceeded ? (object)(insertSucceeded ? 1 : 0) : DBNull.Value));

    public Task UpdateFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default) => ExecuteAsync(
        """
        UPDATE CatalogFeeds
        SET Name = $name,
            FeedUrl = $feedUrl,
            Description = $description,
            CategoryId = $categoryId,
            WebsiteUrl = $websiteUrl,
            LastHealthCheckedAt = $lastHealthCheckedAt,
            LastHealthCheckSucceeded = $lastHealthCheckSucceeded
        WHERE Id = $id;
        """,
        cancellationToken,
        ("$id", feed.Id),
        ("$name", feed.Name),
        ("$feedUrl", feed.FeedUrl),
        ("$description", (object?)feed.Description ?? DBNull.Value),
        ("$categoryId", (object?)feed.CategoryId ?? DBNull.Value),
        ("$websiteUrl", (object?)feed.WebsiteUrl ?? DBNull.Value),
        ("$lastHealthCheckedAt", (object?)feed.LastHealthCheckedAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value),
        ("$lastHealthCheckSucceeded", feed.LastHealthCheckSucceeded is { } updateSucceeded ? (object)(updateSucceeded ? 1 : 0) : DBNull.Value));

    public Task DeleteFeedAsync(string feedId, CancellationToken cancellationToken = default) => ExecuteAsync(
        "DELETE FROM CatalogFeeds WHERE Id = $id AND IsSharedCatalog = 1;",
        cancellationToken,
        ("$id", feedId));

    public Task AddCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default) => ExecuteAsync(
        "INSERT INTO CatalogCategories (Id, Name) VALUES ($id, $name);",
        cancellationToken,
        ("$id", category.Id),
        ("$name", category.Name));

    public Task UpdateCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default) => ExecuteAsync(
        "UPDATE CatalogCategories SET Name = $name WHERE Id = $id;",
        cancellationToken,
        ("$id", category.Id),
        ("$name", category.Name));

    public async Task MergeCategoriesAsync(
        string sourceCategoryId,
        string targetCategoryId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(sourceCategoryId, targetCategoryId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Source and target categories must be different.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using (var verify = connection.CreateCommand())
        {
            verify.Transaction = transaction;
            verify.CommandText = "SELECT COUNT(*) FROM CatalogCategories WHERE Id IN ($sourceId, $targetId);";
            verify.Parameters.AddWithValue("$sourceId", sourceCategoryId);
            verify.Parameters.AddWithValue("$targetId", targetCategoryId);
            if (Convert.ToInt64(await verify.ExecuteScalarAsync(cancellationToken)) != 2)
            {
                throw new InvalidOperationException("The source or target category no longer exists.");
            }
        }

        await using (var moveFeeds = connection.CreateCommand())
        {
            moveFeeds.Transaction = transaction;
            moveFeeds.CommandText = "UPDATE CatalogFeeds SET CategoryId = $targetId WHERE CategoryId = $sourceId;";
            moveFeeds.Parameters.AddWithValue("$sourceId", sourceCategoryId);
            moveFeeds.Parameters.AddWithValue("$targetId", targetCategoryId);
            await moveFeeds.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteSource = connection.CreateCommand())
        {
            deleteSource.Transaction = transaction;
            deleteSource.CommandText = "DELETE FROM CatalogCategories WHERE Id = $sourceId;";
            deleteSource.Parameters.AddWithValue("$sourceId", sourceCategoryId);
            await deleteSource.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default) => ExecuteAsync(
        "DELETE FROM CatalogCategories WHERE Id = $id;",
        cancellationToken,
        ("$id", categoryId));

    public Task AddCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default) => ExecuteAsync(
        "INSERT INTO CatalogCollections (Id, Name) VALUES ($id, $name);",
        cancellationToken,
        ("$id", collection.Id),
        ("$name", collection.Name));

    public Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken = default) => ExecuteAsync(
        "DELETE FROM CatalogCollections WHERE Id = $id;",
        cancellationToken,
        ("$id", collectionId));

    public Task AddFeedToCollectionAsync(
        string collectionId,
        string feedId,
        CancellationToken cancellationToken = default) => ExecuteAsync(
        "INSERT OR IGNORE INTO CatalogCollectionFeeds (CollectionId, FeedId) VALUES ($collectionId, $feedId);",
        cancellationToken,
        ("$collectionId", collectionId),
        ("$feedId", feedId));

    public Task RemoveFeedFromCollectionAsync(
        string collectionId,
        string feedId,
        CancellationToken cancellationToken = default) => ExecuteAsync(
        "DELETE FROM CatalogCollectionFeeds WHERE CollectionId = $collectionId AND FeedId = $feedId;",
        cancellationToken,
        ("$collectionId", collectionId),
        ("$feedId", feedId));

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