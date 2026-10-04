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
        img.reader-centered-image { display: block; margin: 16px auto; }
        a.reader-centered-image-link { clear: both; display: block; text-align: center; }
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
        string? articleUrl,
        string? feedUrl)
    {
        var baseUrl = GetWebUrl(articleUrl) ?? GetWebUrl(feedUrl);
        var bodyFragment = string.IsNullOrWhiteSpace(content)
            ? $"<p>{EncodePlainText(summary)}</p>"
            : content;
        bodyFragment = RemoveDuplicateLeadingImage(bodyFragment, baseUrl);
        bodyFragment = PreserveCenteredImageAlignment(bodyFragment);
        var sanitizedFragment = ArticleHtmlSanitizer.SanitizeFragment(bodyFragment, baseUrl);

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

    private static string RemoveDuplicateLeadingImage(string fragment, string? baseUrl)
    {
        var document = new HtmlDocument();
        document.LoadHtml(fragment);
        var images = document.DocumentNode.SelectNodes("//img[@src or @srcset]");
        if (images is null || images.Count < 2)
        {
            return fragment;
        }

        var leadingImageSources = GetImageSources(images[0], baseUrl).ToHashSet(StringComparer.Ordinal);
        if (leadingImageSources.Count == 0 || !images.Skip(1).Any(image =>
                GetImageSources(image, baseUrl).Any(leadingImageSources.Contains)))
        {
            return fragment;
        }

        images[0].Remove();
        return document.DocumentNode.InnerHtml;
    }

    private static string PreserveCenteredImageAlignment(string fragment)
    {
        var document = new HtmlDocument();
        document.LoadHtml(fragment);
        var centeredImages = document.DocumentNode.SelectNodes("//img[@class]")?
            .Where(image => image.GetAttributeValue("class", string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Contains("aligncenter", StringComparer.OrdinalIgnoreCase))
            .ToArray() ?? [];

        foreach (var element in document.DocumentNode.DescendantsAndSelf())
        {
            element.Attributes.Remove("class");
        }

        foreach (var image in centeredImages)
        {
            image.Attributes.Add("class", "reader-centered-image");
            if (image.ParentNode.Name.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                image.ParentNode.Attributes.Add("class", "reader-centered-image-link");
            }
        }

        return document.DocumentNode.InnerHtml;
    }

    private static IEnumerable<string> GetImageSources(HtmlNode image, string? baseUrl)
    {
        var source = image.GetAttributeValue("src", string.Empty);
        if (!string.IsNullOrWhiteSpace(source))
        {
            yield return NormalizeImageSource(source, baseUrl);
        }

        var sourceSet = image.GetAttributeValue("srcset", string.Empty);
        if (string.IsNullOrWhiteSpace(sourceSet))
        {
            yield break;
        }

        foreach (var candidate in sourceSet.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidateSource = candidate.Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(candidateSource))
            {
                yield return NormalizeImageSource(candidateSource, baseUrl);
            }
        }
    }

    private static string NormalizeImageSource(string source, string? baseUrl)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.AbsoluteUri;
        }

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
               Uri.TryCreate(baseUri, source, out var resolvedUri)
            ? resolvedUri.AbsoluteUri
            : source.Trim();
    }

    private static bool IsWebUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}