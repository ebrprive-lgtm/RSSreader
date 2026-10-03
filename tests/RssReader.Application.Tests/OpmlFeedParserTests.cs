using System.Text;
using System.Xml;
using RssReader.Application;

namespace RssReader.Application.Tests;

[TestClass]
public sealed class OpmlFeedParserTests
{
    [TestMethod]
    public void ParseReadsNestedFoldersAndSkipsMalformedFeedOutlines()
    {
        const string opml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <opml version="2.0">
              <head><title>My feeds</title></head>
              <body>
                <outline text="Technology">
                  <outline title="The Verge" text="Verge" type="rss"
                           xmlUrl="https://example.com/verge.xml" htmlUrl="https://example.com"
                           description="Technology news" />
                  <outline text="Software">
                    <outline text="GitHub Blog" xmlUrl="https://example.com/github.xml" />
                  </outline>
                </outline>
                <outline text="Broken" type="rss" />
                <outline text="Empty URL" xmlUrl="  " />
              </body>
            </opml>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(opml));

        var result = OpmlFeedParser.Parse(stream);

        Assert.AreEqual(2, result.Feeds.Count);
        Assert.AreEqual("The Verge", result.Feeds[0].Name);
        Assert.AreEqual("https://example.com/verge.xml", result.Feeds[0].FeedUrl);
        Assert.AreEqual("Technology", result.Feeds[0].CategoryName);
        Assert.AreEqual("Technology news", result.Feeds[0].Description);
        Assert.AreEqual("https://example.com", result.Feeds[0].WebsiteUrl);
        Assert.AreEqual("GitHub Blog", result.Feeds[1].Name);
        Assert.IsNull(result.Feeds[1].WebsiteUrl);
        Assert.AreEqual("Technology / Software", result.Feeds[1].CategoryName);
        Assert.AreEqual(2, result.SkippedCount);
    }

    [TestMethod]
    public void ParseRejectsDocumentsWithDocumentTypeDeclarations()
    {
        const string opml = "<!DOCTYPE opml [<!ENTITY x SYSTEM 'file:///c:/windows/win.ini'>]><opml><body><outline text='&x;'/></body></opml>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(opml));

        Assert.ThrowsException<XmlException>(() => OpmlFeedParser.Parse(stream));
    }
}