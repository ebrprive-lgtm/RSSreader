using RssReader.Domain;

namespace RssReader.App.ViewModels;

internal sealed class SidebarFeedNavigation
{
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string[]> _feedIdsByFolder = new(StringComparer.OrdinalIgnoreCase);

    public SidebarFeedNavigation(bool useSampleData)
    {
        FeedLinks = new BulkObservableCollection<SidebarLink>();
        FeedLinks.ReplaceAll(useSampleData
            ?
            [
                new SidebarLink("All", "All", "\uE8A5", "5"),
                new SidebarLink("folder:Gaming", "Gaming", string.Empty, "(2)", indentLevel: 1),
                new SidebarLink("folder:tech", "tech", string.Empty, "(3)", indentLevel: 1)
            ]
            : [new SidebarLink("All", "All", "\uE8A5")]);
    }

    public BulkObservableCollection<SidebarLink> FeedLinks { get; }

    public IReadOnlyList<string> ExpandedFolderNames =>
        _expandedFolders.OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase).ToArray();

    public void ToggleFolder(SidebarLink folderLink)
    {
        folderLink.IsExpanded = !folderLink.IsExpanded;
        if (folderLink.IsExpanded)
        {
            _expandedFolders.Add(folderLink.Label);
        }
        else
        {
            _expandedFolders.Remove(folderLink.Label);
        }
    }

    public void RestoreExpandedFolders(IEnumerable<string> folderNames)
    {
        _expandedFolders.Clear();
        foreach (var folder in folderNames)
        {
            _expandedFolders.Add(folder);
        }

        foreach (var folderLink in FeedLinks.Where(link => link.IsFolder))
        {
            folderLink.IsExpanded = _expandedFolders.Contains(folderLink.Label);
        }
    }

    public string[]? FindFolderFeedIds(string folderName) =>
        _feedIdsByFolder.TryGetValue(folderName, out var feedIds) ? feedIds : null;

    public void Update(
        IReadOnlyList<string> folders,
        IReadOnlyList<ProfileSubscription> subscriptions)
    {
        _feedIdsByFolder = folders.ToDictionary(
            folder => folder,
            folder => subscriptions
                .Where(subscription => string.Equals(
                    subscription.FolderName,
                    folder,
                    StringComparison.OrdinalIgnoreCase))
                .Select(subscription => subscription.FeedId)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        var feedLinks = new List<SidebarLink> { new("All", "All", "\uE8A5") };
        _expandedFolders.IntersectWith(folders);
        foreach (var folder in folders)
        {
            var folderSubscriptions = subscriptions.Where(item =>
                    string.Equals(item.FolderName, folder, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var folderLink = new SidebarLink(
                $"folder:{folder}",
                folder,
                string.Empty,
                $"({folderSubscriptions.Length})",
                indentLevel: 1,
                parentFolder: null)
            {
                IsExpanded = _expandedFolders.Contains(folder)
            };
            feedLinks.Add(folderLink);
            foreach (var subscription in folderSubscriptions)
            {
                feedLinks.Add(new SidebarLink(
                    $"feed:{subscription.FeedId}",
                    subscription.FeedName,
                    "\uE774",
                    indentLevel: 2,
                    parentFolder: folderLink));
            }
        }

        FeedLinks.ReplaceAll(feedLinks);
    }
}
