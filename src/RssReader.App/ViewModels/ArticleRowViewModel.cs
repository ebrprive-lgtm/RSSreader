namespace RssReader.App.ViewModels;

public sealed class ArticleRowViewModel(
    string title,
    string source,
    DateTimeOffset publishedAt,
    string folder,
    IReadOnlyList<string> tags,
    string summary,
    bool isRead = false,
    bool isSaved = false,
    string? articleId = null,
    string? feedId = null,
    string? link = null,
    string? content = null,
    string? imageUrl = null) : ObservableObject
{
    private bool _isRead = isRead;
    private bool _isSaved = isSaved;

    public string Title { get; } = title;
    public string Source { get; } = source;
    public DateTimeOffset PublishedAt { get; } = publishedAt;
    public string Folder { get; } = folder;
    public IReadOnlyList<string> Tags { get; } = tags;
    public string Summary { get; } = summary;
    public string? ArticleId { get; } = articleId;
    public string? FeedId { get; } = feedId;
    public string? Link { get; } = link;
    public string? Content { get; } = content;
    public string? ImageUrl { get; } = imageUrl;
    public bool HasReadableSummary =>
        !string.IsNullOrWhiteSpace(Summary) &&
        (!string.Equals(Summary.Trim(), "Source", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Link));
    public bool IsImageVisible => !string.IsNullOrWhiteSpace(ImageUrl);
    public bool IsSourceLinkVisible =>
        Uri.TryCreate(Link, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    public string AgeLabel => FormatAge(PublishedAt, DateTimeOffset.Now);
    public string PublishedDateLabel => PublishedAt.ToLocalTime().ToString("MMM d, yyyy");

    public bool IsRead
    {
        get => _isRead;
        set => SetProperty(ref _isRead, value);
    }

    public bool IsSaved
    {
        get => _isSaved;
        set => SetProperty(ref _isSaved, value);
    }

    public static string FormatAge(DateTimeOffset publishedAt, DateTimeOffset now)
    {
        var age = now - publishedAt;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "Now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes}m ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{(int)age.TotalHours}h ago";
        }

        if (age < TimeSpan.FromDays(7))
        {
            return $"{(int)age.TotalDays}d ago";
        }

        return publishedAt.ToLocalTime().ToString("MMM d");
    }
}