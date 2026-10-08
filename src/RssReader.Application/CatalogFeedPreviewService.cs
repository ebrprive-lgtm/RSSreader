using RssReader.Domain;

namespace RssReader.Application;

public sealed class CatalogFeedPreviewService(IFeedDownloader downloader)
{
    private const int PreviewItemLimit = 5;

    public async Task<IReadOnlyList<DownloadedFeedItem>> GetPreviewItemsAsync(
        CatalogFeed feed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var items = await downloader.DownloadAsync(feed, cancellationToken);
        return items
            .Take(PreviewItemLimit)
            .Select(item => item with { Content = null, ImageUrl = null })
            .ToArray();
    }

    public Task<string> GetRawFeedXmlAsync(
        CatalogFeed feed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (downloader is not IRawFeedXmlDownloader rawFeedXmlDownloader)
        {
            throw new InvalidOperationException("Raw feed XML is not supported by the configured downloader.");
        }

        return rawFeedXmlDownloader.DownloadRawFeedXmlAsync(feed, cancellationToken);
    }
}