namespace RssReader.Domain;

public sealed record FeedArticle(
    string Id,
    string FeedId,
    string ExternalId,
    string Title,
    string? Link,
    DateTimeOffset? PublishedAt,
    string? Summary,
    string? Content,
    string? ImageUrl = null,
    string? Author = null,
    string? SourceXml = null)
{
    public IReadOnlyList<ArticleCategory> Categories { get; init; } = [];
}