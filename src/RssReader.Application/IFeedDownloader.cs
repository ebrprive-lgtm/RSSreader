using RssReader.Domain;

namespace RssReader.Application;

public interface IFeedDownloader
{
    Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(CatalogFeed feed, CancellationToken cancellationToken = default);
}

public interface IRawFeedContentDownloader
{
    Task<string> DownloadRawContentAsync(CatalogFeed feed, CancellationToken cancellationToken = default);
}

public sealed record DownloadedFeedItem(
    string? ExternalId,
    string Title,
    string? Link,
    DateTimeOffset? PublishedAt,
    string? Summary,
    string? Content,
    string? ImageUrl = null);