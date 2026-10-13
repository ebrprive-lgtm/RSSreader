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
    private static readonly Regex HtmlDocumentStart = new(
        @"\A[\uFEFF\s]*(?:<\?xml\b[^?]*\?>\s*)?(?:<!doctype\s+html\b|<html\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

        var responseText = await ReadContentAsync(response.Content, cancellationToken).ConfigureAwait(false);
        var feedText = SyndicationFeedParser.SanitizeInvalidXmlCharacters(responseText);
        if (HtmlDocumentStart.IsMatch(feedText))
        {
            throw new InvalidDataException(
                "The URL returned an HTML web page instead of an RSS or Atom feed. Use the site's feed URL.");
        }

        using var textReader = new StringReader(feedText);
        using var xmlReader = XmlReader.Create(textReader, new XmlReaderSettings
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

        return await ReadContentAsync(response.Content, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumFeedCharacters)
        {
            throw new InvalidDataException("The feed response exceeds the supported size.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var charset = content.Headers.ContentType?.CharSet?.Trim().Trim('"');
        var encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
        using var textReader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        var contentBuilder = new StringBuilder();
        var buffer = new char[8192];
        int charactersRead;
        while ((charactersRead = await textReader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (contentBuilder.Length + charactersRead > MaximumFeedCharacters)
            {
                throw new InvalidDataException("The feed response exceeds the supported size.");
            }

            contentBuilder.Append(buffer, 0, charactersRead);
        }

        return contentBuilder.ToString();
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

        return selectedArticle is null
            ? throw new InvalidDataException("The selected article could not be found in the current feed.")
            : RawFeedArticleSelector.Serialize(selectedArticle);
    }
}
