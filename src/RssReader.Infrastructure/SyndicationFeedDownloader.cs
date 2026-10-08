using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HtmlAgilityPack;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

public sealed class SyndicationFeedDownloader(HttpClient httpClient) :
    IFeedDownloader,
    IRawFeedContentDownloader,
    IRawFeedXmlDownloader
{
    private const int MaximumFeedCharacters = 5_000_000;
    private const int MaximumItems = 500;
    private const int MaximumErrorBodyBytes = 4_096;
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    private const string MediaRssNamespace = "http://search.yahoo.com/mrss/";
    private const string RssContentNamespace = "http://purl.org/rss/1.0/modules/content/";
    private static readonly HashSet<string> ArticleMarkupElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "blockquote", "br", "div", "em", "figure", "h1", "h2", "h3", "h4", "h5", "h6",
        "hr", "i", "img", "li", "ol", "p", "pre", "span", "strong", "table", "td", "th", "tr", "ul"
    };

    public async Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
        CatalogFeed feed,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(feed.FeedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("The catalog contains an invalid feed URL.");
        }

        using var response = await SendFeedRequestAsync(uri, cancellationToken).ConfigureAwait(false);
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
        var syndicationFeed = SyndicationFeed.Load(xmlReader)
            ?? throw new InvalidDataException("The response does not contain an RSS or Atom feed.");

        return syndicationFeed.Items
            .Take(MaximumItems)
            .Select(item => new DownloadedFeedItem(
                item.Id,
                item.Title?.Text ?? string.Empty,
                ResolveLink(item.Links.FirstOrDefault()?.Uri, feedBaseUri),
                item.PublishDate != DateTimeOffset.MinValue
                    ? item.PublishDate
                    : item.LastUpdatedTime != DateTimeOffset.MinValue
                        ? item.LastUpdatedTime
                        : null,
                HtmlTextParser.ToPlainText(item.Summary?.Text),
                GetArticleContent(item) ?? GetTextContent(item.Summary),
                FindImageUrl(item, feedBaseUri),
                GetCategories(item),
                GetAuthor(item)))
            .ToArray();
    }

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

    public async Task<string> DownloadRawContentAsync(
        CatalogFeed feed,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(feed.FeedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("The catalog contains an invalid feed URL.");
        }

        using var response = await SendFeedRequestAsync(uri, cancellationToken).ConfigureAwait(false);

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

    private async Task<HttpResponseMessage> SendFeedRequestAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("RssReader/1.0");
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();
            throw new HttpRequestException(
                FormatTransportFailure(uri, request, stopwatch.Elapsed, exception),
                exception,
                exception.StatusCode);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            throw new TaskCanceledException(
                FormatTransportFailure(uri, request, stopwatch.Elapsed, exception),
                exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var bodyExcerpt = await ReadErrorBodyExcerptAsync(response.Content, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            throw new HttpRequestException(
                FormatHttpFailure(uri, request, response, stopwatch.Elapsed, bodyExcerpt),
                inner: null,
                response.StatusCode);
        }
    }

    private static string FormatTransportFailure(
        Uri uri,
        HttpRequestMessage request,
        TimeSpan elapsed,
        Exception exception)
    {
        var details = new StringBuilder()
            .AppendLine("The feed request did not receive an HTTP response.")
            .AppendLine($"Original URI: {RedactUri(uri)}")
            .AppendLine($"Elapsed: {elapsed.TotalMilliseconds:F0} ms")
            .AppendLine($"Request method: {request.Method}")
            .AppendLine("Request headers:")
            .AppendLine(FormatHeaders(request.Headers))
            .AppendLine("Exception details:");

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            details.Append(current.GetType().Name).Append(": ").AppendLine(current.Message);
        }

        return details.ToString().TrimEnd();
    }

    private static string FormatHttpFailure(
        Uri originalUri,
        HttpRequestMessage request,
        HttpResponseMessage response,
        TimeSpan elapsed,
        ErrorBodyExcerpt bodyExcerpt)
    {
        var finalUri = response.RequestMessage?.RequestUri;
        var isBrowserVerificationChallenge = IsBrowserVerificationChallenge(bodyExcerpt.Text);
        var details = new StringBuilder()
            .AppendLine($"Feed request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).")
            .AppendLine($"Reason phrase: {response.ReasonPhrase ?? "(none)"}")
            .AppendLine($"Original URI: {RedactUri(originalUri)}")
            .AppendLine($"Final URI: {(finalUri is null ? "(unavailable)" : RedactUri(finalUri))}")
            .AppendLine($"Elapsed: {elapsed.TotalMilliseconds:F0} ms")
            .AppendLine($"HTTP version: {response.Version}")
            .AppendLine($"Request method: {request.Method}")
            .AppendLine("Request headers:")
            .AppendLine(FormatHeaders(request.Headers))
            .AppendLine("Final request headers:")
            .AppendLine(response.RequestMessage is null
                ? "  (unavailable)"
                : FormatHeaders(response.RequestMessage.Headers))
            .AppendLine("Response headers:")
            .AppendLine(FormatHeaders(response.Headers))
            .AppendLine("Response content headers:")
            .AppendLine(response.Content is null ? "(none)" : FormatHeaders(response.Content.Headers));

        if (isBrowserVerificationChallenge)
        {
            details.AppendLine(
                "Likely cause: The server returned a browser-verification/anti-bot page instead of RSS or Atom data. " +
                "This downloader does not execute JavaScript or complete browser challenges; use a publisher-provided " +
                "feed endpoint that permits feed-reader requests.");
        }

        details.AppendLine("Response body excerpt:");

        if (bodyExcerpt.Error is not null)
        {
            details.AppendLine(bodyExcerpt.Error);
        }
        else if (string.IsNullOrEmpty(bodyExcerpt.Text))
        {
            details.AppendLine("(empty)");
        }
        else if (isBrowserVerificationChallenge)
        {
            details.AppendLine(
                "Browser-verification challenge markup and script omitted. Detected indicators include " +
                "'Checking your browser', a JavaScript-required notice, and a challenge endpoint.");
        }
        else
        {
            details.Append(bodyExcerpt.Text);
            if (bodyExcerpt.IsTruncated)
            {
                details.AppendLine().Append($"[truncated after {MaximumErrorBodyBytes} bytes]");
            }
        }

        if (bodyExcerpt.DecodingNote is not null)
        {
            details.AppendLine().Append(bodyExcerpt.DecodingNote);
        }

        return details.ToString().TrimEnd();
    }

    private static bool IsBrowserVerificationChallenge(string body) =>
        body.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase) &&
        (body.Contains("Javascript required", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("__challenge", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("X-Hashcash-Solution", StringComparison.OrdinalIgnoreCase));

    private static string FormatHeaders(HttpHeaders headers)
    {
        var formattedHeaders = headers.Select(header =>
        {
            var value = IsSensitiveHeader(header.Key)
                ? "[redacted]"
                : (string.Equals(header.Key, "Location", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(header.Key, "Referer", StringComparison.OrdinalIgnoreCase)) &&
                  header.Value.FirstOrDefault() is { } uriValue
                    ? RedactUriValue(uriValue)
                    : string.Join(", ", header.Value);
            return $"  {header.Key}: {value}";
        });
        return string.Join(Environment.NewLine, formattedHeaders.DefaultIfEmpty("  (none)"));
    }

    private static bool IsSensitiveHeader(string name) =>
        name.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("key", StringComparison.OrdinalIgnoreCase);

    private static string RedactUri(Uri uri)
    {
        var builder = new UriBuilder(uri);
        if (!string.IsNullOrEmpty(builder.UserName))
        {
            builder.UserName = "[redacted]";
        }

        if (!string.IsNullOrEmpty(builder.Password))
        {
            builder.Password = "[redacted]";
        }

        if (!string.IsNullOrEmpty(builder.Fragment))
        {
            builder.Fragment = "[redacted]";
        }

        if (!string.IsNullOrEmpty(builder.Query))
        {
            builder.Query = RedactQueryValues(builder.Query);
        }

        return builder.Uri.ToString();
    }

    private static string RedactUriValue(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? RedactUri(uri)
            : RedactQueryValuesInText(value);

    private static string RedactQueryValuesInText(string value)
    {
        var queryStart = value.IndexOf('?');
        if (queryStart < 0)
        {
            return value;
        }

        var fragmentStart = value.IndexOf('#', queryStart);
        var queryEnd = fragmentStart < 0 ? value.Length : fragmentStart;
        var redactedQuery = RedactQueryValues(value[queryStart..queryEnd]);
        return value[..queryStart] + redactedQuery + value[queryEnd..];
    }

    private static string RedactQueryValues(string query)
    {
        var hasQuestionMark = query.StartsWith("?", StringComparison.Ordinal);
        var redactedQuery = string.Join(
            "&",
            query.TrimStart('?').Split('&').Select(part =>
            {
                var separator = part.IndexOf('=');
                return separator < 0 ? part : $"{part[..(separator + 1)]}[redacted]";
            }));
        return hasQuestionMark ? $"?{redactedQuery}" : redactedQuery;
    }

    private static async Task<ErrorBodyExcerpt> ReadErrorBodyExcerptAsync(
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return new ErrorBodyExcerpt(string.Empty, false, null, null);
        }

        try
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[MaximumErrorBodyBytes + 1];
            var bytesRead = 0;
            while (bytesRead < buffer.Length)
            {
                var count = await stream.ReadAsync(
                    buffer.AsMemory(bytesRead, buffer.Length - bytesRead),
                    cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                bytesRead += count;
            }

            var isTruncated = bytesRead > MaximumErrorBodyBytes;
            var excerptLength = Math.Min(bytesRead, MaximumErrorBodyBytes);
            var decodingNote = default(string);
            Encoding encoding;
            var charset = content.Headers.ContentType?.CharSet?.Trim().Trim('"');
            try
            {
                encoding = string.IsNullOrWhiteSpace(charset)
                    ? Encoding.UTF8
                    : Encoding.GetEncoding(charset, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
                decodingNote = $"Could not decode the body using declared charset '{charset}'; UTF-8 was used.";
            }
            catch (NotSupportedException)
            {
                encoding = Encoding.UTF8;
                decodingNote = $"Could not decode the body using declared charset '{charset}'; UTF-8 was used.";
            }

            return new ErrorBodyExcerpt(
                encoding.GetString(buffer, 0, excerptLength),
                isTruncated,
                decodingNote,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            return new ErrorBodyExcerpt(
                string.Empty,
                false,
                null,
                $"Could not read the response body excerpt: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private sealed record ErrorBodyExcerpt(string Text, bool IsTruncated, string? DecodingNote, string? Error);

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
        var articles = root.Descendants().Where(IsArticleElement).ToArray();
        var baseUri = new Uri(feed.FeedUrl, UriKind.Absolute);
        var selectedArticle = !string.IsNullOrWhiteSpace(externalId)
            ? articles.FirstOrDefault(article => string.Equals(
                GetArticleId(article),
                externalId.Trim(),
                StringComparison.Ordinal))
            : null;

        if (selectedArticle is null && !string.IsNullOrWhiteSpace(link))
        {
            var normalizedLink = NormalizeLink(link, baseUri);
            selectedArticle = articles.FirstOrDefault(article => GetArticleLinks(article)
                .Any(articleLink => string.Equals(NormalizeLink(articleLink, baseUri), normalizedLink, StringComparison.Ordinal)));
        }

        if (selectedArticle is null && !string.IsNullOrWhiteSpace(title))
        {
            selectedArticle = articles.FirstOrDefault(article => string.Equals(
                GetChildText(article, "title"),
                title.Trim(),
                StringComparison.Ordinal));
        }

        return selectedArticle?.ToString(SaveOptions.None)
            ?? throw new InvalidDataException("The selected article could not be found in the current feed.");
    }

    private static bool IsArticleElement(XElement element) =>
        element.Name.LocalName switch
        {
            "item" => element.Parent?.Name.LocalName is "channel" or "RDF",
            "entry" => element.Parent?.Name.LocalName == "feed",
            _ => false
        };

    private static string? GetArticleId(XElement article) =>
        GetChildText(article, "guid") ?? GetChildText(article, "id");

    private static string? GetChildText(XElement element, string localName) =>
        element.Elements()
            .FirstOrDefault(child => string.Equals(child.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();

    private static IEnumerable<string> GetArticleLinks(XElement article) =>
        article.Elements()
            .Where(child => string.Equals(child.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase))
            .Select(link => (string?)link.Attribute("href") ?? link.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim());

    private static string NormalizeLink(string link, Uri baseUri) =>
        Uri.TryCreate(baseUri, link.Trim(), out var resolved) && IsWebUri(resolved)
            ? resolved.AbsoluteUri
            : link.Trim();

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

    private static bool IsWebUri(Uri? uri) =>
        uri is { IsAbsoluteUri: true } &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}