namespace RssReader.Domain;

public enum ProfileStartPage
{
    Today,
    FirstFolder,
    All
}

public enum ProfileArticlePresentation
{
    Cards,
    TitleOnly,
    Magazine
}

public enum ProfileArticleSort
{
    Folder,
    Newest
}

public sealed record ProfilePreferences(
    ProfileStartPage StartPage = ProfileStartPage.Today,
    ProfileArticlePresentation Presentation = ProfileArticlePresentation.Cards,
    ProfileArticleSort Sort = ProfileArticleSort.Folder,
    bool HideReadArticles = false,
    int FolderArticleLimitPerFeed = 10,
    bool RefreshFeedsWhenOpened = true,
    int AutoRefreshIntervalMinutes = 0,
    bool ShowRawFeedButton = false)
{
    public const int MinimumFolderArticleLimitPerFeed = 1;
    public const int MaximumFolderArticleLimitPerFeed = 100;
}