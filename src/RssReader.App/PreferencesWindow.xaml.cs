using System.Globalization;
using System.Windows;
using RssReader.Domain;

namespace RssReader.App;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow(ProfilePreferences preferences)
    {
        InitializeComponent();
        Preferences = preferences;
        StartTodayOption.IsChecked = preferences.StartPage == ProfileStartPage.Today;
        StartFirstFolderOption.IsChecked = preferences.StartPage == ProfileStartPage.FirstFolder;
        StartAllOption.IsChecked = preferences.StartPage == ProfileStartPage.All;
        PresentationTitleOnlyOption.IsChecked = preferences.Presentation == ProfileArticlePresentation.TitleOnly;
        PresentationMagazineOption.IsChecked = preferences.Presentation == ProfileArticlePresentation.Magazine;
        PresentationCardsOption.IsChecked = preferences.Presentation == ProfileArticlePresentation.Cards;
        SortFolderOption.IsChecked = preferences.Sort == ProfileArticleSort.Folder;
        SortNewestOption.IsChecked = preferences.Sort == ProfileArticleSort.Newest;
        HideReadArticlesCheckBox.IsChecked = preferences.HideReadArticles;
        FolderArticleLimitBox.Text = preferences.FolderArticleLimitPerFeed.ToString(CultureInfo.InvariantCulture);
    }

    public ProfilePreferences Preferences { get; private set; }

    private void GeneralNavigation_Click(object sender, RoutedEventArgs e) => ShowSection("General", GeneralPanel);

    private void AppearanceNavigation_Click(object sender, RoutedEventArgs e) => ShowSection("Appearance", AppearancePanel);

    private void ReadingNavigation_Click(object sender, RoutedEventArgs e) => ShowSection("Reading", ReadingPanel);

    private void ShowSection(string title, UIElement section)
    {
        SectionTitle.Text = title;
        GeneralPanel.Visibility = ReferenceEquals(section, GeneralPanel) ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = ReferenceEquals(section, AppearancePanel) ? Visibility.Visible : Visibility.Collapsed;
        ReadingPanel.Visibility = ReferenceEquals(section, ReadingPanel) ? Visibility.Visible : Visibility.Collapsed;
        ValidationMessage.Text = string.Empty;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(FolderArticleLimitBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ||
            limit is < ProfilePreferences.MinimumFolderArticleLimitPerFeed or > ProfilePreferences.MaximumFolderArticleLimitPerFeed)
        {
            ReadingNavigation.IsChecked = true;
            ShowSection("Reading", ReadingPanel);
            ValidationMessage.Text = $"Enter a value from {ProfilePreferences.MinimumFolderArticleLimitPerFeed} to {ProfilePreferences.MaximumFolderArticleLimitPerFeed}.";
            return;
        }

        Preferences = new ProfilePreferences(
            StartTodayOption.IsChecked == true
                ? ProfileStartPage.Today
                : StartFirstFolderOption.IsChecked == true
                    ? ProfileStartPage.FirstFolder
                    : ProfileStartPage.All,
            PresentationTitleOnlyOption.IsChecked == true
                ? ProfileArticlePresentation.TitleOnly
                : PresentationMagazineOption.IsChecked == true
                    ? ProfileArticlePresentation.Magazine
                    : ProfileArticlePresentation.Cards,
            SortNewestOption.IsChecked == true ? ProfileArticleSort.Newest : ProfileArticleSort.Folder,
            HideReadArticlesCheckBox.IsChecked == true,
            limit);
        DialogResult = true;
    }
}