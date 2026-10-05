using System.Net;
using System.Xml;
using System.Xml.Linq;
using RssReader.Domain;
using RssReader.Infrastructure;

namespace RssReader.Infrastructure.Tests;

[TestClass]
public sealed class SyndicationFeedDownloaderTests
{
    [TestMethod]
    public async Task Download_RssNormalizesItemMetadata()
    {
        const string xml = """
            <rss version="2.0">
              <channel>
                <title>Sample feed</title>
                <item>
                  <title>RSS headline</title>
                  <guid>rss-item-1</guid>
                  <link>https://example.test/story</link>
                  <pubDate>Sat, 03 Oct 2026 12:00:00 GMT</pubDate>
                  <description>Feed summary</description>
                </item>
              </channel>
            </rss>
            """;
        using var client = CreateClient(xml);
        var downloader = new SyndicationFeedDownloader(client);

        var items = await downloader.DownloadAsync(CreateFeed());

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("rss-item-1", items[0].ExternalId);
        Assert.AreEqual("RSS headline", items[0].Title);
        Assert.AreEqual("https://example.test/story", items[0].Link);
        Assert.AreEqual("Feed summary", items[0].Summary);
    }

    [TestMethod]
    public async Task Download_RssPreservesNormalizedArticleCategories()
    {
        const string xml = """
            <rss version="2.0"><channel><title>Sample feed</title><item>
              <title>Press release</title>
              <guid>press-release-1</guid>
                            <link>https://example.test/press-release</link>
              <category><![CDATA[ Press Releases ]]></category>
              <category><![CDATA[press releases]]></category>
              <category domain="https://example.test/topics">Public Notices</category>
              <category><![CDATA[   ]]></category>
            </item></channel></rss>
            """;
        using var client = CreateClient(xml);
        var downloader = new SyndicationFeedDownloader(client);

        var categories = (await downloader.DownloadAsync(CreateFeed())).Single().Categories!;

        Assert.AreEqual(2, categories.Count);
        Assert.AreEqual("Press Releases", categories[0].Term);
        Assert.IsNull(categories[0].Scheme);
        Assert.AreEqual("Public Notices", categories[1].Term);
        Assert.AreEqual("https://example.test/topics", categories[1].Scheme);
    }

    [TestMethod]
    public async Task Download_RssReadsDublinCoreCreator()
    {
        const string xml = """
            <rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/"><channel><title>Sample feed</title><item>
              <title>Article</title>
              <dc:creator><![CDATA[Jayme Lozano Carver, The Texas Tribune]]></dc:creator>
            </item></channel></rss>
            """;
        using var client = CreateClient(xml);
        var downloader = new SyndicationFeedDownloader(client);

        var item = (await downloader.DownloadAsync(CreateFeed())).Single();

        Assert.AreEqual("Jayme Lozano Carver, The Texas Tribune", item.Author);
    }

        [TestMethod]
        public async Task Download_RssUsesEncodedArticleBodyWhenFeedProvidesIt()
        {
                const string xml = """
                        <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/">
                            <channel><title>Sample feed</title><item>
                                <title>Article</title>
                                <guid>article-1</guid>
                                <link>https://example.test/article</link>
                                <description>A short excerpt [...]</description>
                                <content:encoded><![CDATA[<p>First complete paragraph.</p><p>Second complete paragraph with <img src="/images/panel.jpg" alt="Panel" />.</p>]]></content:encoded>
                            </item></channel>
                        </rss>
                        """;
                using var client = CreateClient(xml);
                var downloader = new SyndicationFeedDownloader(client);

                var item = (await downloader.DownloadAsync(CreateFeed())).Single();

                Assert.AreEqual("A short excerpt [...]", item.Summary);
                StringAssert.Contains(item.Content, "First complete paragraph.");
                StringAssert.Contains(item.Content, "Second complete paragraph");
                StringAssert.Contains(item.Content, "<img src=\"/images/panel.jpg\"");
        }

            [TestMethod]
            public async Task Download_RssPreservesHtmlDescriptionAsReaderContentFallback()
            {
                const string xml = """
                    <rss version="2.0"><channel><title>Sample feed</title><item>
                      <title>Article</title>
                      <guid>article-1</guid>
                      <link>https://example.test/article</link>
                      <description><![CDATA[<p>Story text with <img src="/images/story.jpg" alt="Story image" />.</p>]]></description>
                    </item></channel></rss>
                    """;
                using var client = CreateClient(xml);
                var downloader = new SyndicationFeedDownloader(client);

                var item = (await downloader.DownloadAsync(CreateFeed())).Single();

                Assert.AreEqual("Story text with .", item.Summary);
                StringAssert.Contains(item.Content, "<p>");
                StringAssert.Contains(item.Content, "<img src=\"/images/story.jpg\"");
            }

    [TestMethod]
    public async Task DownloadRawContent_ReturnsOriginalXmlText()
    {
        const string xml = "<?xml version=\"1.0\"?><rss><channel><title>Raw &amp; exact</title></channel></rss>";
        using var client = CreateClient(xml);
        var downloader = new SyndicationFeedDownloader(client);

        var rawContent = await downloader.DownloadRawContentAsync(CreateFeed());

        Assert.AreEqual(xml, rawContent);
    }

    [TestMethod]
    public async Task DownloadRawArticleContent_RssReturnsOnlyMatchingItem()
    {
        const string xml = """
            <rss version="2.0"><channel><title>Sample feed</title>
              <item><guid>rss-item-1</guid><title>First headline</title><link>https://example.test/first</link></item>
              <item><guid>rss-item-2</guid><title>Second headline</title><link>https://example.test/second</link></item>
            </channel></rss>
            """;
        using var client = CreateClient(xml);
        var downloader = new SyndicationFeedDownloader(client);

        var rawContent = await downloader.DownloadRawArticleContentAsync(
            CreateFeed(),
            "rss-item-2",
            "https://example.test/second",
            "Second headline");

        var item = XElement.Parse(rawContent);
        Assert.AreEqual("item", item.Name.LocalName);
        StringAssert.Contains(rawContent, "rss-item-2");
        StringAssert.Contains(rawContent, "Second headline");
        Assert.IsFalse(rawContent.Contains("rss-item-1", StringComparison.Ordinal));
        Assert.IsFalse(rawContent.Contains("First headline", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DownloadRawArticleContent_AtomReturnsOnlyMatchingEntry()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/">
              <title>Sample Atom</title>
              <entry><id>atom-item-1</id><title>First headline</title><link href="https://example.test/first" /></entry>
              <entry><id>atom-item-2</id><title>Second headline</title><link href="https://example.test/second" /><media:thumbnail url="https://example.test/image.jpg" /></entry>
            </feed>
            """;
        using var client = CreateClient(xml);
        var downloader = new SyndicationFeedDownloader(client);

        var rawContent = await downloader.DownloadRawArticleContentAsync(
            CreateFeed(),
            "atom-item-2",
            "https://example.test/second",
            "Second headline");

        var entry = XElement.Parse(rawContent);
        Assert.AreEqual("entry", entry.Name.LocalName);
        StringAssert.Contains(rawContent, "atom-item-2");
        StringAssert.Contains(rawContent, "Second headline");
        StringAssert.Contains(rawContent, "thumbnail");
        Assert.IsFalse(rawContent.Contains("atom-item-1", StringComparison.Ordinal));
        Assert.IsFalse(rawContent.Contains("First headline", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Download_AtomReadsEntryAndTextContent()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Sample Atom</title>
              <entry>
                <id>atom-item-1</id>
                <title>Atom headline</title>
                <updated>2026-10-03T12:00:00Z</updated>
                <link href="https://example.test/atom-story" />
                <summary>Atom summary</summary>
                <content type="text">Atom body</content>
              </entry>
            </feed>
            """;
        using var client = CreateClient(xml);
        var downloader = new SyndicationFeedDownloader(client);

        var items = await downloader.DownloadAsync(CreateFeed());

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("atom-item-1", items[0].ExternalId);
        Assert.AreEqual("Atom headline", items[0].Title);
        Assert.AreEqual("Atom summary", items[0].Summary);
        Assert.AreEqual("Atom body", items[0].Content);
    }

        [TestMethod]
        public async Task Download_AtomPreservesHtmlAndXhtmlContentMarkup()
        {
                const string xml = """
                        <feed xmlns="http://www.w3.org/2005/Atom">
                            <title>Sample Atom</title>
                            <entry>
                                <id>atom-html-1</id>
                                <title>HTML content</title>
                                <link href="https://example.test/html" />
                                <content type="html">&lt;p&gt;HTML body with &lt;strong&gt;emphasis&lt;/strong&gt;.&lt;/p&gt;</content>
                            </entry>
                            <entry>
                                <id>atom-xhtml-1</id>
                                <title>XHTML content</title>
                                <link href="https://example.test/xhtml" />
                                <content type="xhtml">
                                    <div xmlns="http://www.w3.org/1999/xhtml">
                                        <p>XHTML body with <strong>emphasis</strong>.</p>
                                    </div>
                                </content>
                            </entry>
                        </feed>
                        """;
                using var client = CreateClient(xml);
                var downloader = new SyndicationFeedDownloader(client);

                var items = await downloader.DownloadAsync(CreateFeed());

                Assert.AreEqual(2, items.Count);
                StringAssert.Contains(items[0].Content, "<p>HTML body with <strong>emphasis</strong>.</p>");
                StringAssert.Contains(items[1].Content, "<p>XHTML body with <strong>emphasis</strong>.</p>");
        }

        [TestMethod]
        public async Task Download_ConvertsHtmlSummaryToReadableText()
        {
                const string xml = """
                        <rss version="2.0">
                            <channel>
                                <title>Sample feed</title>
                                <item>
                                    <title>HTML summary</title>
                                    <guid>html-item-1</guid>
                                    <link>https://example.test/html-story</link>
                                    <pubDate>Sat, 03 Oct 2026 12:00:00 GMT</pubDate>
                                    <description>&lt;p&gt;Read &lt;strong&gt;this&lt;/strong&gt; &amp;amp; enjoy.&lt;/p&gt;&lt;p&gt;Second&lt;br/&gt;line&lt;/p&gt;</description>
                                </item>
                                <item>
                                    <title>CDATA summary</title>
                                    <guid>html-item-2</guid>
                                    <link>https://example.test/cdata-story</link>
                                    <pubDate>Sat, 03 Oct 2026 13:00:00 GMT</pubDate>
                                    <description><![CDATA[<p>CDATA <em>content</em></p><p>Second paragraph</p>]]></description>
                                </item>
                            </channel>
                        </rss>
                        """;
                using var client = CreateClient(xml);
                var downloader = new SyndicationFeedDownloader(client);

                var items = await downloader.DownloadAsync(CreateFeed());

                Assert.AreEqual($"Read this & enjoy.{Environment.NewLine}Second{Environment.NewLine}line", items[0].Summary);
                Assert.AreEqual($"CDATA content{Environment.NewLine}Second paragraph", items[1].Summary);
        }

        [TestMethod]
        public async Task Download_RssReadsMediaThumbnailAndHtmlImage()
        {
                const string xml = """
                        <rss version="2.0" xmlns:media="http://search.yahoo.com/mrss/">
                            <channel>
                                <title>Sample feed</title>
                                <item>
                                    <title>Media image</title>
                                    <link>https://example.test/news/story/</link>
                                    <media:thumbnail url="images/cover.jpg" />
                                </item>
                                <item>
                                    <title>HTML image</title>
                                    <link>https://example.test/another-story</link>
                                    <description><![CDATA[<p>Story</p><img alt="Cover" src="/images/inline.jpg">]]></description>
                                </item>
                            </channel>
                        </rss>
                        """;
                using var client = CreateClient(xml);
                var downloader = new SyndicationFeedDownloader(client);

                var items = await downloader.DownloadAsync(CreateFeed() with { FeedUrl = "https://example.test/feeds/rss" });

                Assert.AreEqual("https://example.test/news/story/images/cover.jpg", items[0].ImageUrl);
                Assert.AreEqual("https://example.test/images/inline.jpg", items[1].ImageUrl);
        }

    [TestMethod]
    public async Task Download_RssDescriptionWithComicImageAndSourceLinkPreservesArticleData()
    {
        const string xml = """
            <rss version="2.0"><channel><title>Comics</title><item>
              <title>9 Chickweed Lane</title>
              <link>https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031</link>
              <description><![CDATA[<p><img src="https://resources.arcamax.com/newspics/396/39604/3960483.gif" alt="Comic"></p><p><a href="https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031">Source</a></p>]]></description>
            </item></channel></rss>
            """;
        using var client = CreateClient(xml);
        var downloader = new SyndicationFeedDownloader(client);

        var item = (await downloader.DownloadAsync(CreateFeed())).Single();

        Assert.AreEqual("Source", item.Summary);
        Assert.AreEqual("https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031", item.Link);
        Assert.AreEqual("https://resources.arcamax.com/newspics/396/39604/3960483.gif", item.ImageUrl);
    }

    [TestMethod]
    public async Task Download_ProhibitsDtdDeclarations()
    {
        const string xml = """
            <!DOCTYPE rss [<!ENTITY x "unsafe">]>
            <rss version="2.0"><channel><title>&x;</title></channel></rss>
            """;
        using var client = CreateClient(xml);
        var downloader = new SyndicationFeedDownloader(client);

        await Assert.ThrowsExceptionAsync<XmlException>(() => downloader.DownloadAsync(CreateFeed()));
    }

    [TestMethod]
    public async Task Download_RejectsMalformedXml()
    {
        const string xml = "<rss version=\"2.0\"><channel><item></channel></rss>";
        using var client = CreateClient(xml);
        var downloader = new SyndicationFeedDownloader(client);

        await Assert.ThrowsExceptionAsync<XmlException>(() => downloader.DownloadAsync(CreateFeed()));
    }

    [TestMethod]
    public async Task DownloadRejectsNonHttpFeedUrls()
    {
        using var client = CreateClient("<rss version=\"2.0\" />");
        var downloader = new SyndicationFeedDownloader(client);
        var invalidFeed = CreateFeed() with { FeedUrl = "file:///feed.xml" };

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => downloader.DownloadAsync(invalidFeed));
    }

    [TestMethod]
    public async Task DownloadPropagatesOfflineRequestFailure()
    {
        using var client = new HttpClient(new DelegateResponseHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("The feed is offline."))));
        var downloader = new SyndicationFeedDownloader(client);

        var failure = await Assert.ThrowsExceptionAsync<HttpRequestException>(
            () => downloader.DownloadAsync(CreateFeed()));

        StringAssert.Contains(failure.Message, "offline");
    }

    [TestMethod]
    public async Task DownloadHonorsHttpClientTimeout()
    {
        using var client = new HttpClient(new DelegateResponseHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The request should have timed out.");
        }))
        {
            Timeout = TimeSpan.FromMilliseconds(100)
        };
        var downloader = new SyndicationFeedDownloader(client);

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => downloader.DownloadAsync(CreateFeed()));
    }

    [TestMethod]
    public async Task DownloadHonorsCallerCancellation()
    {
        using var client = new HttpClient(new DelegateResponseHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The request should have been cancelled.");
        }));
        var downloader = new SyndicationFeedDownloader(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => downloader.DownloadAsync(CreateFeed(), cancellation.Token));
    }

    [TestMethod]
    public async Task DownloadPropagatesInterruptedResponseStream()
    {
        const string xml = "<rss version=\"2.0\"><channel><title>Interrupted response</title></channel></rss>";
        var content = new StreamContent(new InterruptedReadStream(System.Text.Encoding.UTF8.GetBytes(xml)));
        using var client = new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        }));
        var downloader = new SyndicationFeedDownloader(client);
        Exception? failure = null;

        try
        {
            await downloader.DownloadAsync(CreateFeed());
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Assert.IsTrue(failure is IOException or XmlException, $"Unexpected failure: {failure?.GetType().FullName ?? "none"}.");
    }

    [TestMethod]
    public async Task DownloadRejectsDeclaredOversizedResponse()
    {
        var content = new StringContent("<rss version=\"2.0\" />");
        content.Headers.ContentLength = 5_000_001;
        using var client = new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        }));
        var downloader = new SyndicationFeedDownloader(client);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => downloader.DownloadAsync(CreateFeed()));
    }

    [TestMethod]
    public async Task DownloadRawContentRejectsOversizedStreamWithoutContentLength()
    {
        var content = new StringContent(new string('x', 5_000_001));
        content.Headers.ContentLength = null;
        using var client = new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        }));
        var downloader = new SyndicationFeedDownloader(client);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => downloader.DownloadRawContentAsync(CreateFeed()));
    }

    private static CatalogFeed CreateFeed() =>
        new("feed-1", "Sample", "https://example.test/rss", null, null);

    private static HttpClient CreateClient(string content) =>
        new(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        }));

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class DelegateResponseHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => sendAsync(request, cancellationToken);
    }

    private sealed class InterruptedReadStream(byte[] content) : MemoryStream(content)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = base.Read(buffer, offset, Math.Min(count, 32));
            if (bytesRead == 0)
            {
                throw new IOException("The response stream ended unexpectedly.");
            }

            return bytesRead;
        }
    }
}