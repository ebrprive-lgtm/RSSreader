using System.Collections.ObjectModel;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

public enum CatalogTaxonomyTreeNodeKind
{
    Category,
    Collection,
    Feed,
    Placeholder
}

public sealed class CatalogTaxonomyTreeNode : ObservableObject
{
    private bool _isExpanded;

    public CatalogTaxonomyTreeNode(
        CatalogTaxonomyTreeNodeKind kind,
        string id,
        string name,
        CatalogManagementViewModel management,
        CatalogCategory? category = null,
        CatalogCollection? collection = null,
        CatalogFeedListItem? feed = null,
        string? parentId = null)
    {
        Kind = kind;
        Id = id;
        Name = FeedTextEncodingRepair.Repair(name);
        Management = management;
        Category = category;
        Collection = collection;
        Feed = feed;
        ParentId = parentId;
    }

    public CatalogTaxonomyTreeNodeKind Kind { get; }
    public string Id { get; }
    public string Name { get; }
    public CatalogManagementViewModel Management { get; }
    public CatalogCategory? Category { get; }
    public CatalogCollection? Collection { get; }
    public CatalogFeedListItem? Feed { get; }
    public string? ParentId { get; }
    public ObservableCollection<CatalogTaxonomyTreeNode> Children { get; } = [];
    public bool IsChildrenLoaded { get; internal set; }
    public bool IsCategory => Kind == CatalogTaxonomyTreeNodeKind.Category;
    public bool IsManagedCategory => Category is not null;
    public bool IsCollection => Kind == CatalogTaxonomyTreeNodeKind.Collection;
    public bool IsFeedNode => Kind == CatalogTaxonomyTreeNodeKind.Feed;
    public bool IsCategoryFeed => Kind == CatalogTaxonomyTreeNodeKind.Feed && ParentId is null;
    public bool IsCollectionFeed => Kind == CatalogTaxonomyTreeNodeKind.Feed && ParentId is not null;
    public string CategoryAvailabilityActionLabel => Category?.IsEnabled == false ? "Enable" : "Disable";
    public string AvailabilityActionLabel => IsCategoryFeed
        ? Feed?.CategoryAvailabilityActionLabel ?? "Disable"
        : Feed?.GlobalAvailabilityActionLabel ?? "Disable globally";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}
