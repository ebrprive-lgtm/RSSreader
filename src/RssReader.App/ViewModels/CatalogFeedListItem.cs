namespace RssReader.App.ViewModels;

public sealed class CatalogFeedListItem : ObservableObject
{
    private bool _isSubscribed;
    private bool _isSelectedForFollow;
    private bool _isHealthCheckInProgress;

    public CatalogFeedListItem(
        string id,
        string name,
        string feedUrl,
        string? description,
        string? categoryName,
        string? categoryId = null,
        string? websiteUrl = null,
        int sameNameCount = 1,
        DateTimeOffset? lastHealthCheckedAt = null,
        bool? lastHealthCheckSucceeded = null)
    {
        Id = id;
        Name = name;
        FeedUrl = feedUrl;
        Description = description;
        CategoryName = categoryName;
        CategoryId = categoryId;
        WebsiteUrl = websiteUrl;
        SameNameCount = sameNameCount;
        LastHealthCheckedAt = lastHealthCheckedAt;
        LastHealthCheckSucceeded = lastHealthCheckSucceeded;

        var metadataGaps = new List<string>();
        if (string.IsNullOrWhiteSpace(Description))
        {
            metadataGaps.Add("description");
        }

        if (string.IsNullOrWhiteSpace(CategoryName))
        {
            metadataGaps.Add("category");
        }

        if (string.IsNullOrWhiteSpace(WebsiteUrl))
        {
            metadataGaps.Add("publisher website");
        }

        MetadataReviewDisplay = metadataGaps.Count == 0
            ? string.Empty
            : $"Metadata gaps: {string.Join(", ", metadataGaps)}";
    }

    public string Id { get; }
    public string Name { get; }
    public string FeedUrl { get; }
    public string? Description { get; }
    public string? CategoryName { get; }
    public string? CategoryId { get; }
    public string? WebsiteUrl { get; }
    public int SameNameCount { get; }
    public DateTimeOffset? LastHealthCheckedAt { get; }
    public bool? LastHealthCheckSucceeded { get; }
    public string SameNameDisplay => SameNameCount > 1 ? $"{SameNameCount} feeds share this name" : string.Empty;
    public string MetadataReviewDisplay { get; }
    public bool HasMetadataGaps => MetadataReviewDisplay.Length > 0;
    public string PublisherWebsiteDisplay =>
        string.IsNullOrWhiteSpace(WebsiteUrl) ? string.Empty : $"Publisher website: {WebsiteUrl}";
    public string HealthCheckDisplay => IsHealthCheckInProgress
        ? "Checking feed..."
        : LastHealthCheckedAt is not { } checkedAt
            ? "Not checked"
            : $"{(LastHealthCheckSucceeded == true ? "Feed valid" : "Check failed")} - checked {checkedAt.ToLocalTime():g}";

    public bool IsHealthCheckInProgress
    {
        get => _isHealthCheckInProgress;
        set
        {
            if (SetProperty(ref _isHealthCheckInProgress, value))
            {
                OnPropertyChanged(nameof(HealthCheckDisplay));
            }
        }
    }

    public bool IsSubscribed
    {
        get => _isSubscribed;
        set
        {
            if (SetProperty(ref _isSubscribed, value))
            {
                if (value)
                {
                    IsSelectedForFollow = false;
                }

                OnPropertyChanged(nameof(CanSelectForFollow));
                OnPropertyChanged(nameof(SubscriptionLabel));
            }
        }
    }

    public bool CanSelectForFollow => !IsSubscribed;

    public bool IsSelectedForFollow
    {
        get => _isSelectedForFollow;
        set => SetProperty(ref _isSelectedForFollow, value && CanSelectForFollow);
    }

    public string SubscriptionLabel => IsSubscribed ? "Unfollow" : "Follow";
}
