using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using RssReader.App.ViewModels;

namespace RssReader.App;

internal sealed class WindowPlacementStore
{
    private readonly string _filePath;

    public WindowPlacementStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RssReader",
            "main-window-placement.json"))
    {
    }

    internal WindowPlacementStore(string filePath)
    {
        _filePath = filePath;
    }

    public WindowPlacement? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_filePath));
        }
        catch (JsonException exception)
        {
            Trace.TraceWarning($"Ignoring invalid main-window placement at '{_filePath}': {exception.Message}");
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The window-placement path must include a directory.");
        Directory.CreateDirectory(directory);

        var temporaryFilePath = $"{_filePath}.tmp";
        File.WriteAllText(temporaryFilePath, JsonSerializer.Serialize(placement));
        File.Move(temporaryFilePath, _filePath, overwrite: true);
    }
}

internal sealed record WindowPlacement(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized,
    double? SidebarWidth = null)
{
    public bool TryGetVisibleBounds(Rect visibleArea, double minimumWidth, double minimumHeight, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (visibleArea.IsEmpty ||
            !double.IsFinite(visibleArea.Left) ||
            !double.IsFinite(visibleArea.Top) ||
            !double.IsFinite(visibleArea.Width) ||
            !double.IsFinite(visibleArea.Height) ||
            !double.IsFinite(Left) ||
            !double.IsFinite(Top) ||
            !double.IsFinite(Width) ||
            !double.IsFinite(Height) ||
            Width <= 0 ||
            Height <= 0 ||
            minimumWidth <= 0 ||
            minimumHeight <= 0 ||
            visibleArea.Width < minimumWidth ||
            visibleArea.Height < minimumHeight)
        {
            return false;
        }

        var width = Math.Clamp(Width, minimumWidth, visibleArea.Width);
        var height = Math.Clamp(Height, minimumHeight, visibleArea.Height);
        var left = Math.Clamp(Left, visibleArea.Left, visibleArea.Right - width);
        var top = Math.Clamp(Top, visibleArea.Top, visibleArea.Bottom - height);
        bounds = new Rect(left, top, width, height);
        return true;
    }
}
