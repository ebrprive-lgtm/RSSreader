using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace RssReader.Infrastructure;

internal static class HtmlTextParser
{
    private static readonly HashSet<string> BlockElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "blockquote", "div", "dl", "fieldset", "figcaption", "figure",
        "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li",
        "main", "ol", "p", "pre", "section", "table", "tr", "ul"
    };

    public static string? ToPlainText(string? html)
    {
        if (html is null)
        {
            return null;
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var text = new StringBuilder();
        AppendNode(document.DocumentNode, text);

        var normalized = Regex.Replace(text.ToString(), @"[ \t\f\v]+", " ");
        normalized = Regex.Replace(normalized, @" *\r?\n *", Environment.NewLine);
        normalized = Regex.Replace(normalized, @"(?:\r?\n){3,}", Environment.NewLine + Environment.NewLine);
        return normalized.Trim();
    }

    private static void AppendNode(HtmlNode node, StringBuilder text)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            text.Append(HtmlEntity.DeEntitize(((HtmlTextNode)node).Text));
            return;
        }

        if (string.Equals(node.Name, "script", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.Name, "style", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(node.Name, "br", StringComparison.OrdinalIgnoreCase))
        {
            AppendLineBreak(text);
            return;
        }

        var isBlockElement = BlockElementNames.Contains(node.Name);
        if (isBlockElement)
        {
            AppendLineBreak(text);
        }

        foreach (var child in node.ChildNodes)
        {
            AppendNode(child, text);
        }

        if (isBlockElement)
        {
            AppendLineBreak(text);
        }
    }

    private static void AppendLineBreak(StringBuilder text)
    {
        if (text.Length > 0 && text[^1] is not ('\r' or '\n'))
        {
            text.AppendLine();
        }
    }
}