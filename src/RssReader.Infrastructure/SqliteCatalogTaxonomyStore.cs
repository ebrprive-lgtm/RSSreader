using Microsoft.Data.Sqlite;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

internal sealed class SqliteCatalogTaxonomyStore(SqliteConnectionFactory connections)
{
    public async Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
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

    public async Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
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
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
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
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
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

    public Task AddCategoryAsync(CatalogCategory category, CancellationToken cancellationToken) =>
        connections.ExecuteAsync(
            "INSERT INTO CatalogCategories (Id, Name) VALUES ($id, $name);",
            cancellationToken,
            ("$id", category.Id),
            ("$name", category.Name));

    public Task UpdateCategoryAsync(CatalogCategory category, CancellationToken cancellationToken) =>
        connections.ExecuteAsync(
            "UPDATE CatalogCategories SET Name = $name WHERE Id = $id;",
            cancellationToken,
            ("$id", category.Id),
            ("$name", category.Name));

    public async Task MergeCategoriesAsync(
        string sourceCategoryId,
        string targetCategoryId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(sourceCategoryId, targetCategoryId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Source and target categories must be different.");
        }

        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
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

    public Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken) =>
        connections.ExecuteAsync(
            "DELETE FROM CatalogCategories WHERE Id = $id;",
            cancellationToken,
            ("$id", categoryId));

    public Task AddCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken) =>
        connections.ExecuteAsync(
            "INSERT INTO CatalogCollections (Id, Name) VALUES ($id, $name);",
            cancellationToken,
            ("$id", collection.Id),
            ("$name", collection.Name));

    public Task UpdateCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken) =>
        connections.ExecuteAsync(
            "UPDATE CatalogCollections SET Name = $name WHERE Id = $id;",
            cancellationToken,
            ("$id", collection.Id),
            ("$name", collection.Name));

    public Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken) =>
        connections.ExecuteAsync(
            "DELETE FROM CatalogCollections WHERE Id = $id;",
            cancellationToken,
            ("$id", collectionId));

    public Task AddFeedToCollectionAsync(
        string collectionId,
        string feedId,
        CancellationToken cancellationToken) =>
        connections.ExecuteAsync(
            "INSERT OR IGNORE INTO CatalogCollectionFeeds (CollectionId, FeedId) VALUES ($collectionId, $feedId);",
            cancellationToken,
            ("$collectionId", collectionId),
            ("$feedId", feedId));

    public Task RemoveFeedFromCollectionAsync(
        string collectionId,
        string feedId,
        CancellationToken cancellationToken) =>
        connections.ExecuteAsync(
            "DELETE FROM CatalogCollectionFeeds WHERE CollectionId = $collectionId AND FeedId = $feedId;",
            cancellationToken,
            ("$collectionId", collectionId),
            ("$feedId", feedId));
}
