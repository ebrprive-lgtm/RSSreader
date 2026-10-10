using System.Buffers;
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
        try
        {
            return await FetchAndExtractAsync(articleUri, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ArticleContentExtractionException("Loading the publisher page timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new ArticleContentExtractionException(
                $"The publisher page could not be downloaded: {exception.Message}",
                exception);
        }
        catch (IOException exception)
        {
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

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        SmartReader.Article article;
        try
        {
            article = new Reader(articleUri.AbsoluteUri, RemoveAdvertisementRegions(html)).GetArticle();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new ArticleContentExtractionException("The publisher page could not be parsed as an article.", exception);
        }

        var articleText = article.TextContent?.Trim() ?? string.Empty;
        if (!article.IsReadable ||
            string.IsNullOrWhiteSpace(article.Content) ||
            articleText.Length < 140 ||
            IsAccessInterstitial(article.Title, articleText))
        {
            throw new ArticleContentExtractionException(
                "The publisher page did not contain readable article text. It may be a paywall or browser challenge.");
        }

        return new ExtractedArticleContent(
            article.Content,
            NullIfWhiteSpace(article.Title),
            NullIfWhiteSpace(article.Author) ?? NullIfWhiteSpace(article.Byline),
            _timeProvider.GetUtcNow());
    }

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
