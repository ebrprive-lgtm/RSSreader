using System.Net;
using HtmlAgilityPack;

namespace RssReader.App;

internal static class ArticleHtmlDocumentBuilder
{
    private const string ReaderStyles = """
        :root { color-scheme: light; }
        html, body { margin: 0; padding: 0; background: #fff; }
        body { color: #202923; font: 16px/1.65 "Segoe UI", sans-serif; overflow-wrap: anywhere; }
        main { max-width: 860px; margin: 0 auto; padding: 12px 0 24px; }
        img { max-width: 100%; height: auto; vertical-align: middle; }
        figure { margin: 16px 0; text-align: center; }
        figcaption { color: #5d6a62; font-size: 0.9em; margin-top: 6px; }
        a { color: #176b9a; text-decoration: underline; }
        blockquote { border-left: 3px solid #a8b8ad; color: #4d5a52; margin: 1em 0; padding: 2px 0 2px 14px; }
        table { border-collapse: collapse; display: block; max-width: 100%; overflow-x: auto; }
        th, td { border: 1px solid #cbd5ce; padding: 6px 9px; text-align: left; vertical-align: top; }
        pre { background: #f1f4f2; border-radius: 3px; overflow-wrap: anywhere; padding: 10px; white-space: pre-wrap; }
        code, kbd, samp { font-family: Consolas, monospace; }
        hr { border: 0; border-top: 1px solid #dfe6e1; margin: 1.5em 0; }
        """;

    public static string Build(
        string? content,
        string? summary,
        string? imageUrl,
        string? articleUrl,
        string? feedUrl)
    {
        var baseUrl = GetWebUrl(articleUrl) ?? GetWebUrl(feedUrl);
        var bodyFragment = string.IsNullOrWhiteSpace(content)
            ? $"<p>{EncodePlainText(summary)}</p>"
            : content;
        var sanitizedFragment = ArticleHtmlSanitizer.SanitizeFragment(bodyFragment, baseUrl);

        if (GetWebUrl(imageUrl) is { } leadImageUrl && !ContainsWebImage(sanitizedFragment))
        {
            var imageMarkup = $"<figure><img src=\"{WebUtility.HtmlEncode(leadImageUrl)}\" alt=\"Article image\"></figure>";
            sanitizedFragment = ArticleHtmlSanitizer.SanitizeFragment(imageMarkup + bodyFragment, baseUrl);
        }

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src http: https:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
              <style>{ReaderStyles}</style>
            </head>
            <body><main>{sanitizedFragment}</main></body>
            </html>
            """;
    }

    private static string EncodePlainText(string? text) =>
        WebUtility.HtmlEncode(text ?? string.Empty)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

    private static string? GetWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsWebUri(uri)
            ? uri.AbsoluteUri
            : null;

    private static bool ContainsWebImage(string fragment)
    {
        var document = new HtmlDocument();
        document.LoadHtml(fragment);
        var images = document.DocumentNode.SelectNodes("//img[@src]");
        return images?.Any(image =>
            Uri.TryCreate(image.GetAttributeValue("src", string.Empty), UriKind.Absolute, out var uri) && IsWebUri(uri)) == true;
    }

    private static bool IsWebUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}