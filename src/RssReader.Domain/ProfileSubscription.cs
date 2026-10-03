namespace RssReader.Domain;

public sealed record ProfileSubscription(
    string ProfileId,
    string FeedId,
    string FeedName,
    string FeedUrl,
    string FolderName);