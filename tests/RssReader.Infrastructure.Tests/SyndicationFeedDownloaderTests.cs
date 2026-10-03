using System.Net;
using System.Xml;
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
    public async Task DownloadRejectsNonHttpFeedUrls()
    {
        using var client = CreateClient("<rss version=\"2.0\" />");
        var downloader = new SyndicationFeedDownloader(client);
        var invalidFeed = CreateFeed() with { FeedUrl = "file:///feed.xml" };

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => downloader.DownloadAsync(invalidFeed));
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
}