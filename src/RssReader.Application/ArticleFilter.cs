namespace RssReader.Application;

public sealed record ArticleFilter(
    string? FolderName = null,
    string? TagName = null,
    string? FeedId = null,
    bool? IsRead = null,
    bool? IsSaved = null,
    string? SearchText = null);