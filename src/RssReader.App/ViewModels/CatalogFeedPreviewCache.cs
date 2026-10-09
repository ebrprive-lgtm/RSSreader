using System.Diagnostics.CodeAnalysis;
using RssReader.Application;

namespace RssReader.App.ViewModels;

internal sealed class CatalogFeedPreviewCache
{
    private const int Capacity = 20;
    private readonly Dictionary<string, CatalogFeedPreview> _previews = new(StringComparer.Ordinal);
    private readonly Queue<string> _insertionOrder = new();

    public bool TryGetValue(string feedId, [NotNullWhen(true)] out CatalogFeedPreview? preview) =>
        _previews.TryGetValue(feedId, out preview);

    public void Store(string feedId, CatalogFeedPreview preview)
    {
        if (!_previews.ContainsKey(feedId))
        {
            if (_previews.Count >= Capacity)
            {
                var oldestFeedId = _insertionOrder.Dequeue();
                _previews.Remove(oldestFeedId);
            }

            _insertionOrder.Enqueue(feedId);
        }

        _previews[feedId] = preview;
    }
}
