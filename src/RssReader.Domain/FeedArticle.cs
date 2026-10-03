namespace RssReader.Domain;

public sealed record FeedArticle(
    string Id,
    string FeedId,
    string ExternalId,
    string Title,
    string? Link,
    DateTimeOffset? PublishedAt,
    string? Summary,
    string? Content);