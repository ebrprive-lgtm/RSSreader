namespace RssReader.Domain;

public sealed record ArticleForProfile(
    FeedArticle Article,
    string Source,
    string? FolderName,
    bool IsRead,
    bool IsSaved);