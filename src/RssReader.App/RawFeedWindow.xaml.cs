using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Xml;
using System.Windows;

namespace RssReader.App;

public partial class RawFeedWindow : Window
{
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
                    [new RawXmlTreeNode(rawContent, [])],
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
            return new RawXmlTreeNode($"{openingTag} />", [], isExpanded: true);
        }

        if (childNodes.All(node => node is XmlText or XmlCDataSection))
        {
            var content = SecurityElement.Escape(element.InnerText) ?? string.Empty;
            return new RawXmlTreeNode($"{openingTag}>{content}</{element.Name}>", [], isExpanded: true);
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
                    children.Add(new RawXmlTreeNode($"<![CDATA[{cdata.Value}]]>", []));
                    break;
                case XmlText text when !string.IsNullOrWhiteSpace(text.Value):
                    children.Add(new RawXmlTreeNode(SecurityElement.Escape(text.Value.Trim()) ?? string.Empty, []));
                    break;
                case XmlComment comment:
                    children.Add(new RawXmlTreeNode($"<!-- {comment.Value} -->", []));
                    break;
                case XmlProcessingInstruction instruction:
                    children.Add(new RawXmlTreeNode($"<?{instruction.Name} {instruction.Value}?>", []));
                    break;
            }
        }

        return new RawXmlTreeNode($"{openingTag}>", children, isExpanded: true);
    }
}

public sealed class RawXmlTreeNode(
    string displayText,
    IReadOnlyList<RawXmlTreeNode> children,
    bool isExpanded = false)
{
    public string DisplayText { get; } = displayText;
    public IReadOnlyList<RawXmlTreeNode> Children { get; } = children;
    public bool IsExpanded { get; set; } = isExpanded;
}