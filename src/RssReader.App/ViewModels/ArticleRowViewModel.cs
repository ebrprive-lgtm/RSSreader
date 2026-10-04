using RssReader.Domain;

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
    string? imageUrl = null,
    string? externalId = null,
    string? feedUrl = null,
    IReadOnlyList<ArticleCategory>? topics = null,
    string? websiteUrl = null,
    string? author = null) : ObservableObject
{
    private bool _isRead = isRead;
    private bool _isSaved = isSaved;

    public string Title { get; } = title;
    public string Source { get; } = source;
    public DateTimeOffset PublishedAt { get; } = publishedAt;
    public string Folder { get; } = folder;
    public IReadOnlyList<string> Tags { get; } = tags;
    public string FeedTagSummary { get; } = FormatFeedTagSummary(tags);
    public bool HasFeedTags => !string.IsNullOrEmpty(FeedTagSummary);
    public IReadOnlyList<ArticleCategory> Topics { get; } = topics ?? [];
    public string TopicSummary { get; } = FormatTopicSummary(topics ?? []);
    public string FullTopicSummary { get; } = FormatTopicSummary(topics ?? [], int.MaxValue);
    public bool HasTopics => !string.IsNullOrEmpty(TopicSummary);
    public string Summary { get; } = summary;
    public string? ArticleId { get; } = articleId;
    public string? FeedId { get; } = feedId;
    public string? Link { get; } = link;
    public string? Content { get; } = content;
    public string? ImageUrl { get; } = imageUrl;
    public string? ExternalId { get; } = externalId;
    public string? FeedUrl { get; } = feedUrl;
    public string? WebsiteUrl { get; } = websiteUrl;
    public string? Author { get; } = string.IsNullOrWhiteSpace(author) ? null : author.Trim();
    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
    public string ReadLaterAutomationName => IsSaved ? "Remove from read later" : "Add to read later";
    public string SourceInitials
    {
        get
        {
            var initials = string.Concat(Source
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(word => !IgnoredSourceWords.Contains(word))
                .Select(word => word.FirstOrDefault(char.IsLetter))
                .Where(char.IsLetter)
                .Take(2)
                .Select(char.ToUpperInvariant));
            return initials.Length == 0 ? "?" : initials;
        }
    }
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);
    public bool HasReadableSummary =>
        string.IsNullOrWhiteSpace(Content) &&
        !string.IsNullOrWhiteSpace(Summary) &&
        (!string.Equals(Summary.Trim(), "Source", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Link));
    public bool IsImageVisible => !string.IsNullOrWhiteSpace(ImageUrl);
    public bool IsSourceLinkVisible =>
        Uri.TryCreate(Link, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    public bool IsWebsiteLinkVisible =>
        Uri.TryCreate(WebsiteUrl, UriKind.Absolute, out var websiteUri) &&
        (websiteUri.Scheme == Uri.UriSchemeHttp || websiteUri.Scheme == Uri.UriSchemeHttps);
    public string AgeLabel => FormatAge(PublishedAt, DateTimeOffset.Now);
    public string PublishedDateLabel => PublishedAt.ToLocalTime().ToString("MMM d, yyyy");

    private static readonly HashSet<string> IgnoredSourceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "and",
        "by",
        "feed",
        "from",
        "of",
        "rss",
        "the"
    };

    private static string FormatTopicSummary(IReadOnlyList<ArticleCategory> topics, int maximumNames = 3)
    {
        var names = topics
            .Select(topic => string.IsNullOrWhiteSpace(topic.Label) ? topic.Term.Trim() : topic.Label.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var visibleNames = names.Take(maximumNames).ToArray();
        return names.Length > maximumNames
            ? $"{string.Join(" / ", visibleNames)} +{names.Length - maximumNames}"
            : string.Join(" / ", visibleNames);
    }

    private static string FormatFeedTagSummary(IReadOnlyList<string> tags)
    {
        var names = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var visibleNames = names.Take(3).ToArray();
        return names.Length > 3
            ? $"{string.Join(" / ", visibleNames)} +{names.Length - 3}"
            : string.Join(" / ", visibleNames);
    }

    public bool IsRead
    {
        get => _isRead;
        set => SetProperty(ref _isRead, value);
    }

    public bool IsSaved
    {
        get => _isSaved;
        set
        {
            if (SetProperty(ref _isSaved, value))
            {
                OnPropertyChanged(nameof(ReadLaterAutomationName));
            }
        }
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