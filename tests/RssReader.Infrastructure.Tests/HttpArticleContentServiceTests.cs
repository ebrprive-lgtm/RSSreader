using System.Net;
using System.Text;
using RssReader.Application;
using RssReader.Infrastructure;

namespace RssReader.Infrastructure.Tests;

[TestClass]
public sealed class HttpArticleContentServiceTests
{
    private static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.216.34");

    [TestMethod]
    public async Task ExtractsReadableArticleAndResolvesRelativeResources()
    {
        var service = CreateService((_, _) => Task.FromResult(HtmlResponse(CreateReadablePage())));
        using (service)
        {
            var result = await service.ExtractAsync(new Uri("https://publisher.example/news/story"));

            StringAssert.Contains(result.Html, "Main article paragraph");
            StringAssert.Contains(result.Html, "Important details");
            StringAssert.Contains(result.Html, "First useful list item");
            StringAssert.Contains(result.Html, "https://publisher.example/images/lead.jpg");
            StringAssert.Contains(result.Html, "https://publisher.example/news/related");
            StringAssert.Contains(result.Html, "Image caption.");
            StringAssert.Contains(result.Html, "A relevant quote provides additional context");
            StringAssert.Contains(result.Html, "<table");
            StringAssert.Contains(result.Html, "2025");
            Assert.IsFalse(result.Html.Contains("navigation marker", StringComparison.OrdinalIgnoreCase), result.Html);
            Assert.IsFalse(result.Html.Contains("sidebar marker", StringComparison.OrdinalIgnoreCase), result.Html);
            Assert.IsFalse(result.Html.Contains("advertisement marker", StringComparison.OrdinalIgnoreCase), result.Html);
            Assert.IsFalse(result.Html.Contains("footer marker", StringComparison.OrdinalIgnoreCase), result.Html);
            Assert.AreEqual("Fixture Writer", result.Author);
            Assert.IsTrue(result.RetrievedAt <= DateTimeOffset.UtcNow);
        }
    }

    [TestMethod]
    public async Task RejectsTargetResolvingToPrivateAddressWithoutSendingRequest()
    {
        var requestCount = 0;
        var service = CreateService(
            (_, _) =>
            {
                requestCount++;
                return Task.FromResult(HtmlResponse(CreateReadablePage()));
            },
            new FixedHostResolver(IPAddress.Parse("192.168.1.25")));
        using (service)
        {
            await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => service.ExtractAsync(new Uri("https://publisher.example/story")));
        }

        Assert.AreEqual(0, requestCount);
    }

    [TestMethod]
    public async Task RejectsUnsupportedSchemesAndCredentialUrlsWithoutSendingRequest()
    {
        var requestCount = 0;
        var service = CreateService(
            (_, _) =>
            {
                requestCount++;
                return Task.FromResult(HtmlResponse(CreateReadablePage()));
            });
        using (service)
        {
            await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => service.ExtractAsync(new Uri("file:///private/article.html")));
            await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => service.ExtractAsync(new Uri("https://reader:secret@publisher.example/story")));
        }

        Assert.AreEqual(0, requestCount);
    }

    [TestMethod]
    public async Task RejectsRedirectToLoopbackBeforeFollowingIt()
    {
        var requestCount = 0;
        var service = CreateService(
            (request, _) =>
            {
                requestCount++;
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri("http://127.0.0.1/private");
                return Task.FromResult(response);
            });
        using (service)
        {
            await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => service.ExtractAsync(new Uri("https://publisher.example/story")));
        }

        Assert.AreEqual(1, requestCount);
    }

    [TestMethod]
    public async Task RejectsNonHtmlResponses()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"message\":\"not html\"}", Encoding.UTF8, "application/json")
        };
        var service = CreateService((_, _) => Task.FromResult(response));
        using (service)
        {
            await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => service.ExtractAsync(new Uri("https://publisher.example/story")));
        }
    }

    [TestMethod]
    public async Task RejectsResponsesLargerThanConfiguredLimit()
    {
        var content = new ByteArrayContent(new byte[HttpArticleContentService.MaximumResponseBytes + 1]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var service = CreateService((_, _) => Task.FromResult(response));
        using (service)
        {
            await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => service.ExtractAsync(new Uri("https://publisher.example/story")));
        }
    }

    [TestMethod]
    public async Task ConvertsRequestTimeoutToRecoverableExtractionFailure()
    {
        var service = CreateService(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return HtmlResponse(CreateReadablePage());
            },
            requestTimeout: TimeSpan.FromMilliseconds(20));
        using (service)
        {
            var exception = await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => service.ExtractAsync(new Uri("https://publisher.example/story")));
            StringAssert.Contains(exception.Message, "timed out");
        }
    }

    [TestMethod]
    public async Task PropagatesCallerCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        var service = CreateService(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return HtmlResponse(CreateReadablePage());
            });
        using (service)
        {
            var request = service.ExtractAsync(
                new Uri("https://publisher.example/story"),
                cancellationSource.Token);
            cancellationSource.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => request);
        }
    }

    [TestMethod]
    public async Task RejectsUnusableArticleAndHttpFailuresWithVisibleErrors()
    {
        var shortPageService = CreateService(
            (_, _) => Task.FromResult(HtmlResponse("<html><body>Checking your browser</body></html>")));
        using (shortPageService)
        {
            await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => shortPageService.ExtractAsync(new Uri("https://publisher.example/story")));
        }

        var missingPageService = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using (missingPageService)
        {
            var exception = await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => missingPageService.ExtractAsync(new Uri("https://publisher.example/story")));
            StringAssert.Contains(exception.Message, "404");
        }

        var emptyPageService = CreateService(
            (_, _) => Task.FromResult(HtmlResponse("<html><head><title>Unavailable</title></head><body></body></html>")));
        using (emptyPageService)
        {
            await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => emptyPageService.ExtractAsync(new Uri("https://publisher.example/story")));
        }

        var paywallText = string.Concat(Enumerable.Repeat(
            "Subscribe to continue reading this member-only story. ",
            10));
        var paywallService = CreateService(
            (_, _) => Task.FromResult(HtmlResponse(
                $"<html><body><article><h1>Member story</h1><p>{paywallText}</p></article></body></html>")));
        using (paywallService)
        {
            await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => paywallService.ExtractAsync(new Uri("https://publisher.example/story")));
        }

        var malformedPageService = CreateService(
            (_, _) => Task.FromResult(HtmlResponse(
                "<html><head><title>Malformed</title><body><article><div><p><<<<<")));
        using (malformedPageService)
        {
            await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => malformedPageService.ExtractAsync(new Uri("https://publisher.example/story")));
        }
    }

    [TestMethod]
    public async Task ExplainsForbiddenResponseWithoutReadingItsBody()
    {
        const string denialPageContent = "Untrusted HTML from the denial page.";
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(denialPageContent)
        };
        response.Headers.Add("cf-ray", "example-ray-id");
        var service = CreateService(
            (_, _) => Task.FromResult(response));
        using (service)
        {
            var exception = await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => service.ExtractAsync(new Uri("https://publisher.example/story")));

            StringAssert.Contains(exception.Message, "HTTP 403 Forbidden");
            StringAssert.Contains(exception.Message, "Response headers indicate Cloudflare");
            StringAssert.Contains(exception.Message, "in-app browser");
            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.IsTrue(exception.IsCloudflareResponse);
            Assert.IsFalse(exception.Message.Contains(denialPageContent, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task ForbiddenResponseWithoutKnownProviderProvidesGenericDiagnostic()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        using (service)
        {
            var exception = await Assert.ThrowsExceptionAsync<ArticleContentExtractionException>(
                () => service.ExtractAsync(new Uri("https://publisher.example/story")));

            StringAssert.Contains(exception.Message, "did not identify a blocking provider");
            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.IsFalse(exception.IsCloudflareResponse);
        }
    }

    private static HttpArticleContentService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        IArticleHostAddressResolver? resolver = null,
        TimeSpan? requestTimeout = null)
    {
        var client = new HttpClient(new StubHttpMessageHandler(send));
        return new HttpArticleContentService(
            client,
            resolver ?? new FixedHostResolver(PublicAddress),
            requestTimeout: requestTimeout);
    }

    private static HttpResponseMessage HtmlResponse(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };

    private static string CreateReadablePage()
    {
        var paragraphs = string.Concat(Enumerable.Range(0, 5).Select(index => $"""
            <p>Main article paragraph {index + 1}: careful reporting and clear explanation give readers the necessary context. 
            The underlying research includes practical examples, recent observations, and detailed comparisons with previous results.
            Each additional sentence gives the page enough meaningful editorial content for reliable article extraction.</p>
            """));

        return $$"""
            <!doctype html>
            <html>
              <head>
                <title>The Feature Story</title>
                <meta name="author" content="Fixture Writer">
              </head>
              <body>
                <nav>Home and navigation marker with many links <a href="/one">one</a> <a href="/two">two</a> <a href="/three">three</a></nav>
                <aside>Sidebar marker with unrelated content and links.</aside>
                <div class="advertisement">Advertisement marker sponsored placement.</div>
                <main>
                  <article>
                    <h1>The Feature Story</h1>
                    <p><a href="/news/related">Related story</a></p>
                    <figure><img src="/images/lead.jpg" alt="A useful image"><figcaption>Image caption.</figcaption></figure>
                    {{paragraphs}}
                    <h2>Important details</h2>
                    <ul><li>First useful list item</li><li>Second useful list item</li></ul>
                    <blockquote>A relevant quote provides additional context for the reader.</blockquote>
                    <table><tbody><tr><th>Year</th><td>2025</td></tr></tbody></table>
                  </article>
                </main>
                <footer>Footer marker with unrelated links and page navigation.</footer>
              </body>
            </html>
            """;
    }

    private sealed class FixedHostResolver(IPAddress address) : IArticleHostAddressResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([address]);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
