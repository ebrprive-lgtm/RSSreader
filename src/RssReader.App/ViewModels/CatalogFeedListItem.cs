using System.Collections.ObjectModel;

namespace RssReader.App.ViewModels;

public sealed class CatalogFeedListItem : ObservableObject
{
    private bool _isSubscribed;
    private string? _folderName;
    private string _newTagName = string.Empty;

    public CatalogFeedListItem(string id, string name, string feedUrl, string? description, string? categoryName)
    {
        Id = id;
        Name = name;
        FeedUrl = feedUrl;
        Description = description;
        CategoryName = categoryName;
    }

    public string Id { get; }
    public string Name { get; }
    public string FeedUrl { get; }
    public string? Description { get; }
    public string? CategoryName { get; }
    public ObservableCollection<FeedTagListItem> Tags { get; } = [];

    public bool IsSubscribed
    {
        get => _isSubscribed;
        set
        {
            if (SetProperty(ref _isSubscribed, value))
            {
                OnPropertyChanged(nameof(SubscriptionLabel));
            }
        }
    }

    public string SubscriptionLabel => IsSubscribed ? "Unfollow" : "Follow";

    public string? FolderName
    {
        get => _folderName;
        set => SetProperty(ref _folderName, value);
    }

    public string NewTagName
    {
        get => _newTagName;
        set => SetProperty(ref _newTagName, value);
    }
}

public sealed record FeedTagListItem(string FeedId, string Name);