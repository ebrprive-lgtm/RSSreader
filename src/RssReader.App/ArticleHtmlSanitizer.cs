using Ganss.Xss;

namespace RssReader.App;

public static class ArticleHtmlSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static string SanitizeFragment(string html, string? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(html);

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && IsWebUri(baseUri))
        {
            return Sanitizer.Sanitize(html, baseUri.AbsoluteUri);
        }

        return Sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(
        [
            "a", "abbr", "address", "article", "aside", "b", "blockquote", "br", "caption", "cite",
            "code", "dd", "del", "div", "dl", "dt", "em", "figcaption", "figure", "footer", "h1",
            "h2", "h3", "h4", "h5", "h6", "header", "hr", "i", "img", "ins", "kbd", "li", "main",
            "ol", "p", "pre", "q", "s", "samp", "section", "small", "span", "strong", "sub", "sup",
            "table", "tbody", "td", "tfoot", "th", "thead", "time", "tr", "u", "ul", "wbr"
        ]);

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(
        [
            "alt", "class", "colspan", "datetime", "height", "href", "rowspan", "scope", "src", "title", "width"
        ]);

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith([Uri.UriSchemeHttp, Uri.UriSchemeHttps]);
        sanitizer.UriAttributes.Clear();
        sanitizer.UriAttributes.UnionWith(["href", "src"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();
        return sanitizer;
    }

    private static bool IsWebUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}