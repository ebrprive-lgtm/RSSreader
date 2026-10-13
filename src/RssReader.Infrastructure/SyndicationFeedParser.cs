using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HtmlAgilityPack;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

internal static class SyndicationFeedParser
{
    private const int MaximumItems = 500;
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    private const string MediaRssNamespace = "http://search.yahoo.com/mrss/";
    private const string RssContentNamespace = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Rss10Namespace = "http://purl.org/rss/1.0/";
    private static readonly HashSet<string> ArticleMarkupElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "blockquote", "br", "div", "em", "figure", "h1", "h2", "h3", "h4", "h5", "h6",
        "hr", "i", "img", "li", "ol", "p", "pre", "span", "strong", "table", "td", "th", "tr", "ul"
    };

    public static IReadOnlyList<DownloadedFeedItem> Parse(XmlReader reader, Uri baseUri)
    {
        var sourceFeed = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var feedRoot = sourceFeed.Root
            ?? throw new InvalidDataException("The response does not contain an RSS or Atom feed.");
        var syndicationDocument = feedRoot.Name == RdfNamespace + "RDF"
            ? ConvertRdfFeed(feedRoot)
            : NormalizeLegacyRssVersion(sourceFeed);
        using var feedTextReader = new StringReader(syndicationDocument.ToString(SaveOptions.DisableFormatting));
        using var feedReader = XmlReader.Create(feedTextReader, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 5_000_000,
            MaxCharactersFromEntities = 0
        });
        var syndicationFeed = SyndicationFeed.Load(feedReader)
            ?? throw new InvalidDataException("The response does not contain an RSS or Atom feed.");

        var matchedSourceArticles = new HashSet<XElement>();
        return syndicationFeed.Items
            .Take(MaximumItems)
            .Select(item =>
            {
                var title = item.Title?.Text ?? string.Empty;
                var link = ResolveLink(item.Links.FirstOrDefault()?.Uri, baseUri);
                var sourceArticle = RawFeedArticleSelector.Select(
                    feedRoot,
                    item.Id,
                    link,
                    title,
                    baseUri,
                    matchedSourceArticles);
                if (sourceArticle is not null)
                {
                    matchedSourceArticles.Add(sourceArticle);
                }

                return new DownloadedFeedItem(
                    item.Id,
                    title,
                    link,
                    item.PublishDate != DateTimeOffset.MinValue
                        ? item.PublishDate
                        : item.LastUpdatedTime != DateTimeOffset.MinValue
                            ? item.LastUpdatedTime
                            : null,
                    GetSummary(item),
                    GetArticleContent(item) ?? GetTextContent(item.Summary),
                    FindImageUrl(item, baseUri),
                    GetCategories(item),
                    GetAuthor(item),
                    sourceArticle is null ? null : RawFeedArticleSelector.Serialize(sourceArticle));
            })
            .ToArray();
    }

    internal static string SanitizeInvalidXmlCharacters(string value)
    {
        StringBuilder? sanitized = null;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var codePoint = (int)character;
            var characterLength = 1;
            if (char.IsHighSurrogate(character) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                codePoint = char.ConvertToUtf32(character, value[index + 1]);
                characterLength = 2;
            }

            if (codePoint is 0x9 or 0xA or 0xD ||
                (codePoint >= 0x20 && codePoint <= 0xD7FF) ||
                (codePoint >= 0xE000 && codePoint <= 0xFFFD) ||
                (codePoint >= 0x10000 && codePoint <= 0x10FFFF))
            {
                sanitized?.Append(value, index, characterLength);
            }
            else
            {
                sanitized ??= new StringBuilder(value.Length).Append(value, 0, index);
                if ((index == 0 || !char.IsWhiteSpace(value[index - 1])) &&
                    (index + 1 == value.Length || !char.IsWhiteSpace(value[index + 1])) &&
                    (sanitized.Length == 0 || !char.IsWhiteSpace(sanitized[^1])))
                {
                    sanitized.Append(' ');
                }
            }

            if (characterLength == 2)
            {
                index++;
            }
        }

        return sanitized?.ToString() ?? value;
    }

    private static XDocument ConvertRdfFeed(XElement root)
    {
        var channel = root.Element(Rss10Namespace + "channel")
            ?? throw new InvalidDataException("The RDF feed does not contain an RSS channel.");
        var sourceItems = root.Elements(Rss10Namespace + "item").ToArray();
        var itemsByResource = sourceItems
            .Select(item => (Item: item, Resource: (string?)item.Attribute(RdfNamespace + "about")))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Resource))
            .GroupBy(entry => entry.Resource!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Item, StringComparer.Ordinal);
        var orderedItems = new List<XElement>();
        var addedItems = new HashSet<XElement>();

        foreach (var resource in channel.Element(Rss10Namespace + "items")?
                     .Element(RdfNamespace + "Seq")?
                     .Elements(RdfNamespace + "li")
                     .Select(item => (string?)item.Attribute(RdfNamespace + "resource"))
                     .Where(resource => !string.IsNullOrWhiteSpace(resource)) ?? Enumerable.Empty<string?>())
        {
            if (itemsByResource.TryGetValue(resource!, out var item) && addedItems.Add(item))
            {
                orderedItems.Add(item);
            }
        }

        foreach (var item in sourceItems)
        {
            if (addedItems.Add(item))
            {
                orderedItems.Add(item);
            }
        }

        var normalizedChannel = new XElement(
            "channel",
            channel.Elements()
                .Where(element => element.Name != Rss10Namespace + "items")
                .Where(element => !IsRdfResourceReference(element, "image") &&
                                  !IsRdfResourceReference(element, "textinput"))
                .Select(ConvertRss10Element),
            orderedItems.Select(item => ConvertRss10Item(item)));
        return new XDocument(new XElement(
            "rss",
            new XAttribute("version", "2.0"),
            normalizedChannel));
    }

    private static XDocument NormalizeLegacyRssVersion(XDocument feed)
    {
        var root = feed.Root;
        if (root is null ||
            root.Name != "rss" ||
            !string.Equals((string?)root.Attribute("version"), "0.92", StringComparison.Ordinal))
        {
            return feed;
        }

        var normalizedFeed = new XDocument(feed);
        normalizedFeed.Root!.SetAttributeValue("version", "2.0");
        return normalizedFeed;
    }

    private static bool IsRdfResourceReference(XElement element, string elementName) =>
        element.Name == Rss10Namespace + elementName &&
        element.Attribute(RdfNamespace + "resource") is not null;

    private static XElement ConvertRss10Item(XElement item)
    {
        var resource = (string?)item.Attribute(RdfNamespace + "about");
        var converted = ConvertRss10Element(item);
        if (!string.IsNullOrWhiteSpace(resource))
        {
            if (converted.Element("guid") is null)
            {
                converted.Add(new XElement("guid", resource));
            }

            if (converted.Element("link") is null)
            {
                converted.Add(new XElement("link", resource));
            }
        }

        return converted;
    }

    private static XElement ConvertRss10Element(XElement element) =>
        new(
            element.Name.Namespace == Rss10Namespace
                ? XName.Get(element.Name.LocalName)
                : element.Name,
            element.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.Namespace != RdfNamespace)
                .Select(attribute => new XAttribute(
                    attribute.Name.Namespace == Rss10Namespace
                        ? XName.Get(attribute.Name.LocalName)
                        : attribute.Name,
                    attribute.Value)),
            element.Nodes().Select(node => node is XElement child ? ConvertRss10Element(child) : node));

    private static string? ResolveLink(Uri? link, Uri baseUri)
    {
        var resolved = ResolveUri(link, baseUri);
        return resolved?.AbsoluteUri ?? link?.OriginalString;
    }

    private static Uri? ResolveUri(Uri? uri, Uri baseUri)
    {
        if (uri is null || uri.IsAbsoluteUri)
        {
            return uri;
        }

        return Uri.TryCreate(baseUri, uri.OriginalString, out var resolved) ? resolved : null;
    }

    private static string? GetAuthor(SyndicationItem item)
    {
        var creatorExtension = item.ElementExtensions.FirstOrDefault(extension =>
            string.Equals(extension.OuterName, "creator", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(extension.OuterNamespace, DublinCoreNamespace, StringComparison.Ordinal));
        if (creatorExtension is not null)
        {
            using var reader = creatorExtension.GetReader();
            reader.MoveToContent();
            return NormalizeCategoryValue(reader.ReadElementContentAsString());
        }

        return item.Authors
            .Select(author => NormalizeCategoryValue(author.Name) ?? NormalizeCategoryValue(author.Email))
            .FirstOrDefault(author => author is not null);
    }

    private static IReadOnlyList<ArticleCategory> GetCategories(SyndicationItem item)
    {
        var categories = new List<ArticleCategory>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in item.Categories)
        {
            var term = NormalizeCategoryValue(category.Name);
            if (term is null)
            {
                continue;
            }

            var scheme = NormalizeCategoryValue(category.Scheme);
            var label = NormalizeCategoryValue(category.Label);
            var identity = $"{term}\0{scheme}";
            if (seen.Add(identity))
            {
                categories.Add(new ArticleCategory(term, scheme, label));
            }
        }

        return categories;
    }

    private static string? NormalizeCategoryValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FindImageUrl(SyndicationItem item, Uri feedUri)
    {
        var baseUri = item.Links
                .Where(link => string.Equals(link.RelationshipType, "alternate", StringComparison.OrdinalIgnoreCase))
                .Select(link => ResolveUri(link.Uri, feedUri))
                .FirstOrDefault(IsWebUri)
            ?? item.Links
                .Where(link => !string.Equals(link.RelationshipType, "enclosure", StringComparison.OrdinalIgnoreCase))
                .Select(link => ResolveUri(link.Uri, feedUri))
                .FirstOrDefault(IsWebUri)
            ?? feedUri;
        var imageEnclosure = item.Links.FirstOrDefault(link =>
            string.Equals(link.RelationshipType, "enclosure", StringComparison.OrdinalIgnoreCase) &&
            link.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)?.Uri;
        imageEnclosure = ResolveUri(imageEnclosure, baseUri);
        if (IsWebUri(imageEnclosure))
        {
            return imageEnclosure!.AbsoluteUri;
        }

        var mediaImages = GetMediaRssElements(item, "thumbnail")
            .Concat(GetMediaRssElements(item, "content").Where(IsMediaImage));
        foreach (var element in mediaImages)
        {
            var imageUrl = (string?)element.Attribute("url");
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

    private static string? GetSummary(SyndicationItem item)
    {
        var summary = HtmlTextParser.ToPlainText(item.Summary?.Text);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            return summary;
        }

        return HtmlTextParser.ToPlainText(
            GetMediaRssElements(item, "description")
                .Select(element => element.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static IEnumerable<XElement> GetMediaRssElements(SyndicationItem item, string elementName)
    {
        var elementNameWithNamespace = XName.Get(elementName, MediaRssNamespace);
        return item.ElementExtensions
            .Where(extension => extension.OuterNamespace == MediaRssNamespace)
            .SelectMany(extension => extension.GetObject<XElement>().DescendantsAndSelf(elementNameWithNamespace));
    }

    private static bool IsMediaImage(XElement element)
    {
        var medium = (string?)element.Attribute("medium");
        var type = (string?)element.Attribute("type");
        return string.Equals(medium, "image", StringComparison.OrdinalIgnoreCase) ||
               (string.IsNullOrWhiteSpace(medium) &&
                (string.IsNullOrWhiteSpace(type) || type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)));
    }

    private static string? GetArticleContent(SyndicationItem item)
    {
        if (item.Content is TextSyndicationContent textContent)
        {
            return GetTextContent(textContent);
        }

        if (item.Content is XmlSyndicationContent xmlContent)
        {
            using var reader = xmlContent.GetReaderAtContent();
            return reader.ReadOuterXml();
        }

        var encodedContent = item.ElementExtensions.FirstOrDefault(extension =>
            extension.OuterNamespace == RssContentNamespace && extension.OuterName == "encoded");
        return encodedContent?.GetObject<XElement>().Value;
    }

    private static string? GetTextContent(TextSyndicationContent? content)
    {
        if (content is null)
        {
            return null;
        }

        if (!string.Equals(content.Type, "text", StringComparison.OrdinalIgnoreCase) || ContainsArticleMarkup(content.Text))
        {
            return content.Text;
        }

        return WebUtility.HtmlEncode(content.Text)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static bool ContainsArticleMarkup(string content)
    {
        var document = new HtmlDocument();
        document.LoadHtml(content);
        return document.DocumentNode.Descendants().Any(node =>
            node.NodeType == HtmlNodeType.Element && ArticleMarkupElementNames.Contains(node.Name));
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

    internal static bool IsWebUri(Uri? uri) =>
        uri is { IsAbsoluteUri: true } &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
