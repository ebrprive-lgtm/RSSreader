using System.Globalization;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App;

public partial class PreferencesWindow : Window
{
    private readonly ILibraryBackupService? _libraryBackupService;

    public PreferencesWindow(ProfilePreferences preferences, ILibraryBackupService? libraryBackupService = null)
    {
        InitializeComponent();
        _libraryBackupService = libraryBackupService;
        CreateLibraryBackupButton.IsEnabled = libraryBackupService is not null;
        RestoreLibraryBackupButton.IsEnabled = libraryBackupService is not null;
        Loaded += PreferencesWindow_Loaded;
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
        RefreshFeedsWhenOpenedCheckBox.IsChecked = preferences.RefreshFeedsWhenOpened;
        ShowRawFeedButtonCheckBox.IsChecked = preferences.ShowRawFeedButton;
        LimitArticleWidthCheckBox.IsChecked = preferences.LimitArticleWidth;
        AutoRefreshIntervalComboBox.SelectedItem = AutoRefreshIntervalComboBox.Items
            .OfType<System.Windows.Controls.ComboBoxItem>()
            .First(item => int.Parse((string)item.Tag, CultureInfo.InvariantCulture) == preferences.AutoRefreshIntervalMinutes);
        SelectOption(ArticleListDensityComboBox, preferences.ArticleListDensity);
        SelectOption(ReaderThemeComboBox, preferences.ReaderTheme);
        AutoLoadFullArticleCheckBox.IsChecked = preferences.AutoLoadFullArticle;
        PreloadNextArticleCheckBox.IsChecked = preferences.PreloadNextArticle;
        SmoothArticleScrollingCheckBox.IsChecked = preferences.SmoothArticleScrolling;
    }

    public ProfilePreferences Preferences { get; private set; }
    public bool RestorePending { get; private set; }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void GeneralNavigation_Click(object sender, RoutedEventArgs e) => ShowSection("General", GeneralPanel);

    private void AppearanceNavigation_Click(object sender, RoutedEventArgs e) => ShowSection("Appearance", AppearancePanel);

    private void ReadingNavigation_Click(object sender, RoutedEventArgs e) => ShowSection("Reading", ReadingPanel);

    private void DebuggingNavigation_Click(object sender, RoutedEventArgs e) => ShowSection("Debugging", DebuggingPanel);

    private void DataNavigation_Click(object sender, RoutedEventArgs e) => ShowSection("Data & backups", DataPanel);

    private void ShowSection(string title, UIElement section)
    {
        SectionTitle.Text = title;
        GeneralPanel.Visibility = ReferenceEquals(section, GeneralPanel) ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = ReferenceEquals(section, AppearancePanel) ? Visibility.Visible : Visibility.Collapsed;
        ReadingPanel.Visibility = ReferenceEquals(section, ReadingPanel) ? Visibility.Visible : Visibility.Collapsed;
        DebuggingPanel.Visibility = ReferenceEquals(section, DebuggingPanel) ? Visibility.Visible : Visibility.Collapsed;
        DataPanel.Visibility = ReferenceEquals(section, DataPanel) ? Visibility.Visible : Visibility.Collapsed;
        ValidationMessage.Text = string.Empty;
    }

    private async void PreferencesWindow_Loaded(object sender, RoutedEventArgs e) =>
        await RefreshStorageSizeAsync();

    private async void RefreshStorageSize_Click(object sender, RoutedEventArgs e) =>
        await RefreshStorageSizeAsync();

    private async Task RefreshStorageSizeAsync()
    {
        if (_libraryBackupService is null)
        {
            StorageUsageText.Text = "Local storage information is unavailable.";
            return;
        }

        try
        {
            var bytes = await Task.Run(_libraryBackupService.GetLocalStorageBytes);
            StorageUsageText.Text = $"Database and write-ahead log: {FormatStorageSize(bytes)}";
        }
        catch (Exception exception)
        {
            DataOperationStatus.Text = "Local storage size could not be read.";
            Trace.TraceError($"Could not read local library storage size: {exception}");
        }
    }

    private async void CreateLibraryBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_libraryBackupService is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".rssbackup",
            FileName = $"rssreader-{DateTimeOffset.Now:yyyyMMdd}.rssbackup",
            Filter = "RSS Reader library backup (*.rssbackup)|*.rssbackup",
            OverwritePrompt = true,
            Title = "Back up the complete local library"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _libraryBackupService.CreateBackupAsync(dialog.FileName);
            DataOperationStatus.Text = $"Library backup created: {dialog.FileName}";
        }
        catch (Exception exception)
        {
            DataOperationStatus.Text = $"Backup failed: {exception.Message}";
            Trace.TraceError($"Could not create the local library backup: {exception}");
        }
    }

    private async void RestoreLibraryBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_libraryBackupService is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "RSS Reader library backup (*.rssbackup;*.db)|*.rssbackup;*.db",
            Title = "Choose a complete RSS Reader library backup"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var confirmation = MessageDialogWindow.Show(
            this,
            "Restoring replaces the complete local library, including every profile. The current library will be saved as a safety backup first. RSS Reader will close after preparing the restore; reopen it to finish. Backup files are not encrypted. Continue?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _libraryBackupService.StageRestoreAsync(dialog.FileName);
            RestorePending = true;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            DataOperationStatus.Text = $"Restore could not be prepared: {exception.Message}";
            Trace.TraceError($"Could not prepare the local library restore: {exception}");
        }
    }

    private static string FormatStorageSize(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? $"{bytes / (1024d * 1024 * 1024):N2} GB"
            : bytes >= 1024L * 1024
                ? $"{bytes / (1024d * 1024):N1} MB"
                : $"{bytes / 1024d:N0} KB";

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
            limit,
            RefreshFeedsWhenOpenedCheckBox.IsChecked == true,
            int.Parse((string)((System.Windows.Controls.ComboBoxItem)AutoRefreshIntervalComboBox.SelectedItem).Tag, CultureInfo.InvariantCulture),
            ShowRawFeedButtonCheckBox.IsChecked == true,
            LimitArticleWidthCheckBox.IsChecked == true,
            Preferences.HideFollowedCatalogFeeds,
            Preferences.ReadingLayout,
            GetSelectedOption<ProfileArticleListDensity>(ArticleListDensityComboBox),
            GetSelectedOption<ProfileReaderTheme>(ReaderThemeComboBox),
            Preferences.ReaderTextSize,
            Preferences.ReaderLineSpacing,
            Preferences.ReaderFontFamily,
            Preferences.SplitPaneListRatio,
            AutoLoadFullArticle: AutoLoadFullArticleCheckBox.IsChecked == true,
            PreloadNextArticle: PreloadNextArticleCheckBox.IsChecked == true,
            SmoothArticleScrolling: SmoothArticleScrollingCheckBox.IsChecked == true);
        DialogResult = true;
    }

    private static void SelectOption<T>(System.Windows.Controls.ComboBox comboBox, T value)
        where T : struct, Enum =>
        comboBox.SelectedItem = comboBox.Items
            .OfType<System.Windows.Controls.ComboBoxItem>()
            .First(item => int.Parse((string)item.Tag, CultureInfo.InvariantCulture) == Convert.ToInt32(value, CultureInfo.InvariantCulture));

    private static T GetSelectedOption<T>(System.Windows.Controls.ComboBox comboBox)
        where T : struct, Enum =>
        (T)Enum.ToObject(
            typeof(T),
            int.Parse(
                (string)((System.Windows.Controls.ComboBoxItem)comboBox.SelectedItem).Tag,
                CultureInfo.InvariantCulture));
}