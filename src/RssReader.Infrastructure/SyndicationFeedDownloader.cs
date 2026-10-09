using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

public sealed class SyndicationFeedDownloader(HttpClient httpClient) :
    IFeedDownloader,
    IRawFeedContentDownloader,
    IRawFeedXmlDownloader
{
    private const int MaximumFeedCharacters = 5_000_000;
    private readonly FeedRequestClient _requestClient = new(httpClient);

    public async Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
        CatalogFeed feed,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(feed.FeedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("The catalog contains an invalid feed URL.");
        }

        using var response = await _requestClient.SendAsync(uri, cancellationToken).ConfigureAwait(false);
        var feedBaseUri = response.RequestMessage?.RequestUri ?? uri;

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
        return SyndicationFeedParser.Parse(xmlReader, feedBaseUri);
    }

    public async Task<string> DownloadRawContentAsync(
        CatalogFeed feed,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(feed.FeedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("The catalog contains an invalid feed URL.");
        }

        using var response = await _requestClient.SendAsync(uri, cancellationToken).ConfigureAwait(false);

        if (response.Content.Headers.ContentLength is > MaximumFeedCharacters)
        {
            throw new InvalidDataException("The feed response exceeds the supported size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim().Trim('"');
        var encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
        using var textReader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        var content = new StringBuilder();
        var buffer = new char[8192];
        int charactersRead;
        while ((charactersRead = await textReader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (content.Length + charactersRead > MaximumFeedCharacters)
            {
                throw new InvalidDataException("The feed response exceeds the supported size.");
            }

            content.Append(buffer, 0, charactersRead);
        }

        return content.ToString();
    }

    public Task<string> DownloadRawFeedXmlAsync(
        CatalogFeed feed,
        CancellationToken cancellationToken = default) =>
        DownloadRawContentAsync(feed, cancellationToken);



    public async Task<string> DownloadRawArticleContentAsync(
        CatalogFeed feed,
        string? externalId,
        string? link,
        string title,
        CancellationToken cancellationToken = default)
    {
        var rawContent = await DownloadRawContentAsync(feed, cancellationToken).ConfigureAwait(false);
        using var textReader = new StringReader(rawContent);
        using var xmlReader = XmlReader.Create(textReader, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumFeedCharacters,
            MaxCharactersFromEntities = 0
        });

        var root = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace).Root
            ?? throw new InvalidDataException("The feed does not contain an XML root element.");
        var baseUri = new Uri(feed.FeedUrl, UriKind.Absolute);
        var selectedArticle = RawFeedArticleSelector.Select(root, externalId, link, title, baseUri);

        return selectedArticle?.ToString(SaveOptions.None)
            ?? throw new InvalidDataException("The selected article could not be found in the current feed.");
    }
}
