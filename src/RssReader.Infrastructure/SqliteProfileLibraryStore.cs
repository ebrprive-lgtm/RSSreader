using Microsoft.Data.Sqlite;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

internal sealed class SqliteProfileLibraryStore(SqliteConnectionFactory connections)
{
    public async Task<IReadOnlyList<ProfileSubscription>> GetSubscriptionsAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT subscription.ProfileId, feed.Id, feed.Name, feed.FeedUrl, subscription.FolderName, feed.WebsiteUrl
            FROM ProfileSubscriptions AS subscription
            INNER JOIN CatalogFeeds AS feed ON feed.Id = subscription.FeedId
            WHERE subscription.ProfileId = $profileId
              AND (
                  feed.IsSharedCatalog = 1
                  OR EXISTS (
                      SELECT 1 FROM ProfileFeedOwners AS owner
                      WHERE owner.FeedId = feed.Id AND owner.ProfileId = subscription.ProfileId
                  )
              )
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
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return subscriptions;
    }

    public Task SubscribeAsync(
        string profileId,
        string feedId,
        string folderName,
        CancellationToken cancellationToken) => connections.ExecuteAsync(
        "INSERT OR IGNORE INTO ProfileSubscriptions (ProfileId, FeedId, FolderName) VALUES ($profileId, $feedId, $folder);",
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId),
        ("$folder", folderName));

    public async Task UnsubscribeAsync(
        string profileId,
        string feedId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using (var unsubscribe = connection.CreateCommand())
        {
            unsubscribe.Transaction = transaction;
            unsubscribe.CommandText = """
                DELETE FROM ProfileSubscriptions
                WHERE ProfileId = $profileId AND FeedId = $feedId;
                """;
            unsubscribe.Parameters.AddWithValue("$profileId", profileId);
            unsubscribe.Parameters.AddWithValue("$feedId", feedId);
            await unsubscribe.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var removePersonalFeed = connection.CreateCommand())
        {
            removePersonalFeed.Transaction = transaction;
            removePersonalFeed.CommandText = """
                DELETE FROM ProfileFeedOwners
                WHERE ProfileId = $profileId AND FeedId = $feedId;
                """;
            removePersonalFeed.Parameters.AddWithValue("$profileId", profileId);
            removePersonalFeed.Parameters.AddWithValue("$feedId", feedId);
            await removePersonalFeed.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetFoldersAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
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

    public Task AddFolderAsync(string profileId, string name, CancellationToken cancellationToken) =>
        connections.ExecuteAsync(
            "INSERT INTO ProfileFolders (ProfileId, Name) VALUES ($profileId, $name);",
            cancellationToken,
            ("$profileId", profileId),
            ("$name", name.Trim()));

    public async Task DeleteFolderAsync(string profileId, string name, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using (var removePersonalFeeds = connection.CreateCommand())
        {
            removePersonalFeeds.Transaction = transaction;
            removePersonalFeeds.CommandText = """
                DELETE FROM ProfileFeedOwners
                WHERE ProfileId = $profileId
                  AND FeedId IN (
                      SELECT FeedId FROM ProfileSubscriptions
                      WHERE ProfileId = $profileId AND FolderName = $name COLLATE NOCASE
                  );
                """;
            removePersonalFeeds.Parameters.AddWithValue("$profileId", profileId);
            removePersonalFeeds.Parameters.AddWithValue("$name", name);
            await removePersonalFeeds.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var unsubscribe = connection.CreateCommand())
        {
            unsubscribe.Transaction = transaction;
            unsubscribe.CommandText = "DELETE FROM ProfileSubscriptions WHERE ProfileId = $profileId AND FolderName = $name COLLATE NOCASE;";
            unsubscribe.Parameters.AddWithValue("$profileId", profileId);
            unsubscribe.Parameters.AddWithValue("$name", name);
            await unsubscribe.ExecuteNonQueryAsync(cancellationToken);
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
        CancellationToken cancellationToken) => connections.ExecuteAsync(
        "UPDATE ProfileSubscriptions SET FolderName = $folder WHERE ProfileId = $profileId AND FeedId = $feedId;",
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId),
        ("$folder", folderName));

    public async Task<IReadOnlyList<ProfileFeedTag>> GetFeedTagsAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
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
        CancellationToken cancellationToken) => connections.ExecuteAsync(
        "INSERT OR IGNORE INTO ProfileFeedTags (ProfileId, FeedId, Name) VALUES ($profileId, $feedId, $name);",
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId),
        ("$name", tagName.Trim()));

    public Task RemoveFeedTagAsync(
        string profileId,
        string feedId,
        string tagName,
        CancellationToken cancellationToken) => connections.ExecuteAsync(
        "DELETE FROM ProfileFeedTags WHERE ProfileId = $profileId AND FeedId = $feedId AND Name = $name;",
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId),
        ("$name", tagName));
}
