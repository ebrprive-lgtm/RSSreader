using RssReader.Domain;

namespace RssReader.Application;

public interface IFeedDownloader
{
    Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(CatalogFeed feed, CancellationToken cancellationToken = default);
}

public interface IRawFeedXmlDownloader
{
    Task<string> DownloadRawFeedXmlAsync(CatalogFeed feed, CancellationToken cancellationToken = default);
}

public interface IRawFeedContentDownloader
{
    Task<string> DownloadRawArticleContentAsync(
        CatalogFeed feed,
        string? externalId,
        string? link,
        string title,
        CancellationToken cancellationToken = default);
}

public sealed record DownloadedFeedItem(
    string? ExternalId,
    string Title,
    string? Link,
    DateTimeOffset? PublishedAt,
    string? Summary,
    string? Content,
    string? ImageUrl = null,
    IReadOnlyList<ArticleCategory>? Categories = null,
    string? Author = null);