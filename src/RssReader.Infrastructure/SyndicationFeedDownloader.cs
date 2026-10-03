using System.Net;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

public sealed class SyndicationFeedDownloader(HttpClient httpClient) : IFeedDownloader
{
    private const int MaximumFeedCharacters = 5_000_000;
    private const int MaximumItems = 500;
    private const string MediaRssNamespace = "http://search.yahoo.com/mrss/";

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
                (item.Content as TextSyndicationContent)?.Text,
                FindImageUrl(item, uri)))
            .ToArray();
    }

    private static string? FindImageUrl(SyndicationItem item, Uri feedUri)
    {
        var baseUri = item.Links.FirstOrDefault(link =>
                string.Equals(link.RelationshipType, "alternate", StringComparison.OrdinalIgnoreCase) && IsWebUri(link.Uri))?.Uri
            ?? item.Links.FirstOrDefault(link =>
                !string.Equals(link.RelationshipType, "enclosure", StringComparison.OrdinalIgnoreCase) && IsWebUri(link.Uri))?.Uri
            ?? feedUri;
        var imageEnclosure = item.Links.FirstOrDefault(link =>
            string.Equals(link.RelationshipType, "enclosure", StringComparison.OrdinalIgnoreCase) &&
            link.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)?.Uri;
        if (IsWebUri(imageEnclosure))
        {
            return imageEnclosure!.AbsoluteUri;
        }

        foreach (var extension in item.ElementExtensions)
        {
            if (extension.OuterNamespace != MediaRssNamespace ||
                extension.OuterName is not ("thumbnail" or "content"))
            {
                continue;
            }

            var imageUrl = (string?)extension.GetObject<XElement>().Attribute("url");
            if (TryResolveImageUrl(imageUrl, baseUri, out var resolvedImageUrl))
            {
                return resolvedImageUrl;
            }
        }

        var markup = new[] { (item.Content as TextSyndicationContent)?.Text, item.Summary?.Text };
        foreach (var text in markup)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match imageTag in Regex.Matches(text, @"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var source = Regex.Match(
                    imageTag.Value,
                    @"\b(?:src|data-src)\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)'|(?<url>[^\s>]+))",
                    RegexOptions.IgnoreCase);
                if (source.Success && TryResolveImageUrl(source.Groups["url"].Value, baseUri, out var imageUrl))
                {
                    return imageUrl;
                }
            }
        }

        return null;
    }

    private static bool TryResolveImageUrl(string? value, Uri baseUri, out string? imageUrl)
    {
        imageUrl = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var decodedValue = WebUtility.HtmlDecode(value.Trim());
        if (!Uri.TryCreate(baseUri, decodedValue, out var uri) || !IsWebUri(uri))
        {
            return false;
        }

        imageUrl = uri.AbsoluteUri;
        return true;
    }

    private static bool IsWebUri(Uri? uri) =>
        uri is { IsAbsoluteUri: true } &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}