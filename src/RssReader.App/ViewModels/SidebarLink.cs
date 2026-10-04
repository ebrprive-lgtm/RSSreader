using System.Windows;

namespace RssReader.App.ViewModels;

public sealed class SidebarLink(
    string route,
    string label,
    string glyph,
    string? count = null,
    int indentLevel = 0,
    SidebarLink? parentFolder = null)
    : ObservableObject
{
    private bool _isSelected;
    private bool _isExpanded;

    public string Route { get; } = route;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    public bool IsFolder => Route.StartsWith("folder:", StringComparison.Ordinal);
    public bool IsFeedEntry => ParentFolder is not null;
    public SidebarLink? ParentFolder { get; } = parentFolder;
    public string? Count { get; } = count;
    public Thickness IndentMargin { get; } = new(indentLevel * 14, 0, 0, 0);

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}