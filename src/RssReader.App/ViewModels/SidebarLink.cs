namespace RssReader.App.ViewModels;

public sealed class SidebarLink(string route, string label, string glyph, string? count = null)
    : ObservableObject
{
    private bool _isSelected;

    public string Route { get; } = route;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    public string? Count { get; } = count;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}