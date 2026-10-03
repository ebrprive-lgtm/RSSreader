using System.Xml;
using System.Xml.Linq;

namespace RssReader.Application;

public sealed record OpmlFeed(string Name, string FeedUrl, string? Description, string? CategoryName);

public sealed record OpmlFeedParseResult(IReadOnlyList<OpmlFeed> Feeds, int SkippedCount);

public static class OpmlFeedParser
{
    private const long MaximumDocumentCharacters = 5_000_000;

    public static OpmlFeedParseResult Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumDocumentCharacters,
            CloseInput = false
        };

        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "opml")
        {
            throw new InvalidDataException("The selected file is not an OPML document.");
        }

        var body = document.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "body");
        if (body is null)
        {
            throw new InvalidDataException("The OPML document has no body.");
        }

        var feeds = new List<OpmlFeed>();
        var skippedCount = 0;
        ReadOutlines(body, [], feeds, ref skippedCount);
        return new OpmlFeedParseResult(feeds, skippedCount);
    }

    private static void ReadOutlines(
        XElement parent,
        IReadOnlyList<string> folders,
        ICollection<OpmlFeed> feeds,
        ref int skippedCount)
    {
        foreach (var outline in parent.Elements().Where(element => element.Name.LocalName == "outline"))
        {
            var feedUrl = GetAttribute(outline, "xmlUrl");
            var type = GetAttribute(outline, "type");
            if (feedUrl is not null)
            {
                if (string.IsNullOrWhiteSpace(feedUrl))
                {
                    skippedCount++;
                }
                else
                {
                    var name = FirstNonEmpty(GetAttribute(outline, "title"), GetAttribute(outline, "text"))
                        ?? "Imported feed";
                    feeds.Add(new OpmlFeed(
                        name,
                        feedUrl.Trim(),
                        GetAttribute(outline, "description"),
                        folders.Count == 0 ? null : string.Join(" / ", folders)));
                }
            }
            else if (string.Equals(type, "rss", StringComparison.OrdinalIgnoreCase))
            {
                skippedCount++;
            }

            var childFolders = folders;
            if (feedUrl is null)
            {
                var folderName = FirstNonEmpty(GetAttribute(outline, "title"), GetAttribute(outline, "text"));
                if (folderName is not null)
                {
                    childFolders = folders.Append(folderName).ToArray();
                }
            }

            ReadOutlines(outline, childFolders, feeds, ref skippedCount);
        }
    }

    private static string? GetAttribute(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}