namespace RssReader.Domain;

public sealed record ProfileFeedRefreshState(
    string ProfileId,
    string FeedId,
    DateTimeOffset LastAttemptAt,
    DateTimeOffset? LastSuccessfulAt,
    string? LastFailure);