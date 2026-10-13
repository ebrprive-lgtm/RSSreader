using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using HtmlAgilityPack;
using RssReader.Application;
using SmartReader;

namespace RssReader.Infrastructure;

public sealed class HttpArticleContentService : IArticleContentService, IDisposable
{
    internal const int MaximumResponseBytes = 4 * 1024 * 1024;
    internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(20);
    private const int MaximumRedirects = 5;
    private readonly HttpClient _httpClient;
    private readonly IArticleHostAddressResolver _hostAddressResolver;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;
    private readonly bool _ownsHttpClient;

    static HttpArticleContentService() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public HttpArticleContentService(IArticleHostAddressResolver hostAddressResolver)
        : this(
            CreateHttpClient(hostAddressResolver),
            hostAddressResolver,
            TimeProvider.System,
            DefaultRequestTimeout,
            ownsHttpClient: true)
    {
    }

    internal HttpArticleContentService(
        HttpClient httpClient,
        IArticleHostAddressResolver hostAddressResolver,
        TimeProvider? timeProvider = null,
        TimeSpan? requestTimeout = null)
        : this(
            httpClient,
            hostAddressResolver,
            timeProvider ?? TimeProvider.System,
            requestTimeout ?? DefaultRequestTimeout,
            ownsHttpClient: true)
    {
    }

    private HttpArticleContentService(
        HttpClient httpClient,
        IArticleHostAddressResolver hostAddressResolver,
        TimeProvider timeProvider,
        TimeSpan requestTimeout,
        bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _hostAddressResolver = hostAddressResolver;
        _timeProvider = timeProvider;
        _requestTimeout = requestTimeout;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<ExtractedArticleContent> ExtractAsync(
        Uri articleUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(articleUri);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_requestTimeout);
        var diagnostic = CreateGamesRadarDiagnostic(articleUri);
        try
        {
            return await FetchAndExtractAsync(articleUri, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            diagnostic?.Invoke("Publisher-page request timed out.");
            throw new ArticleContentExtractionException("Loading the publisher page timed out.");
        }
        catch (HttpRequestException exception)
        {
            diagnostic?.Invoke($"Publisher-page request failed: {exception.Message}");
            throw new ArticleContentExtractionException(
                $"The publisher page could not be downloaded: {exception.Message}",
                exception);
        }
        catch (IOException exception)
        {
            diagnostic?.Invoke($"Publisher-page response could not be read: {exception.Message}");
            throw new ArticleContentExtractionException(
                $"The publisher page could not be read: {exception.Message}",
                exception);
        }
    }

    private async Task<ExtractedArticleContent> FetchAndExtractAsync(
        Uri articleUri,
        CancellationToken cancellationToken)
    {
        var currentUri = articleUri;
        var redirectCount = 0;
        var visitedUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diagnostic = CreateGamesRadarDiagnostic(articleUri);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostic?.Invoke($"Validating and requesting {currentUri.GetLeftPart(UriPartial.Path)}.");
            await ValidatePublicTargetAsync(currentUri, cancellationToken).ConfigureAwait(false);
            if (!visitedUris.Add(currentUri.AbsoluteUri))
            {
                throw new ArticleContentExtractionException("The publisher page redirected in a loop.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.UserAgent.ParseAdd("RSSReader/1.0");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            diagnostic?.Invoke(
                $"HTTP response {(int)response.StatusCode} ({response.ReasonPhrase}); " +
                $"contentType={response.Content.Headers.ContentType?.MediaType ?? "unknown"}.");

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= MaximumRedirects)
                {
                    throw new ArticleContentExtractionException("The publisher page redirected too many times.");
                }

                if (response.Headers.Location is not { } location)
                {
                    throw new ArticleContentExtractionException("The publisher returned an invalid redirect.");
                }

                var redirectUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                diagnostic?.Invoke(
                    $"Redirect {redirectCount + 1} to {redirectUri.GetLeftPart(UriPartial.Path)}.");
                if (currentUri.Scheme == Uri.UriSchemeHttps &&
                    redirectUri.Scheme == Uri.UriSchemeHttp)
                {
                    throw new ArticleContentExtractionException("The publisher redirected to an insecure HTTP page.");
                }

                currentUri = redirectUri;
                redirectCount++;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    var isCloudflareResponse = HasCloudflareResponseMarkers(response);
                    var providerDiagnostic = isCloudflareResponse
                        ? " Response headers indicate Cloudflare."
                        : " Response headers did not identify a blocking provider.";
                    throw new ArticleContentExtractionException(
                        "The publisher denied the app's full-text request (HTTP 403 Forbidden)." +
                        providerDiagnostic +
                        " Try opening the site in the in-app browser or your regular browser; the publisher may still deny access.",
                        statusCode: response.StatusCode,
                        isCloudflareResponse: isCloudflareResponse);
                }

                throw new ArticleContentExtractionException(
                    $"The publisher returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var contentType = response.Content.Headers.ContentType;
            if (contentType?.MediaType is not { } mediaType ||
                (!string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArticleContentExtractionException("The publisher did not return an HTML page.");
            }

            var htmlBytes = await ReadBoundedContentAsync(response.Content, cancellationToken).ConfigureAwait(false);
            var html = DecodeHtml(htmlBytes, contentType);
            diagnostic?.Invoke($"Received HTML from {currentUri.GetLeftPart(UriPartial.Path)}; bytes={htmlBytes.Length}.");
            var article = ParseArticle(currentUri, html);
            cancellationToken.ThrowIfCancellationRequested();
            return article;
        }
    }

    private async Task ValidatePublicTargetAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArticleContentExtractionException("Only public HTTP or HTTPS article pages can be loaded.");
        }

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (IPAddress.TryParse(host, out var literalAddress) && !IsPublicAddress(literalAddress))
        {
            throw new ArticleContentExtractionException("Local and private network addresses cannot be loaded.");
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArticleContentExtractionException("Local and private network addresses cannot be loaded.");
        }

        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await _hostAddressResolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            throw new ArticleContentExtractionException("The publisher address could not be resolved.", exception);
        }

        if (addresses.Count == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            throw new ArticleContentExtractionException("Local and private network addresses cannot be loaded.");
        }
    }

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new ArticleContentExtractionException("The publisher page exceeds the 4 MiB download limit.");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var bytesRead = await input.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return output.ToArray();
                }

                if (output.Length + bytesRead > MaximumResponseBytes)
                {
                    throw new ArticleContentExtractionException("The publisher page exceeds the 4 MiB download limit.");
                }

                output.Write(buffer, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string DecodeHtml(byte[] bytes, MediaTypeHeaderValue contentType)
    {
        var charset = contentType.CharSet?.Trim().Trim('"');
        Encoding encoding;
        try
        {
            encoding = string.IsNullOrWhiteSpace(charset)
                ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
                : Encoding.GetEncoding(
                    charset,
                    EncoderFallback.ReplacementFallback,
                    DecoderFallback.ReplacementFallback);
        }
        catch (ArgumentException exception)
        {
            throw new ArticleContentExtractionException("The publisher used an unsupported text encoding.", exception);
        }

        return encoding.GetString(bytes);
    }

    private ExtractedArticleContent ParseArticle(Uri articleUri, string html)
    {
        var diagnostic = CreateGamesRadarDiagnostic(articleUri);
        diagnostic?.Invoke($"Starting extraction for {articleUri.GetLeftPart(UriPartial.Path)}; pageChars={html.Length}.");

        var semanticBody = FindSemanticArticleBody(html, diagnostic);
        var pageWithoutAdvertisements = RemoveAdvertisementRegions(html);
        diagnostic?.Invoke($"After advertisement cleanup: pageChars={pageWithoutAdvertisements.Length}.");
        SmartReader.Article article;
        try
        {
            article = new Reader(articleUri.AbsoluteUri, pageWithoutAdvertisements).GetArticle();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            diagnostic?.Invoke($"Full-page SmartReader failed: {exception.GetType().Name}: {exception.Message}");
            throw new ArticleContentExtractionException("The publisher page could not be parsed as an article.", exception);
        }

        diagnostic?.Invoke(
            $"Full-page SmartReader: readable={article.IsReadable}; title=\"{article.Title}\"; " +
            $"textChars={article.TextContent?.Length ?? 0}; htmlChars={article.Content?.Length ?? 0}; " +
            $"preview=\"{CreateTextPreview(article.TextContent)}\".");

        SmartReader.Article? semanticArticle = null;
        if (semanticBody is not null)
        {
            try
            {
                semanticArticle = new Reader(articleUri.AbsoluteUri, semanticBody.Html).GetArticle();
            }
            catch (Exception exception) when (
                exception is ArgumentException or FormatException or InvalidOperationException)
            {
                diagnostic?.Invoke(
                    $"Semantic-region SmartReader failed; keeping full-page result: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        if (semanticArticle is not null)
        {
            diagnostic?.Invoke(
                $"Semantic-region SmartReader: readable={semanticArticle.IsReadable}; " +
                $"title=\"{semanticArticle.Title}\"; textChars={semanticArticle.TextContent?.Length ?? 0}; " +
                $"htmlChars={semanticArticle.Content?.Length ?? 0}; " +
                $"preview=\"{CreateTextPreview(semanticArticle.TextContent)}\".");
        }
        else
        {
            diagnostic?.Invoke("No semantic article region was selected.");
        }

        var semanticText = semanticArticle?.TextContent?.Trim();
        if (string.IsNullOrWhiteSpace(semanticText) && semanticBody is not null)
        {
            semanticText = ExtractPlainText(semanticBody.Html);
        }

        var semanticTextLength = semanticText?.Length ?? 0;
        var fullPageTextLength = article.TextContent?.Trim().Length ?? 0;
        var useSemanticContent = semanticBody is not null &&
                                 (semanticBody.ConfidenceScore >= 1000 ||
                                  !article.IsReadable ||
                                  semanticTextLength >= Math.Max(
                                      140,
                                      (int)Math.Ceiling(fullPageTextLength * 1.25d)));
        var useSemanticArticle = useSemanticContent &&
                                 semanticArticle?.IsReadable == true &&
                                 !string.IsNullOrWhiteSpace(semanticArticle.Content);
        var content = useSemanticArticle
            ? semanticArticle!.Content
            : useSemanticContent
                ? semanticBody!.Html
                : article.Content;
        var articleText = useSemanticContent
            ? semanticText ?? string.Empty
            : article.TextContent?.Trim() ?? string.Empty;
        var selectedSource = useSemanticArticle
            ? "explicit semantic article region"
            : useSemanticContent
                ? "raw explicit semantic article region"
                : "full-page SmartReader";
        diagnostic?.Invoke(
            $"Selected {selectedSource}: textChars={articleText.Length}; htmlChars={content?.Length ?? 0}; " +
            $"fullPageReadable={article.IsReadable}; fullPageTextChars={fullPageTextLength}; " +
            $"semanticConfidence={semanticBody?.ConfidenceScore ?? 0}; semanticTextChars={semanticTextLength}; " +
            $"preview=\"{CreateTextPreview(articleText)}\".");
        if ((!useSemanticContent && !article.IsReadable) ||
            string.IsNullOrWhiteSpace(content) ||
            articleText.Length < 140 ||
            IsAccessInterstitial(article.Title, articleText))
        {
            diagnostic?.Invoke(
                $"Rejected selected content: readable={(useSemanticContent || article.IsReadable)}; " +
                $"textChars={articleText.Length}; interstitial={IsAccessInterstitial(article.Title, articleText)}.");
            throw new ArticleContentExtractionException(
                "The publisher page did not contain readable article text. It may be a paywall or browser challenge.");
        }

        return new ExtractedArticleContent(
            content,
            NullIfWhiteSpace(article.Title),
            NullIfWhiteSpace(article.Author) ?? NullIfWhiteSpace(article.Byline),
            _timeProvider.GetUtcNow());
    }

    private static Action<string>? CreateGamesRadarDiagnostic(Uri articleUri)
    {
        var host = articleUri.DnsSafeHost;
        if (!host.Equals("gamesradar.com", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".gamesradar.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return message => Trace.WriteLine($"[GameRadar] {message}");
    }

    private static string CreateTextPreview(string? text)
    {
        var preview = NormalizeArticleText(text ?? string.Empty);
        return preview.Length <= 240 ? preview : $"{preview[..240]}...";
    }

    private static SemanticArticleBody? FindSemanticArticleBody(string html, Action<string>? diagnostic)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var candidates = document.DocumentNode.SelectNodes("//*[@itemprop or @id or @class]");
        if (candidates is null)
        {
            diagnostic?.Invoke("No semantic candidates found in the page markup.");
            return null;
        }

        diagnostic?.Invoke($"Inspecting {candidates.Count} elements for semantic article content.");
        HtmlNode? bestCandidate = null;
        var bestScore = int.MinValue;
        var bestConfidenceScore = 0;
        foreach (var candidate in candidates)
        {
            var confidenceScore = GetSemanticContentScore(candidate);
            if (confidenceScore == 0)
            {
                continue;
            }

            var candidateText = NormalizeArticleText(candidate.InnerText);
            var textLength = candidateText.Length;
            if (textLength < 140)
            {
                diagnostic?.Invoke(
                    $"Skipped {DescribeArticleNode(candidate)}: semantic match but textChars={textLength}.");
                continue;
            }

            var paragraphs = candidate.Descendants("p").ToArray();
            var paragraphTextLength = paragraphs.Sum(paragraph =>
                NormalizeArticleText(paragraph.InnerText).Length);
            var linkTextLength = candidate.Descendants("a").Sum(link =>
                NormalizeArticleText(link.InnerText).Length);
            var linkDensityPenalty = textLength == 0
                ? 0
                : (int)(100d * Math.Min(linkTextLength, textLength) / textLength);
            var score = confidenceScore +
                        Math.Min(paragraphs.Length, 12) * 20 +
                        Math.Min(paragraphTextLength / 200, 20) -
                        linkDensityPenalty;
            diagnostic?.Invoke(
                $"Candidate {DescribeArticleNode(candidate)}: confidence={confidenceScore}; score={score}; textChars={textLength}; " +
                $"paragraphs={paragraphs.Length}; paragraphTextChars={paragraphTextLength}; " +
                $"linkDensityPenalty={linkDensityPenalty}; preview=\"{CreateTextPreview(candidateText)}\".");
            if (score > bestScore)
            {
                bestCandidate = candidate;
                bestScore = score;
                bestConfidenceScore = confidenceScore;
            }
        }

        if (bestCandidate is null)
        {
            diagnostic?.Invoke("No semantic candidate met the minimum text threshold.");
            return null;
        }

        diagnostic?.Invoke(
            $"Selected semantic candidate {DescribeArticleNode(bestCandidate)} with " +
            $"confidence={bestConfidenceScore}; score={bestScore}.");
        return new SemanticArticleBody(bestCandidate.InnerHtml.Trim(), bestConfidenceScore);
    }

    private static string DescribeArticleNode(HtmlNode node) =>
        $"{node.Name} id=\"{node.GetAttributeValue("id", string.Empty)}\" " +
        $"class=\"{node.GetAttributeValue("class", string.Empty)}\" " +
        $"itemprop=\"{node.GetAttributeValue("itemprop", string.Empty)}\"";

    private static int GetSemanticContentScore(HtmlNode node)
    {
        var itemProperties = node.GetAttributeValue("itemprop", string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (itemProperties.Any(property =>
                property.Equals("articleBody", StringComparison.OrdinalIgnoreCase)))
        {
            return 1200;
        }

        if (HasSemanticContentMarker(node.GetAttributeValue("id", string.Empty)))
        {
            return 1100;
        }

        if (HasSemanticContentMarker(node.GetAttributeValue("class", string.Empty)))
        {
            return 1000;
        }

        return node.Name.Equals("article", StringComparison.OrdinalIgnoreCase) &&
               !HasExcludedArticleMarker(node.GetAttributeValue("class", string.Empty))
            ? 700
            : 0;
    }

    private static bool HasSemanticContentMarker(string value)
    {
        var values = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var marker in values)
        {
            var parts = marker.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeSemanticMarker)
                .Where(part => part.Length > 0)
                .ToArray();
            if (HasAdjacentSemanticPair(parts))
            {
                return true;
            }

            var compactMarker = NormalizeSemanticMarker(marker);
            if (IsSemanticBodyPrefix(compactMarker))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAdjacentSemanticPair(string[] parts)
    {
        for (var index = 0; index + 1 < parts.Length; index++)
        {
            if (IsArticleMarker(parts[index]) && IsBodyMarker(parts[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSemanticBodyPrefix(string marker) =>
        marker.StartsWith("articlebody", StringComparison.Ordinal) ||
        marker.StartsWith("articlecontent", StringComparison.Ordinal) ||
        marker.StartsWith("postbody", StringComparison.Ordinal) ||
        marker.StartsWith("postcontent", StringComparison.Ordinal) ||
        marker.StartsWith("entrybody", StringComparison.Ordinal) ||
        marker.StartsWith("entrycontent", StringComparison.Ordinal) ||
        marker.StartsWith("storybody", StringComparison.Ordinal) ||
        marker.StartsWith("storycontent", StringComparison.Ordinal);

    private static bool IsArticleMarker(string marker) =>
        marker.Equals("article", StringComparison.Ordinal) ||
        marker.Equals("post", StringComparison.Ordinal) ||
        marker.Equals("entry", StringComparison.Ordinal) ||
        marker.Equals("story", StringComparison.Ordinal);

    private static bool IsBodyMarker(string marker) =>
        marker.Equals("body", StringComparison.Ordinal) ||
        marker.Equals("content", StringComparison.Ordinal);

    private static bool HasExcludedArticleMarker(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(marker => marker.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries))
            .Select(NormalizeSemanticMarker)
            .Any(marker =>
                marker is "list" or "related" or "popular" or "card" or "teaser" or
                    "promo" or "recommendation" or "recirculation" or "sidebar" or "widget");

    private static string NormalizeSemanticMarker(string marker) =>
        new(marker.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private sealed record SemanticArticleBody(string Html, int ConfidenceScore);

    private static string ExtractPlainText(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return NormalizeArticleText(document.DocumentNode.InnerText);
    }

    private static string NormalizeArticleText(string text) =>
        string.Join(
            ' ',
            WebUtility.HtmlDecode(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsAccessInterstitial(string? title, string text)
    {
        var content = $"{title} {text}";
        string[] markers =
        [
            "verify you are human",
            "checking your browser",
            "complete the captcha",
            "enable javascript and cookies",
            "subscription required",
            "subscribe to continue reading",
            "this content is for subscribers only",
            "you have reached your article limit"
        ];

        return markers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RemoveAdvertisementRegions(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var elements = document.DocumentNode.SelectNodes("//*[@class or @id]");
        if (elements is null)
        {
            return html;
        }

        foreach (var element in elements.ToArray())
        {
            var className = element.GetAttributeValue("class", string.Empty);
            var id = element.GetAttributeValue("id", string.Empty);
            if (IsAdvertisementMarker(className) || IsAdvertisementMarker(id))
            {
                element.Remove();
            }
        }

        return document.DocumentNode.OuterHtml;
    }

    private static bool IsAdvertisementMarker(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(token => token.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries))
            .Any(token => token.Equals("ad", StringComparison.OrdinalIgnoreCase) ||
                          token.Equals("ads", StringComparison.OrdinalIgnoreCase) ||
                          token.Equals("advert", StringComparison.OrdinalIgnoreCase) ||
                          token.Equals("advertisement", StringComparison.OrdinalIgnoreCase) ||
                          token.Equals("adslot", StringComparison.OrdinalIgnoreCase) ||
                          token.Equals("sponsored", StringComparison.OrdinalIgnoreCase));

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static bool HasCloudflareResponseMarkers(HttpResponseMessage response) =>
        response.Headers.Contains("cf-ray") ||
        response.Headers.Contains("cf-cache-status") ||
        response.Headers.Server.Any(server =>
            string.Equals(server.Product?.Name, "cloudflare", StringComparison.OrdinalIgnoreCase));

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublicAddress(address.MapToIPv4());
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] != 0 &&
                   bytes[0] != 10 &&
                   bytes[0] != 127 &&
                   bytes[0] < 224 &&
                   !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) &&
                   !(bytes[0] == 169 && bytes[1] == 254) &&
                   !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
                   !(bytes[0] == 192 && bytes[1] == 168) &&
                   !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2) &&
                   !(bytes[0] == 198 && bytes[1] is 18 or 19) &&
                   !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) &&
                   !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        var ipv6Bytes = address.GetAddressBytes();
        return !IPAddress.IsLoopback(address) &&
               !address.IsIPv6LinkLocal &&
               !address.IsIPv6SiteLocal &&
               !address.IsIPv6Multicast &&
               (ipv6Bytes[0] & 0xE0) == 0x20 &&
               !(ipv6Bytes[0] == 0x20 && ipv6Bytes[1] == 0x02) &&
               !(ipv6Bytes[0] == 0x20 &&
                 ipv6Bytes[1] == 0x01 &&
                 ipv6Bytes[2] == 0x0D &&
                 ipv6Bytes[3] == 0xB8);
    }

    private static HttpClient CreateHttpClient(IArticleHostAddressResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli,
            ConnectCallback = (context, cancellationToken) =>
                OpenPublicConnectionAsync(resolver, context, cancellationToken),
            UseCookies = false,
            UseProxy = false
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static async ValueTask<Stream> OpenPublicConnectionAsync(
        IArticleHostAddressResolver resolver,
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host.TrimEnd('.');
        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            throw new HttpRequestException("The publisher address could not be resolved.", exception);
        }
        if (addresses.Count == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            throw new HttpRequestException("The publisher resolved to a local or private network address.");
        }

        SocketException? lastSocketException = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                lastSocketException = exception;
            }
        }

        throw lastSocketException ?? new SocketException((int)SocketError.HostNotFound);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
