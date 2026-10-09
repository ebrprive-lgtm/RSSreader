using System.Xml.Linq;

namespace RssReader.Infrastructure;

internal static class RawFeedArticleSelector
{
    public static XElement? Select(
        XElement feedRoot,
        string? externalId,
        string? link,
        string title,
        Uri baseUri)
    {
        var articles = feedRoot.Descendants().Where(IsArticleElement).ToArray();
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

        return selectedArticle;
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
        Uri.TryCreate(baseUri, link.Trim(), out var resolved) &&
        resolved is not null &&
        SyndicationFeedParser.IsWebUri(resolved)
            ? resolved.AbsoluteUri
            : link.Trim();
}
