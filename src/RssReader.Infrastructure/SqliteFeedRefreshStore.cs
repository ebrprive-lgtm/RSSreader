using System.Globalization;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

internal sealed class SqliteFeedRefreshStore(SqliteConnectionFactory connections)
{
    public async Task<IReadOnlyList<ProfileFeedRefreshState>> GetFeedRefreshStatesAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ProfileId, FeedId, LastAttemptAt, LastSuccessfulAt, LastFailure
            FROM ProfileFeedRefreshStates
            WHERE ProfileId = $profileId
            ORDER BY FeedId;
            """;
        command.Parameters.AddWithValue("$profileId", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var states = new List<ProfileFeedRefreshState>();
        while (await reader.ReadAsync(cancellationToken))
        {
            states.Add(new ProfileFeedRefreshState(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                reader.IsDBNull(3)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return states;
    }

    public Task RecordFeedRefreshAttemptAsync(
        string profileId,
        string feedId,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken) => connections.ExecuteAsync(
        """
        INSERT INTO ProfileFeedRefreshStates (ProfileId, FeedId, LastAttemptAt, LastSuccessfulAt, LastFailure)
        VALUES ($profileId, $feedId, $attemptedAt, NULL, NULL)
        ON CONFLICT(ProfileId, FeedId) DO UPDATE SET
            LastAttemptAt = excluded.LastAttemptAt,
            LastFailure = NULL;
        """,
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId),
        ("$attemptedAt", attemptedAt.ToString("O", CultureInfo.InvariantCulture)));

    public Task RecordFeedRefreshResultAsync(
        string profileId,
        string feedId,
        DateTimeOffset? successfulAt,
        string? failure,
        CancellationToken cancellationToken) => connections.ExecuteAsync(
        """
        UPDATE ProfileFeedRefreshStates
        SET LastSuccessfulAt = COALESCE($successfulAt, LastSuccessfulAt),
            LastFailure = $failure
        WHERE ProfileId = $profileId AND FeedId = $feedId;
        """,
        cancellationToken,
        ("$profileId", profileId),
        ("$feedId", feedId),
        ("$successfulAt", (object?)successfulAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value),
        ("$failure", (object?)failure ?? DBNull.Value));
}
