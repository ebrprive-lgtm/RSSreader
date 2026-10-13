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
        bool? lastHealthCheckSucceeded = null,
        bool isEnabled = true,
        bool isEnabledInCategory = true,
        bool isCategoryEnabled = true)
    {
        Id = id;
        Name = FeedTextEncodingRepair.Repair(name);
        FeedUrl = feedUrl;
        Description = description;
        CategoryName = categoryName is null ? null : FeedTextEncodingRepair.Repair(categoryName);
        CategoryId = categoryId;
        WebsiteUrl = websiteUrl;
        SameNameCount = sameNameCount;
        LastHealthCheckedAt = lastHealthCheckedAt;
        LastHealthCheckSucceeded = lastHealthCheckSucceeded;
        IsEnabled = isEnabled;
        IsEnabledInCategory = isEnabledInCategory;
        IsCategoryEnabled = isCategoryEnabled;
    }

    public string Id { get; }
    public string Name { get; }
    public string FeedUrl { get; }
    public string? Description { get; }
    public string? CategoryName { get; }
    public string? CategoryId { get; }
    public string? WebsiteUrl { get; }
    public int SameNameCount { get; }
    public bool HasSameName => SameNameCount > 1;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasWebsiteUrl => !string.IsNullOrWhiteSpace(WebsiteUrl);
    public DateTimeOffset? LastHealthCheckedAt { get; }
    public bool? LastHealthCheckSucceeded { get; }
    public bool IsEnabled { get; }
    public bool IsEnabledInCategory { get; }
    public bool IsCategoryEnabled { get; }
    public string GlobalAvailabilityActionLabel => IsEnabled ? "Disable globally" : "Enable globally";
    public string CategoryAvailabilityActionLabel => IsEnabledInCategory ? "Disable" : "Enable";
    public string SameNameDisplay => SameNameCount > 1 ? $"{SameNameCount} feeds share this name" : string.Empty;
    public bool HasHealthCheck => LastHealthCheckedAt is not null;
    public string PublisherWebsiteDisplay =>
        string.IsNullOrWhiteSpace(WebsiteUrl) ? string.Empty : $"Publisher website: {WebsiteUrl}";
    public string PublisherWebsiteLabel => GetPublisherWebsiteLabel(WebsiteUrl);
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
                OnPropertyChanged(nameof(SubscriptionAutomationName));
            }
        }
    }

    public bool CanSelectForFollow => !IsSubscribed;
    public string SelectForFollowAutomationName => $"Select {Name} for follow";
    public string PreviewAutomationName => $"Preview {Name}";
    public string SubscriptionAutomationName => $"{SubscriptionLabel} {Name}";

    public bool IsSelectedForFollow
    {
        get => _isSelectedForFollow;
        set => SetProperty(ref _isSelectedForFollow, value && CanSelectForFollow);
    }

    public string SubscriptionLabel => IsSubscribed ? "Unfollow" : "Follow";

    private static string GetPublisherWebsiteLabel(string? websiteUrl)
    {
        if (!Uri.TryCreate(websiteUrl, UriKind.Absolute, out var websiteUri) ||
            (websiteUri.Scheme != Uri.UriSchemeHttp && websiteUri.Scheme != Uri.UriSchemeHttps))
        {
            return "Website";
        }

        var host = websiteUri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        return websiteUri.IsDefaultPort ? host : $"{host}:{websiteUri.Port}";
    }
}
