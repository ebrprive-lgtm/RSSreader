using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using HtmlAgilityPack;

namespace RssReader.App;

public partial class RawFeedWindow : Window
{
    private const string RssContentNamespace = "http://purl.org/rss/1.0/modules/content/";
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";
    private static readonly Regex HtmlBreakTag = new(@"<br\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly string _rawContent;

    public RawFeedWindow(string feedSource, string rawContent)
    {
        InitializeComponent();
        _rawContent = rawContent;
        FeedSourceText.Text = feedSource;
        RawContentTreeView.ItemsSource = CreateTree(rawContent);
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyXml_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_rawContent);
            CopyStatusText.Text = "XML copied to clipboard.";
        }
        catch (ExternalException)
        {
            CopyStatusText.Text = "Could not access the clipboard.";
        }
    }

    private void CopyTreeNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: RawXmlTreeNode node })
        {
            return;
        }

        try
        {
            Clipboard.SetText(node.GetCopyXml());
            CopyStatusText.Text = "XML node copied to clipboard.";
        }
        catch (ExternalException)
        {
            CopyStatusText.Text = "Could not access the clipboard.";
        }
    }

    private static IReadOnlyList<RawXmlTreeNode> CreateTree(string rawContent)
    {
        try
        {
            using var stringReader = new StringReader(rawContent);
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 5_000_000,
                MaxCharactersFromEntities = 0
            });
            var document = new XmlDocument { XmlResolver = null };
            document.Load(xmlReader);
            return document.DocumentElement is { } root
                ? [CreateNode(root)]
                : [new RawXmlTreeNode("The XML document has no root element.", [], isExpanded: true)];
        }
        catch (XmlException exception)
        {
            return
            [
                new RawXmlTreeNode(
                    $"XML could not be parsed: {exception.Message}",
                    [new RawXmlTreeNode(rawContent, [], copyXmlFactory: () => rawContent)],
                    isExpanded: true)
            ];
        }
    }

    private static RawXmlTreeNode CreateNode(XmlElement element)
    {
        var attributes = string.Concat(element.Attributes
            .Cast<XmlAttribute>()
            .Select(attribute => $" {attribute.Name}=\"{SecurityElement.Escape(attribute.Value)}\""));
        var openingTag = $"<{element.Name}{attributes}";
        var childNodes = element.ChildNodes.Cast<XmlNode>().ToArray();
        if (childNodes.Length == 0)
        {
            return new RawXmlTreeNode(
                $"{openingTag} />",
                [],
                isExpanded: true,
                copyXmlFactory: () => SerializeXmlNode(element));
        }

        if (element.LocalName == "encoded" &&
            element.NamespaceURI == RssContentNamespace &&
            childNodes.Length == 1 &&
            childNodes[0] is XmlCDataSection encodedContent)
        {
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(encodedContent.Value ?? string.Empty);
            if (htmlDocument.DocumentNode.ChildNodes.Any(node => node.NodeType == HtmlNodeType.Element))
            {
                var htmlChildren = htmlDocument.DocumentNode.ChildNodes
                    .Select(CreateHtmlNode)
                    .ToList();
                htmlChildren.Add(new RawXmlTreeNode(
                    $"</{element.Name}>",
                    [],
                    copyXmlFactory: () => SerializeXmlNode(element)));
                return new RawXmlTreeNode(
                    $"{openingTag}>",
                    htmlChildren,
                    isExpanded: true,
                    isEncoded: true,
                    copyXmlFactory: () => SerializeXmlNode(element));
            }
        }

        if (childNodes.All(node => node is XmlText or XmlCDataSection) &&
            !HtmlBreakTag.IsMatch(element.InnerText))
        {
            return new RawXmlTreeNode(
                $"{openingTag}>{element.InnerText}</{element.Name}>",
                [],
                isExpanded: true,
                copyXmlFactory: () => SerializeXmlNode(element));
        }

        var children = new List<RawXmlTreeNode>();
        foreach (var child in childNodes)
        {
            switch (child)
            {
                case XmlElement childElement:
                    children.Add(CreateNode(childElement));
                    break;
                case XmlCDataSection cdata:
                    if (HtmlBreakTag.IsMatch(cdata.Value ?? string.Empty))
                    {
                        AddTextNodes(children, cdata.Value);
                    }
                    else
                    {
                        children.Add(new RawXmlTreeNode(
                            $"<![CDATA[{cdata.Value}]]>",
                            [],
                            copyXmlFactory: () => SerializeXmlNode(cdata)));
                    }
                    break;
                case XmlText text when !string.IsNullOrWhiteSpace(text.Value):
                    AddTextNodes(children, text.Value);
                    break;
                case XmlComment comment:
                    children.Add(new RawXmlTreeNode(
                        $"<!-- {comment.Value} -->",
                        [],
                        copyXmlFactory: () => SerializeXmlNode(comment)));
                    break;
                case XmlProcessingInstruction instruction:
                    children.Add(new RawXmlTreeNode(
                        $"<?{instruction.Name} {instruction.Value}?>",
                        [],
                        copyXmlFactory: () => SerializeXmlNode(instruction)));
                    break;
            }
        }

        children.Add(new RawXmlTreeNode(
            $"</{element.Name}>",
            [],
            copyXmlFactory: () => SerializeXmlNode(element)));
        return new RawXmlTreeNode(
            $"{openingTag}>",
            children,
            isExpanded: true,
            copyXmlFactory: () => SerializeXmlNode(element));
    }

    private static RawXmlTreeNode CreateHtmlNode(HtmlNode node)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            return new RawXmlTreeNode(
                HtmlEntity.DeEntitize(node.InnerText).Trim(),
                [],
                copyXmlFactory: () => SerializeHtmlNode(node));
        }

        if (node.NodeType != HtmlNodeType.Element)
        {
            return new RawXmlTreeNode(node.OuterHtml, [], copyXmlFactory: () => SerializeHtmlNode(node));
        }

        var attributes = string.Concat(node.Attributes
            .Select(attribute => $" {attribute.Name}=\"{SecurityElement.Escape(attribute.Value)}\""));
        var openingTag = $"<{node.Name}{attributes}";
        if (node.ChildNodes.Count == 0)
        {
            return new RawXmlTreeNode(
                $"{openingTag} />",
                [],
                isExpanded: true,
                copyXmlFactory: () => SerializeHtmlNode(node));
        }

        if (node.ChildNodes.All(child => child.NodeType == HtmlNodeType.Text))
        {
            return new RawXmlTreeNode(
                $"{openingTag}>{node.InnerHtml}</{node.Name}>",
                [],
                isExpanded: true,
                copyXmlFactory: () => SerializeHtmlNode(node));
        }

        var children = node.ChildNodes.Select(CreateHtmlNode).ToList();
        children.Add(new RawXmlTreeNode(
            $"</{node.Name}>",
            [],
            copyXmlFactory: () => SerializeHtmlNode(node)));
        return new RawXmlTreeNode(
            $"{openingTag}>",
            children,
            isExpanded: true,
            copyXmlFactory: () => SerializeHtmlNode(node));
    }

    private static void AddTextNodes(List<RawXmlTreeNode> children, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var startIndex = 0;
        foreach (Match breakTag in HtmlBreakTag.Matches(value))
        {
            AddTextNode(children, value[startIndex..breakTag.Index]);
            children.Add(new RawXmlTreeNode(
                breakTag.Value,
                [],
                copyXmlFactory: () => SerializeTextFragment(breakTag.Value)));
            startIndex = breakTag.Index + breakTag.Length;
        }

        AddTextNode(children, value[startIndex..]);
    }

    private static void AddTextNode(List<RawXmlTreeNode> children, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var text = value.Trim();
            children.Add(new RawXmlTreeNode(text, [], copyXmlFactory: () => SerializeTextFragment(text)));
        }
    }

    private static string SerializeXmlNode(XmlNode node)
    {
        if (node is XmlElement element)
        {
            node = CreateStandaloneElement(element);
        }

        return SerializeXmlFragment(writer => node.WriteTo(writer));
    }

    private static XmlElement CreateStandaloneElement(XmlElement element)
    {
        var clone = (XmlElement)element.CloneNode(deep: true);
        var namespaceDeclarations = new Dictionary<string, string>(StringComparer.Ordinal);
        XmlNode? current = element;
        while (current is XmlElement scope)
        {
            foreach (XmlAttribute attribute in scope.Attributes)
            {
                if (attribute.NamespaceURI != XmlnsNamespace)
                {
                    continue;
                }

                var prefix = attribute.Prefix == "xmlns" ? attribute.LocalName : string.Empty;
                if (prefix != "xml")
                {
                    namespaceDeclarations.TryAdd(prefix, attribute.Value);
                }
            }

            current = scope.ParentNode;
        }

        foreach (var (prefix, namespaceUri) in namespaceDeclarations)
        {
            var qualifiedName = prefix.Length == 0 ? "xmlns" : $"xmlns:{prefix}";
            if (clone.GetAttributeNode(qualifiedName) is not null)
            {
                continue;
            }

            var declaration = prefix.Length == 0
                ? clone.OwnerDocument!.CreateAttribute(string.Empty, "xmlns", XmlnsNamespace)
                : clone.OwnerDocument!.CreateAttribute("xmlns", prefix, XmlnsNamespace);
            declaration.Value = namespaceUri;
            clone.Attributes.Append(declaration);
        }

        return clone;
    }

    private static string SerializeXmlFragment(Action<XmlWriter> write)
    {
        using var stringWriter = new StringWriter();
        using (var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            OmitXmlDeclaration = true
        }))
        {
            write(xmlWriter);
        }

        return stringWriter.ToString();
    }

    private static string SerializeTextFragment(string text) =>
        SerializeXmlFragment(writer => writer.WriteString(text));

    private static string SerializeHtmlNode(HtmlNode node) =>
        SerializeXmlFragment(writer => WriteHtmlNode(writer, node));

    private static void WriteHtmlNode(XmlWriter writer, HtmlNode node)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                writer.WriteString(HtmlEntity.DeEntitize(node.InnerText));
                break;
            case HtmlNodeType.Comment:
                writer.WriteComment(node.InnerHtml.Replace("--", "- -", StringComparison.Ordinal));
                break;
            case HtmlNodeType.Element:
                writer.WriteStartElement(XmlConvert.EncodeLocalName(node.Name));
                foreach (var attribute in node.Attributes)
                {
                    writer.WriteAttributeString(
                        XmlConvert.EncodeLocalName(attribute.Name),
                        HtmlEntity.DeEntitize(attribute.Value));
                }

                foreach (var child in node.ChildNodes)
                {
                    WriteHtmlNode(writer, child);
                }

                writer.WriteEndElement();
                break;
            default:
                writer.WriteString(HtmlEntity.DeEntitize(node.InnerText));
                break;
        }
    }
}

public sealed class RawXmlTreeNode(
    string displayText,
    IReadOnlyList<RawXmlTreeNode> children,
    bool isExpanded = false,
    bool isEncoded = false,
    Func<string>? copyXmlFactory = null)
{
    public string DisplayText { get; } = displayText;
    public IReadOnlyList<RawXmlTreeNode> Children { get; } = children;
    public bool IsExpanded { get; set; } = isExpanded;
    public bool IsEncoded { get; } = isEncoded;
    public string GetCopyXml() => copyXmlFactory?.Invoke() ?? DisplayText;
}