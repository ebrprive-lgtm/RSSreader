using System.ServiceModel.Syndication;
using System.Xml;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

public sealed class SyndicationFeedDownloader(HttpClient httpClient) : IFeedDownloader
{
    private const int MaximumFeedCharacters = 5_000_000;
    private const int MaximumItems = 500;

    public async Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
        CatalogFeed feed,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(feed.FeedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("The catalog contains an invalid feed URL.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("RssReader/1.0");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumFeedCharacters)
        {
            throw new InvalidDataException("The feed response exceeds the supported size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumFeedCharacters,
            MaxCharactersFromEntities = 0
        });
        var syndicationFeed = SyndicationFeed.Load(xmlReader)
            ?? throw new InvalidDataException("The response does not contain an RSS or Atom feed.");

        return syndicationFeed.Items
            .Take(MaximumItems)
            .Select(item => new DownloadedFeedItem(
                item.Id,
                item.Title?.Text ?? string.Empty,
                item.Links.FirstOrDefault()?.Uri?.AbsoluteUri,
                item.PublishDate != DateTimeOffset.MinValue
                    ? item.PublishDate
                    : item.LastUpdatedTime != DateTimeOffset.MinValue
                        ? item.LastUpdatedTime
                        : null,
                item.Summary?.Text,
                (item.Content as TextSyndicationContent)?.Text))
            .ToArray();
    }
}