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

public enum ProfileReadingLayout
{
    FullPage,
    SplitPane
}

public enum ProfileArticleListDensity
{
    Default,
    Compact,
    Spacious
}

public enum ProfileReaderTheme
{
    Light,
    Dark,
    Warm
}

public enum ProfileReaderTextSize
{
    Small,
    Medium,
    Large
}

public enum ProfileReaderLineSpacing
{
    Compact,
    Normal,
    Relaxed
}

public enum ProfileReaderFontFamily
{
    SansSerif,
    Serif,
    Monospace
}

public sealed record ProfilePreferences(
    ProfileStartPage StartPage = ProfileStartPage.Today,
    ProfileArticlePresentation Presentation = ProfileArticlePresentation.Cards,
    ProfileArticleSort Sort = ProfileArticleSort.Folder,
    bool HideReadArticles = false,
    int FolderArticleLimitPerFeed = 10,
    bool RefreshFeedsWhenOpened = true,
    int AutoRefreshIntervalMinutes = 0,
    bool ShowRawFeedButton = false,
    bool LimitArticleWidth = true,
    bool HideFollowedCatalogFeeds = false,
    ProfileReadingLayout ReadingLayout = ProfileReadingLayout.FullPage,
    ProfileArticleListDensity ArticleListDensity = ProfileArticleListDensity.Default,
    ProfileReaderTheme ReaderTheme = ProfileReaderTheme.Light,
    ProfileReaderTextSize ReaderTextSize = ProfileReaderTextSize.Medium,
    ProfileReaderLineSpacing ReaderLineSpacing = ProfileReaderLineSpacing.Normal,
    ProfileReaderFontFamily ReaderFontFamily = ProfileReaderFontFamily.SansSerif,
    double SplitPaneListRatio = 0.45)
{
    public const int MinimumFolderArticleLimitPerFeed = 1;
    public const int MaximumFolderArticleLimitPerFeed = 100;
}