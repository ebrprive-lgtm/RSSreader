namespace RssReader.App.ViewModels;

public sealed class CatalogFeedListItem : ObservableObject
{
    private bool _isSubscribed;

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
}
