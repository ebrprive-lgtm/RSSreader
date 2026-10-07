using System.Net;
using HtmlAgilityPack;

namespace RssReader.App;

internal static class ArticleHtmlDocumentBuilder
{
    private const string ReaderStyles = """
        :root { color-scheme: light; }
        html, body { margin: 0; padding: 0; background: #fff; }
        body { color: #202923; font: 16px/1.65 "Segoe UI", sans-serif; overflow-wrap: anywhere; }
        main { box-sizing: border-box; width: 100%; padding: 12px 12px 24px; }
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
        .reader-feed-preview { max-width: 760px; margin: 0 auto; }
        .reader-feed-preview-image { margin: 0 0 20px; }
        .reader-feed-preview-image img { width: 100%; max-height: 360px; object-fit: cover; border-radius: 12px; }
        .reader-feed-preview-summary { font-size: 1.12em; }
        .reader-feed-preview-note { background: #f1f4f2; border-left: 3px solid #86a58e; border-radius: 4px; color: #4d5a52; margin-top: 22px; padding: 12px 16px; }
        .reader-feed-preview-link { font-weight: 600; }
        """;

    public static string Build(
        string? content,
        string? summary,
        string? articleUrl,
        string? feedUrl,
        bool limitArticleWidth = true,
        string? imageUrl = null)
    {
        var baseUrl = GetWebUrl(articleUrl) ?? GetWebUrl(feedUrl);
        var isFeedPreview = IsFeedPreview(content, summary);
        var bodyFragment = string.IsNullOrWhiteSpace(content)
            ? $"<p>{EncodePlainText(summary)}</p>"
            : content;
        bodyFragment = RemoveDuplicateLeadingImage(bodyFragment, baseUrl);
        bodyFragment = PreserveCenteredImageAlignment(bodyFragment);
        var widthLimitStyle = limitArticleWidth
            ? "main { max-width: 900px; margin: 0 auto; }"
            : "main { max-width: none; margin: 0; }";

        var sanitizedFragment = ArticleHtmlSanitizer.SanitizeFragment(bodyFragment, baseUrl);
        if (isFeedPreview)
        {
            sanitizedFragment = BuildFeedPreview(sanitizedFragment, imageUrl, articleUrl, baseUrl);
        }

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src http: https:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
              <style>{ReaderStyles}{widthLimitStyle}</style>
            </head>
            <body><main>{sanitizedFragment}</main></body>
            </html>
            """;
    }

    internal static string? FindFirstWebImageUrl(
        string? content,
        string? articleUrl,
        string? feedUrl)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var baseUrl = GetWebUrl(articleUrl) ?? GetWebUrl(feedUrl);
        var document = new HtmlDocument();
        document.LoadHtml(content);
        var images = document.DocumentNode.SelectNodes("//img[@src or @srcset]");
        if (images is null)
        {
            return null;
        }

        foreach (var image in images)
        {
            foreach (var source in GetImageSources(image, baseUrl))
            {
                if (Uri.TryCreate(source, UriKind.Absolute, out var imageUri) && IsWebUri(imageUri))
                {
                    return imageUri.AbsoluteUri;
                }
            }
        }

        return null;
    }

    private static string EncodePlainText(string? text) =>
        WebUtility.HtmlEncode(text ?? string.Empty)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

    private static bool IsFeedPreview(string? content, string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        var document = new HtmlDocument();
        document.LoadHtml(content);
        if (document.DocumentNode.SelectSingleNode("//img|//audio|//video|//table|//blockquote|//pre") is not null)
        {
            return false;
        }

        var contentText = NormalizeText(document.DocumentNode.InnerText);
        return contentText.Length > 0 &&
            string.Equals(contentText, NormalizeText(summary), StringComparison.Ordinal);
    }

    private static string BuildFeedPreview(
        string sanitizedContent,
        string? imageUrl,
        string? articleUrl,
        string? baseUrl)
    {
        var document = new HtmlDocument();
        document.LoadHtml(sanitizedContent);
        var hasInlineImage = document.DocumentNode.SelectSingleNode("//img") is not null;
        var resolvedImageUrl = hasInlineImage || string.IsNullOrWhiteSpace(imageUrl)
            ? null
            : GetWebUrl(NormalizeImageSource(imageUrl, baseUrl));
        var image = resolvedImageUrl is null
            ? string.Empty
            : $"""
                <figure class="reader-feed-preview-image">
                  <img src="{WebUtility.HtmlEncode(resolvedImageUrl)}" alt="Article image">
                </figure>
                """;
        var articleUri = GetWebUrl(articleUrl);
        var link = articleUri is null
            ? string.Empty
            : $"""
                <p><a class="reader-feed-preview-link" href="{WebUtility.HtmlEncode(articleUri)}">Open article on the publisher's site</a></p>
                """;

        return $"""
            <section class="reader-feed-preview">
              {image}
              <div class="reader-feed-preview-summary">{sanitizedContent}</div>
              <aside class="reader-feed-preview-note">
                <p><strong>Feed preview</strong></p>
                <p>This feed provides a short preview. The publisher may have more to read.</p>
                {link}
              </aside>
            </section>
            """;
    }

    private static string NormalizeText(string text) =>
        string.Join(' ', WebUtility.HtmlDecode(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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