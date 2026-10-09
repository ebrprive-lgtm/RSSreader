using RssReader.Domain;

namespace RssReader.App.ViewModels;

public sealed record ArticleTopicChip(string Name, string? Term, string? Scheme)
{
    public bool IsSelectable => !string.IsNullOrWhiteSpace(Term);
}

public sealed record ArticleFeedTagChip(string Name, bool IsSelectable = true);

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

    public string Title { get; } = FeedTextEncodingRepair.Repair(title);
    public string Source { get; } = FeedTextEncodingRepair.Repair(source);
    public DateTimeOffset PublishedAt { get; } = publishedAt;
    public string Folder { get; } = FeedTextEncodingRepair.Repair(folder);
    public IReadOnlyList<string> Tags { get; } = tags.Select(FeedTextEncodingRepair.Repair).ToArray();
    public string FeedTagSummary { get; } = FormatFeedTagSummary(tags);
    public string FullFeedTagSummary { get; } = FormatFeedTagSummary(tags, int.MaxValue);
    public IReadOnlyList<string> FeedTagChipLabels { get; } = FormatChipLabels(GetFeedTagNames(tags));
    public IReadOnlyList<ArticleFeedTagChip> FeedTagChips { get; } = CreateFeedTagChips(GetFeedTagNames(tags));
    public bool HasFeedTags => !string.IsNullOrEmpty(FeedTagSummary);
    public IReadOnlyList<ArticleCategory> Topics { get; } = NormalizeTopics(topics ?? []);
    public string TopicSummary { get; } = FormatTopicSummary(NormalizeTopics(topics ?? []));
    public string FullTopicSummary { get; } = FormatTopicSummary(NormalizeTopics(topics ?? []), int.MaxValue);
    public IReadOnlyList<string> TopicChipLabels { get; } = FormatChipLabels(GetTopicNames(NormalizeTopics(topics ?? [])));
    public IReadOnlyList<ArticleTopicChip> TopicChips { get; } = CreateTopicChips(NormalizeTopics(topics ?? []));
    public IReadOnlyList<ArticleTopicChip> CardTopicChips { get; } = CreateCardTopicChips(NormalizeTopics(topics ?? []));
    public bool HasTopics => !string.IsNullOrEmpty(TopicSummary);
    public string Summary { get; } = FeedTextEncodingRepair.Repair(summary);
    public string? ArticleId { get; } = articleId;
    public string? FeedId { get; } = feedId;
    public string? Link { get; } = link;
    public string? Content { get; } = content;
    public string? ImageUrl { get; } = imageUrl;
    public string? CardImageUrl { get; } = string.IsNullOrWhiteSpace(imageUrl)
        ? ArticleHtmlDocumentBuilder.FindFirstWebImageUrl(content, link, feedUrl)
        : imageUrl;
    public string? ExternalId { get; } = externalId;
    public string? FeedUrl { get; } = feedUrl;
    public string? WebsiteUrl { get; } = websiteUrl;
    public string? Author { get; } = string.IsNullOrWhiteSpace(author)
        ? null
        : FeedTextEncodingRepair.Repair(author.Trim());
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
        var names = GetTopicNames(topics);
        var visibleNames = names.Take(maximumNames).ToArray();
        return names.Length > maximumNames
            ? $"{string.Join(" / ", visibleNames)} +{names.Length - maximumNames}"
            : string.Join(" / ", visibleNames);
    }

    private static string[] GetTopicNames(IReadOnlyList<ArticleCategory> topics) =>
        topics
            .Select(topic => string.IsNullOrWhiteSpace(topic.Label) ? topic.Term.Trim() : topic.Label.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ArticleTopicChip> CreateTopicChips(IReadOnlyList<ArticleCategory> topics) =>
        topics
            .Select(topic => new ArticleTopicChip(
                string.IsNullOrWhiteSpace(topic.Label) ? topic.Term.Trim() : topic.Label.Trim(),
                topic.Term,
                topic.Scheme))
            .Where(topic => !string.IsNullOrWhiteSpace(topic.Name))
            .DistinctBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ArticleTopicChip> CreateCardTopicChips(IReadOnlyList<ArticleCategory> topics)
    {
        var chips = CreateTopicChips(topics);
        return chips.Count <= 2
            ? chips
            : chips.Take(2).Append(new ArticleTopicChip($"+{chips.Count - 2}", null, null)).ToArray();
    }

    private static IReadOnlyList<ArticleCategory> NormalizeTopics(IReadOnlyList<ArticleCategory> topics) =>
        topics.Select(topic => topic with
        {
            Term = FeedTextEncodingRepair.Repair(topic.Term),
            Label = topic.Label is null ? null : FeedTextEncodingRepair.Repair(topic.Label)
        }).ToArray();

    private static string FormatFeedTagSummary(IReadOnlyList<string> tags, int maximumNames = 3)
    {
        var names = GetFeedTagNames(tags);
        var visibleNames = names.Take(maximumNames).ToArray();
        return names.Length > maximumNames
            ? $"{string.Join(" / ", visibleNames)} +{names.Length - maximumNames}"
            : string.Join(" / ", visibleNames);
    }

    private static string[] GetFeedTagNames(IReadOnlyList<string> tags) =>
        tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => FeedTextEncodingRepair.Repair(tag.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ArticleFeedTagChip> CreateFeedTagChips(IReadOnlyList<string> names)
    {
        const int visibleLimit = 2;
        var chips = names
            .Take(visibleLimit)
            .Select(name => new ArticleFeedTagChip(name))
            .ToList();
        if (names.Count > visibleLimit)
        {
            chips.Add(new ArticleFeedTagChip($"+{names.Count - visibleLimit}", IsSelectable: false));
        }

        return chips;
    }

    private static IReadOnlyList<string> FormatChipLabels(string[] names)
    {
        const int visibleLimit = 2;
        var labels = names.Take(visibleLimit).ToList();
        if (names.Length > visibleLimit)
        {
            labels.Add($"+{names.Length - visibleLimit}");
        }

        return labels;
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