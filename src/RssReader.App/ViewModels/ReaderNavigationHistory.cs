namespace RssReader.App.ViewModels;

internal sealed class ReaderNavigationHistory
{
    private const int MaximumEntries = 100;
    private readonly LinkedList<NavigationHistoryEntry> _entries = new();

    public int Count => _entries.Count;

    public void Push(NavigationHistoryEntry entry)
    {
        _entries.AddLast(entry);
        if (_entries.Count > MaximumEntries)
        {
            _entries.RemoveFirst();
        }
    }

    public NavigationHistoryEntry? Pop()
    {
        if (_entries.Last is not { } previous)
        {
            return null;
        }

        _entries.RemoveLast();
        return previous.Value;
    }
}

internal sealed record NavigationHistoryEntry(
    string ActiveRoute,
    string? SelectedArticleId,
    ArticleRowViewModel? SelectedArticleReference,
    string SearchQuery,
    string? SelectedArticleTopicTerm,
    string? SelectedArticleTopicScheme,
    bool UnreadOnly,
    bool SavedOnly,
    bool IsCardsView,
    bool IsMagazineView,
    bool IsSortByDate,
    string CatalogSearchQuery,
    string? SelectedCatalogCategoryId,
    string? SelectedCatalogCollectionId,
    bool HideFollowedCatalogFeeds,
    string[] ExpandedFolderNames);
