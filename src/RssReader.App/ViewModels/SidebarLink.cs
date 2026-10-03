using System.Windows;

namespace RssReader.App.ViewModels;

public sealed class SidebarLink(
    string route,
    string label,
    string glyph,
    string? count = null,
    int indentLevel = 0)
    : ObservableObject
{
    private bool _isSelected;

    public string Route { get; } = route;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    public string? Count { get; } = count;
    public Thickness IndentMargin { get; } = new(indentLevel * 14, 0, 0, 0);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}