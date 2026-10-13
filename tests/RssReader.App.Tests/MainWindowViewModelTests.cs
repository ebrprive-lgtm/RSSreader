using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using RssReader.App.ViewModels;
using RssReader.Application;
using RssReader.Domain;
using RssReader.Infrastructure;
using Microsoft.Data.Sqlite;
using System.Threading;
using System.Runtime.InteropServices;
using Ellipse = System.Windows.Shapes.Ellipse;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace RssReader.App.Tests;

[TestClass]
public sealed class MainWindowViewModelTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void CatalogFeedWebsiteLabelShowsItsDomainWithoutPath()
    {
        var feed = new CatalogFeedListItem(
            "example-feed",
            "Example Feed",
            "https://feeds.example.com/rss",
            null,
            null,
            websiteUrl: "https://www.publisher.example/articles");

        Assert.AreEqual("publisher.example", feed.PublisherWebsiteLabel);
    }

    [TestMethod]
    public void RawFeedWindowShowsCachedBadgeOnlyForCachedContent()
    {
        WpfTestHost.Run(() =>
        {
            var cachedWindow = new RawFeedWindow("Example feed", "<item />", isCached: true);
            var liveWindow = new RawFeedWindow("Example feed", "<item />");

            try
            {
                Assert.AreEqual(
                    Visibility.Visible,
                    ((Border)cachedWindow.FindName("CachedSourceBadge")!).Visibility);
                Assert.AreEqual(
                    Visibility.Collapsed,
                    ((Border)liveWindow.FindName("CachedSourceBadge")!).Visibility);
            }
            finally
            {
                cachedWindow.Close();
                liveWindow.Close();
            }
        });
    }

    [TestMethod]
    public void CatalogFeedScanProgressWindowCancelsTheActiveScan()
    {
        WpfTestHost.Run(() =>
        {
            var scanStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scanCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new CatalogFeedScanProgressWindow(
                2,
                async (_, cancellationToken) =>
                {
                    scanStarted.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    finally
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            scanCancelled.TrySetResult();
                        }
                    }

                    return 0;
                });
            Assert.AreEqual(WindowStyle.None, window.WindowStyle);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                if (!scanStarted.Task.IsCompleted)
                {
                    return;
                }

                var cancelButton = (Button)window.FindName("CancelButton")!;
                if (!cancelButton.IsEnabled)
                {
                    return;
                }

                if (Equals(cancelButton.Content, "Cancel"))
                {
                    cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (Equals(cancelButton.Content, "Close"))
                {
                    timer.Stop();
                    cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };
            timer.Start();

            window.ShowDialog();

            Assert.IsTrue(scanCancelled.Task.IsCompleted);
            StringAssert.Contains(((TextBlock)window.FindName("StatusText")!).Text, "cancelled");
        });
    }

    [TestMethod]
    public void PreferencesWindowPreservesSplitModeWhileSavingOtherReadingOptions()
    {
        WpfTestHost.Run(() =>
        {
            var initialPreferences = new ProfilePreferences(
                ReaderTextSize: ProfileReaderTextSize.Large,
                ReaderLineSpacing: ProfileReaderLineSpacing.Relaxed,
                ReaderFontFamily: ProfileReaderFontFamily.Serif,
                ReadingLayout: ProfileReadingLayout.SplitPane,
                SplitPaneListRatio: 0.63,
                AutoLoadFullArticle: false,
                PreloadNextArticle: false,
                SmoothArticleScrolling: true);
            var window = new PreferencesWindow(initialPreferences);
            Assert.IsNull(window.FindName("ReadingLayoutComboBox"));
            Assert.AreEqual(0, ((ComboBox)window.FindName("ArticleListDensityComboBox")!).SelectedIndex);
            Assert.AreEqual(0, ((ComboBox)window.FindName("ReaderThemeComboBox")!).SelectedIndex);
            Assert.IsFalse(((CheckBox)window.FindName("AutoLoadFullArticleCheckBox")!).IsChecked);
            Assert.IsFalse(((CheckBox)window.FindName("PreloadNextArticleCheckBox")!).IsChecked);
            Assert.IsTrue(((CheckBox)window.FindName("SmoothArticleScrollingCheckBox")!).IsChecked);
            Assert.IsNull(window.FindName("ReaderTextSizeComboBox"));
            Assert.IsNull(window.FindName("ReaderLineSpacingComboBox"));
            Assert.IsNull(window.FindName("ReaderFontFamilyComboBox"));
            var dataNavigation = (RadioButton)window.FindName("DataNavigation")!;
            dataNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(Visibility.Visible, ((StackPanel)window.FindName("DataPanel")!).Visibility);
            Assert.IsFalse(((Button)window.FindName("CreateLibraryBackupButton")!).IsEnabled);
            Assert.IsFalse(((Button)window.FindName("RestoreLibraryBackupButton")!).IsEnabled);

            window.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    ((ComboBox)window.FindName("ArticleListDensityComboBox")!).SelectedIndex = 2;
                    ((ComboBox)window.FindName("ReaderThemeComboBox")!).SelectedIndex = 2;
                    ((CheckBox)window.FindName("AutoLoadFullArticleCheckBox")!).IsChecked = true;
                    ((CheckBox)window.FindName("PreloadNextArticleCheckBox")!).IsChecked = true;
                    ((CheckBox)window.FindName("SmoothArticleScrollingCheckBox")!).IsChecked = false;
                    ((Button)window.FindName("SavePreferencesButton")!).RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                }));

            Assert.IsTrue(window.ShowDialog() == true);
            Assert.AreEqual(
                new ProfilePreferences(
                    ReadingLayout: ProfileReadingLayout.SplitPane,
                    SplitPaneListRatio: 0.63,
                    ArticleListDensity: ProfileArticleListDensity.Spacious,
                    ReaderTheme: ProfileReaderTheme.Warm,
                    ReaderTextSize: ProfileReaderTextSize.Large,
                    ReaderLineSpacing: ProfileReaderLineSpacing.Relaxed,
                    ReaderFontFamily: ProfileReaderFontFamily.Serif,
                    AutoLoadFullArticle: true,
                    PreloadNextArticle: true,
                    SmoothArticleScrolling: false),
                window.Preferences);
        });
    }

    [TestMethod]
    public void ApplicationThemeUpdatesExistingMainWindowAndPreferencesWindow()
    {
        WpfTestHost.Run(() =>
        {
            var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
            var mainWindow = CreateTestMainWindow(viewModel);
            var preferencesWindow = new PreferencesWindow(new ProfilePreferences());
            WpfTestHost.Application.MainWindow = mainWindow;

            try
            {
                mainWindow.Show();
                preferencesWindow.Show();
                mainWindow.UpdateLayout();
                preferencesWindow.UpdateLayout();

                viewModel.ApplyPreferences(new ProfilePreferences(ReaderTheme: ProfileReaderTheme.Dark));
                mainWindow.UpdateLayout();
                preferencesWindow.UpdateLayout();

                var darkContent = Color.FromRgb(32, 35, 38);
                Assert.AreEqual(
                    darkContent,
                    ((SolidColorBrush)((Border)mainWindow.FindName("MainWindowSurface")!).Background).Color);
                Assert.AreEqual(darkContent, ((SolidColorBrush)preferencesWindow.Background).Color);
                Assert.AreEqual(
                    Color.FromRgb(238, 240, 242),
                    ((SolidColorBrush)WpfTestHost.Application.Resources["TextBrush"]).Color);
            }
            finally
            {
                preferencesWindow.Close();
                mainWindow.Close();
                WpfTestHost.Application.MainWindow = null;
                AppThemeManager.Apply(ProfileReaderTheme.Light);
            }
        });
    }

    [TestMethod]
    public void ArticleReaderTypographyPopupUpdatesTextSizeSpacingAndFontFamily()
    {
        WpfTestHost.Run(() =>
        {
            var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
            viewModel.SelectArticleCommand.Execute(viewModel.VisibleArticles[0]);
            var window = CreateTestMainWindow(viewModel);
            WpfTestHost.Application.MainWindow = window;

            try
            {
                window.Show();
                window.UpdateLayout();
                var readerView = (FrameworkElement)window.FindName("ArticleReaderView")!;
                var popup = (System.Windows.Controls.Primitives.Popup)readerView.FindName("ReaderTypographyPopup")!;
                ((Button)readerView.FindName("ReaderTypographyButton")!).RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.IsTrue(popup.IsOpen);

                ((ComboBox)readerView.FindName("ReaderTextSizeComboBox")!).SelectedIndex = 3;
                ((ComboBox)readerView.FindName("ReaderFontFamilyComboBox")!).SelectedIndex = 1;
                ((ComboBox)readerView.FindName("ReaderLineSpacingComboBox")!).SelectedIndex = 0;

                Assert.AreEqual(ProfileReaderTextSize.XLarge, viewModel.ReaderTextSize);
                Assert.AreEqual(ProfileReaderFontFamily.Serif, viewModel.ReaderFontFamily);
                Assert.AreEqual(ProfileReaderLineSpacing.ExtraCompact, viewModel.ReaderLineSpacing);
            }
            finally
            {
                window.Close();
                WpfTestHost.Application.MainWindow = null;
                AppThemeManager.Apply(ProfileReaderTheme.Light);
            }
        });
    }

    [TestMethod]
    public void CommandPaletteFiltersDestinationsWithoutLosingItsKeyboardResultsList()
    {
        WpfTestHost.Run(() =>
        {
            var window = new CommandPaletteWindow(
            [
                new CommandPaletteItem("Refresh feeds", "Update followed sources", () => { }),
                new CommandPaletteItem("Show reader", "Open article reader", () => { })
            ]);
            window.Show();
            window.UpdateLayout();

            var searchBox = (TextBox)window.FindName("SearchBox")!;
            var commandList = (ListBox)window.FindName("CommandList")!;
            searchBox.Text = "reader";
            window.UpdateLayout();

            Assert.AreEqual(1, commandList.Items.Count);
            Assert.AreEqual("Show reader", ((CommandPaletteItem)commandList.Items[0]).Label);
            window.Close();
        });
    }

    private static T FindReaderElement<T>(MainWindow window, string name) where T : class
    {
        var readerView = window.FindName("ArticleReaderView") as FrameworkElement
            ?? throw new AssertFailedException("The article reader view was not found.");
        return readerView.FindName(name) as T
            ?? throw new AssertFailedException($"The article reader element '{name}' was not found.");
    }

    private static T FindArticleListElement<T>(MainWindow window, string name) where T : class
    {
        var articleListView = window.FindName("ArticleListView") as FrameworkElement
            ?? throw new AssertFailedException("The article list view was not found.");
        return articleListView.FindName(name) as T
            ?? throw new AssertFailedException($"The article list element '{name}' was not found.");
    }

    [TestMethod]
    public void CollectionMembershipPickerSearchesBulkSelectsAndAddsCollections()
    {
        WpfTestHost.Run(() =>
        {
            var window = new CatalogMembershipPickerWindow(
                "Collections for source",
                "Choose collections for this source.",
                [
                    new CatalogMembershipPickerItem("gaming", "PC Gaming", "12 feeds", isSelected: false),
                    new CatalogMembershipPickerItem("news", "Daily News", "8 feeds", isSelected: true)
                ]);
            window.Show();
            window.UpdateLayout();

            var searchBox = (TextBox)window.FindName("SearchBox")!;
            var membershipList = (ListBox)window.FindName("MembershipList")!;
            searchBox.Text = "gaming";
            window.UpdateLayout();
            Assert.AreEqual(1, membershipList.Items.Count);
            ((Button)window.FindName("SelectShownButton")!).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            CollectionAssert.AreEquivalent(new[] { "gaming", "news" }, window.SelectedIds.ToArray());
            ((Button)window.FindName("ClearShownButton")!).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            CollectionAssert.AreEqual(new[] { "news" }, window.SelectedIds.ToArray());
            Assert.AreEqual("1 selected · 1 shown", ((TextBlock)window.FindName("SelectionSummary")!).Text);
            window.Close();

            var createWindow = new CatalogMembershipPickerWindow(
                "Collections for source",
                "Choose collections for this source.",
                [],
                _ => Task.FromResult<CatalogMembershipPickerItem?>(
                    new CatalogMembershipPickerItem("new-collection", "New collection", "0 feeds", false)));
            createWindow.Show();
            createWindow.UpdateLayout();
            ((Button)createWindow.FindName("CreateCollectionButton")!).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            CollectionAssert.AreEqual(new[] { "new-collection" }, createWindow.SelectedIds.ToArray());
            createWindow.Close();
        });
    }

    [TestMethod]
    public void ReadLaterAutomationNameTracksSavedState()
    {
        var article = new ArticleRowViewModel(
            "Example article",
            "Example source",
            DateTimeOffset.Now,
            "Inbox",
            [],
            "Example summary");
        var automationNameChanged = false;
        article.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ArticleRowViewModel.ReadLaterAutomationName))
            {
                automationNameChanged = true;
            }
        };

        Assert.AreEqual("Add to read later", article.ReadLaterAutomationName);
        article.IsSaved = true;
        Assert.AreEqual("Remove from read later", article.ReadLaterAutomationName);
        Assert.IsTrue(automationNameChanged);
    }

    [TestMethod]
    public void ArticleRowRepairsMojibakeAcrossSharedViewModeTextAndPreservesValidUnicode()
    {
        var article = new ArticleRowViewModel(
            "FranÃ§ois reviews â€œCafÃ© Noirâ€™s â€“ part 2",
            "Forbes Ã‚Â» Business RSS Feed",
            DateTimeOffset.Now,
            "CafÃ© & Culture",
            ["CrÃ¨me brûlée"],
            "A proper café and an encoded naÃ¯ve review.",
            topics: [new ArticleCategory("CrÃ¨me Brûlée", Label: "CafÃ© & Culture")],
            author: "RenÃ©e");

        Assert.AreEqual("François reviews “Café Noir’s – part 2", article.Title);
        Assert.AreEqual("Forbes » Business RSS Feed", article.Source);
        Assert.AreEqual("Café & Culture", article.Folder);
        Assert.AreEqual("Crème brûlée", article.Tags.Single());
        Assert.AreEqual("Crème brûlée", article.FeedTagSummary);
        Assert.AreEqual("Café & Culture", article.TopicSummary);
        Assert.AreEqual("Café & Culture", article.Topics.Single().Label);
        Assert.AreEqual("A proper café and an encoded naïve review.", article.Summary);
        Assert.AreEqual("Renée", article.Author);

        var validUnicodeArticle = new ArticleRowViewModel(
            "Café – España",
            "Café News",
            DateTimeOffset.Now,
            "España",
            [],
            "Already-correct naïve text.");
        Assert.AreEqual("Café – España", validUnicodeArticle.Title);
        Assert.AreEqual("Café News", validUnicodeArticle.Source);
        Assert.AreEqual("Already-correct naïve text.", validUnicodeArticle.Summary);
    }

    [TestMethod]
    public void ArticleTopicSummariesCapCompactTextButRetainEveryTopicForReadingView()
    {
        var article = new ArticleRowViewModel(
            "Example article",
            "Example source",
            DateTimeOffset.Now,
            "Inbox",
            [],
            "Example summary",
            topics:
            [
                new ArticleCategory("Science"),
                new ArticleCategory("Review", Label: "Reviews"),
                new ArticleCategory("Events"),
                new ArticleCategory("Updates")
            ]);

        Assert.AreEqual("Science / Reviews / Events +1", article.TopicSummary);
        Assert.AreEqual("Science / Reviews / Events / Updates", article.FullTopicSummary);
        CollectionAssert.AreEqual(new[] { "Science", "Reviews", "+2" }, article.TopicChipLabels.ToArray());
        CollectionAssert.AreEqual(
            new[] { "Science", "Reviews", "Events", "Updates" },
            article.TopicChips.Select(topic => topic.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Science", "Reviews", "+2" },
            article.CardTopicChips.Select(topic => topic.Name).ToArray());
        Assert.IsFalse(article.CardTopicChips[^1].IsSelectable);
    }

    [TestMethod]
    public void ArticleFeedTagSummaryIsCompactAndDeduplicated()
    {
        var article = new ArticleRowViewModel(
            "Example article",
            "Example source",
            DateTimeOffset.Now,
            "Inbox",
            [" Reviews ", "News", "reviews", "Analysis", "Culture"],
            "Example summary");

        Assert.AreEqual("Reviews / News / Analysis +1", article.FeedTagSummary);
        Assert.AreEqual("Reviews / News / Analysis / Culture", article.FullFeedTagSummary);
        CollectionAssert.AreEqual(new[] { "Reviews", "News", "+2" }, article.FeedTagChipLabels.ToArray());
        CollectionAssert.AreEqual(
            new[] { "Reviews", "News", "+2" },
            article.FeedTagChips.Select(chip => chip.Name).ToArray());
        Assert.IsTrue(article.FeedTagChips.Take(2).All(chip => chip.IsSelectable));
        Assert.IsFalse(article.FeedTagChips[^1].IsSelectable);
        Assert.IsTrue(article.HasFeedTags);

        var untaggedArticle = new ArticleRowViewModel(
            "Untagged article",
            "Example source",
            DateTimeOffset.Now,
            "Inbox",
            [],
            "Example summary");
        Assert.IsFalse(untaggedArticle.HasFeedTags);
    }

    [TestMethod]
    public async Task BulkArticleActionsRequireSelectionModeAndApplyOnlyToSelectedArticles()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var articlesToSave = viewModel.VisibleArticles.Take(2).ToArray();
        var unselectedArticle = viewModel.VisibleArticles.Single(article => !articlesToSave.Contains(article));

        viewModel.ToggleBulkSelectionModeCommand.Execute(null);
        foreach (var article in articlesToSave)
        {
            viewModel.SelectArticleCommand.Execute(article);
        }

        Assert.IsNull(viewModel.SelectedArticle);
        Assert.AreEqual(2, viewModel.BulkSelectedArticleCount);
        Assert.IsTrue(articlesToSave.All(article => article.IsBulkSelected));
        Assert.IsFalse(unselectedArticle.IsBulkSelected);

        await viewModel.BulkSaveCommand.ExecuteAsync();

        Assert.AreEqual(0, viewModel.BulkSelectedArticleCount);
        Assert.IsTrue(articlesToSave.All(article => article.IsSaved));
        Assert.IsFalse(unselectedArticle.IsSaved);
        StringAssert.Contains(viewModel.StatusMessage, "Updated 2 of 2 selected articles.");

        viewModel.ToggleBulkSelectionModeCommand.Execute(null);
        Assert.IsFalse(viewModel.IsBulkSelectionMode);
        Assert.IsTrue(articlesToSave.All(article => !article.IsBulkSelected));
    }

    [TestMethod]
    public void SplitPaneFallsBackAtNarrowWidthsAndListDensityChangesCardVirtualizationSize()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var article = viewModel.VisibleArticles[0];
        viewModel.ApplyPreferences(new ProfilePreferences(ReadingLayout: ProfileReadingLayout.SplitPane));
        viewModel.SelectArticleCommand.Execute(article);

        viewModel.UpdateSplitPaneViewportWidth(680);
        Assert.IsTrue(viewModel.IsSplitPaneActive);
        Assert.IsTrue(viewModel.IsArticleListVisible);
        Assert.AreEqual(1, viewModel.ArticleListGridColumnSpan);
        Assert.AreEqual(2, viewModel.ArticleReaderGridColumn);

        viewModel.UpdateSplitPaneViewportWidth(679);
        Assert.IsFalse(viewModel.IsSplitPaneActive);
        Assert.IsFalse(viewModel.IsArticleListVisible);
        Assert.AreEqual(3, viewModel.ArticleReaderGridColumnSpan);

        viewModel.UpdateSplitPaneViewportWidth(700);
        Assert.IsTrue(viewModel.IsSplitPaneActive);
        viewModel.ApplyPreferences(new ProfilePreferences(ArticleListDensity: ProfileArticleListDensity.Compact));
        Assert.AreEqual(new Size(286, 346), viewModel.ArticleCardItemSize);
        viewModel.ApplyPreferences(new ProfilePreferences(ArticleListDensity: ProfileArticleListDensity.Spacious));
        Assert.AreEqual(new Size(346, 410), viewModel.ArticleCardItemSize);
    }

    [TestMethod]
    public void EmptySplitPaneReaderPlaceholderIsHiddenOnFollowSources()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        viewModel.ApplyPreferences(new ProfilePreferences(ReadingLayout: ProfileReadingLayout.SplitPane));
        viewModel.UpdateSplitPaneViewportWidth(800);

        Assert.IsTrue(viewModel.IsSplitPaneEmptyReaderPlaceholderVisible);
        viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));

        Assert.IsFalse(viewModel.IsSplitPaneEmptyReaderPlaceholderVisible);

        viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));

        Assert.IsTrue(viewModel.IsSplitPaneEmptyReaderPlaceholderVisible);
    }

    [TestMethod]
    public async Task SplitPaneToggleChangesAndPersistsProfileLayoutAndDividerRatio()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var article = viewModel.VisibleArticles[0];
        viewModel.SelectArticleCommand.Execute(article);
        viewModel.UpdateSplitPaneViewportWidth(1000);

        await viewModel.ToggleSplitPaneCommand.ExecuteAsync();

        Assert.IsTrue(viewModel.IsSplitPaneEnabled);
        Assert.IsTrue(viewModel.IsSplitPaneActive);
        Assert.IsTrue(viewModel.IsArticleListVisible);
        Assert.AreSame(article, viewModel.SelectedArticle);

        await viewModel.UpdateSplitPaneListRatioAsync(0.9);
        Assert.AreEqual(0.7, viewModel.SplitPaneListRatio, 0.001);

        await viewModel.ToggleSplitPaneCommand.ExecuteAsync();
        Assert.IsFalse(viewModel.IsSplitPaneEnabled);
        Assert.IsFalse(viewModel.IsSplitPaneActive);
        Assert.IsFalse(viewModel.IsArticleListVisible);
        Assert.AreSame(article, viewModel.SelectedArticle);
    }

    [TestMethod]
    public void SplitPaneToggleShowsAnEmptyReaderPlaceholderWithoutAnArticleSelection()
    {
        WpfTestHost.Run(() =>
        {
            var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
            var window = CreateTestMainWindow(viewModel);
            window.Width = 1100;
            window.Height = 760;
            WpfTestHost.Application.MainWindow = window;

            try
            {
                window.Show();
                window.UpdateLayout();
                var articleList = (ArticleListView)window.FindName("ArticleListView")!;
                var toggleButton = (Button)articleList.FindName("ArticleListSplitPaneToggleButton")!;
                Assert.AreSame(viewModel.ToggleSplitPaneCommand, toggleButton.Command);
                toggleButton.Command.Execute(toggleButton.CommandParameter);
                window.UpdateLayout();

                var placeholder = (FrameworkElement)window.FindName("SplitPaneEmptyReaderPlaceholder")!;
                var splitter = (FrameworkElement)window.FindName("SplitPaneResizeSplitter")!;
                Assert.IsTrue(viewModel.IsSplitPaneActive);
                Assert.IsTrue(articleList.IsVisible);
                Assert.IsFalse(viewModel.IsReadingViewVisible);
                Assert.AreEqual(Visibility.Visible, placeholder.Visibility);
                Assert.AreEqual(Visibility.Visible, splitter.Visibility);
            }
            finally
            {
                window.Close();
                WpfTestHost.Application.MainWindow = null;
            }
        });
    }

    [TestMethod]
    public void SplitPaneToggleMovesBetweenFullPageReaderAndArticleList()
    {
        WpfTestHost.Run(() =>
        {
            var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
            viewModel.SelectArticleCommand.Execute(viewModel.VisibleArticles[0]);
            var window = CreateTestMainWindow(viewModel);
            window.Width = 1100;
            window.Height = 760;
            WpfTestHost.Application.MainWindow = window;

            try
            {
                window.Show();
                window.UpdateLayout();

                var articleList = (ArticleListView)window.FindName("ArticleListView")!;
                var articleReader = (ArticleReaderView)window.FindName("ArticleReaderView")!;
                var listToggle = (Button)articleList.FindName("ArticleListSplitPaneToggleButton")!;
                var readerToggle = (Button)articleReader.FindName("ReaderSplitPaneToggleButton")!;

                Assert.IsTrue(articleReader.IsVisible);
                Assert.AreEqual(Visibility.Visible, readerToggle.Visibility);
                Assert.IsFalse(listToggle.IsVisible);

                readerToggle.Command.Execute(readerToggle.CommandParameter);
                window.UpdateLayout();

                Assert.IsTrue(viewModel.IsSplitPaneActive);
                Assert.IsFalse(readerToggle.IsVisible);
                Assert.IsTrue(listToggle.IsVisible);

                listToggle.Command.Execute(listToggle.CommandParameter);
                window.UpdateLayout();

                Assert.IsFalse(viewModel.IsSplitPaneActive);
                Assert.IsTrue(articleReader.IsVisible);
                Assert.IsTrue(readerToggle.IsVisible);
                Assert.IsFalse(listToggle.IsVisible);
            }
            finally
            {
                window.Close();
                WpfTestHost.Application.MainWindow = null;
            }
        });
    }

    [TestMethod]
    public void SplitPaneUsesAvailableWorkspaceWidthRatherThanWholeWindowWidth()
    {
        WpfTestHost.Run(() =>
        {
            var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
            viewModel.ApplyPreferences(new ProfilePreferences(ReadingLayout: ProfileReadingLayout.SplitPane));
            viewModel.SelectArticleCommand.Execute(viewModel.VisibleArticles[0]);
            var window = CreateTestMainWindow(viewModel);
            window.Width = 1100;
            window.Height = 760;
            WpfTestHost.Application.MainWindow = window;

            try
            {
                window.Show();
                window.UpdateLayout();

                var workspace = (Grid)window.FindName("WorkspaceGrid")!;
                var readingWorkspace = (Grid)window.FindName("ReadingWorkspaceGrid")!;
                var articleList = (FrameworkElement)window.FindName("ArticleListView")!;
                var articleReader = (FrameworkElement)window.FindName("ArticleReaderView")!;
                var splitter = (GridSplitter)window.FindName("SplitPaneResizeSplitter")!;
                Assert.IsTrue(
                    workspace.ActualWidth >= 680,
                    $"Expected enough reading workspace at 1100 DIPs, got {workspace.ActualWidth}.");
                Assert.IsTrue(viewModel.IsSplitPaneActive);
                Assert.AreEqual(Visibility.Visible, splitter.Visibility);
                Assert.AreEqual(GridResizeBehavior.PreviousAndNext, splitter.ResizeBehavior);
                Assert.AreEqual(0.45, readingWorkspace.ColumnDefinitions[0].ActualWidth /
                    (readingWorkspace.ColumnDefinitions[0].ActualWidth + readingWorkspace.ColumnDefinitions[2].ActualWidth), 0.02);
                Assert.AreEqual(1, Grid.GetColumnSpan(articleList));
                Assert.AreEqual(2, Grid.GetColumn(articleReader));
                Assert.AreEqual(1, Grid.GetColumnSpan(articleReader));
                Assert.IsTrue(articleList.IsVisible);
                Assert.IsTrue(articleReader.IsVisible);

                readingWorkspace.ColumnDefinitions[0].Width = new GridLength(0.46, GridUnitType.Star);
                readingWorkspace.ColumnDefinitions[2].Width = new GridLength(0.54, GridUnitType.Star);
                window.UpdateLayout();
                splitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false));
                Assert.AreEqual(0.46, readingWorkspace.ColumnDefinitions[0].Width.Value, 0.001);
                Assert.AreEqual(0.54, readingWorkspace.ColumnDefinitions[2].Width.Value, 0.001);
                Assert.AreEqual(0.46, viewModel.SplitPaneListRatio, 0.001);
            }
            finally
            {
                window.Close();
                WpfTestHost.Application.MainWindow = null;
            }
        });
    }

    [TestMethod]
    public void ReaderPreferenceChangesPreserveTheCurrentFeedAndDoNotRebuildItsArticleList()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        viewModel.ApplyPreferences(new ProfilePreferences());
        viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "folder:Gaming"));
        var article = viewModel.VisibleArticles[0];
        viewModel.SelectArticleCommand.Execute(article);
        viewModel.UpdateSplitPaneViewportWidth(1280);
        var activeRoute = viewModel.ActiveRoute;
        var collectionChangeCount = 0;
        viewModel.VisibleArticles.CollectionChanged += (_, _) => collectionChangeCount++;

        viewModel.ApplyPreferences(new ProfilePreferences(
            ReadingLayout: ProfileReadingLayout.SplitPane,
            ReaderTheme: ProfileReaderTheme.Warm,
            ReaderTextSize: ProfileReaderTextSize.Large,
            ReaderLineSpacing: ProfileReaderLineSpacing.Relaxed,
            ReaderFontFamily: ProfileReaderFontFamily.Monospace));

        Assert.AreEqual(activeRoute, viewModel.ActiveRoute);
        Assert.AreEqual(0, collectionChangeCount);
        Assert.IsTrue(viewModel.IsSplitPaneActive);
        Assert.AreEqual(ProfileReaderTheme.Warm, viewModel.ReaderTheme);
        Assert.AreEqual(ProfileReaderTextSize.Large, viewModel.ReaderTextSize);
        Assert.AreEqual(ProfileReaderLineSpacing.Relaxed, viewModel.ReaderLineSpacing);
        Assert.AreEqual(ProfileReaderFontFamily.Monospace, viewModel.ReaderFontFamily);
    }

    [TestMethod]
    public void UnreadCountsAndContextActionsStayInSyncWithArticleState()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var article = viewModel.VisibleArticles[0];
        var allArticlesLink = viewModel.FeedLinks.Single(link => link.Route == "All");
        var gamingFolderLink = viewModel.FeedLinks.Single(link => link.Route == "folder:Gaming");
        var reviewsTagLink = viewModel.TagLinks.Single(link => link.Route == "tag:Reviews");

        Assert.AreEqual(3, allArticlesLink.UnreadCount);
        Assert.AreEqual(2, gamingFolderLink.UnreadCount);
        Assert.AreEqual(2, reviewsTagLink.UnreadCount);

        viewModel.ToggleArticleReadCommand.Execute(article);

        Assert.IsTrue(article.IsRead);
        Assert.IsNull(viewModel.SelectedArticle);
        Assert.AreEqual(2, allArticlesLink.UnreadCount);
        Assert.AreEqual(1, gamingFolderLink.UnreadCount);
        Assert.AreEqual(1, reviewsTagLink.UnreadCount);

        viewModel.ToggleArticleSavedCommand.Execute(article);
        Assert.IsTrue(article.IsSaved);
    }

    [TestMethod]
    public void SidebarFeedRowReservesActionSpaceOnlyWhileHovered()
    {
        WpfTestHost.Run(() =>
        {
            var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
            viewModel.IsListView = true;
            var parentFolder = new SidebarLink("folder:Comics", "Comics", string.Empty, "(1)")
            {
                IsExpanded = true
            };
            viewModel.FeedLinks.Add(parentFolder);
            var feedLink = new SidebarLink(
                "feed:comics",
                "Comics by Keith Knight",
                "\uE774",
                indentLevel: 2,
                parentFolder: parentFolder)
            {
                UnreadCount = 95
            };
            viewModel.FeedLinks.Add(feedLink);
            var window = CreateTestMainWindow(viewModel);
            window.Width = 1100;
            window.Height = 760;

            try
            {
                window.Show();
                window.UpdateLayout();

                var feedRow = FindVisualChildren<Grid>(window)
                    .Single(grid => grid.Name == "SidebarLinkRoot" && ReferenceEquals(grid.DataContext, feedLink));
                var folderRow = FindVisualChildren<Grid>(window)
                    .Single(grid => grid.Name == "SidebarLinkRoot" && ReferenceEquals(grid.DataContext, parentFolder));
                var folderCount = FindVisualChildren<TextBlock>(folderRow)
                    .Single(textBlock => textBlock.Text == parentFolder.Count);
                var navigationButton = feedRow.Children.OfType<Button>()
                    .Single(button => Grid.GetColumn(button) == 0 &&
                                      ReferenceEquals(button.Command, viewModel.ActivateSidebarLinkCommand));
                var unreadCountButton = feedRow.Children.OfType<Button>().Single(button => Grid.GetColumn(button) == 2);
                var actionPanel = feedRow.Children.OfType<StackPanel>().Single();
                var label = FindVisualChildren<TextBlock>(feedRow).Single(textBlock => textBlock.Text == feedLink.Label);
                var unreadBadge = FindVisualChildren<Border>(feedRow)
                    .Single(border => Equals(border.ToolTip, feedLink.UnreadCountAutomationName));

                Assert.AreSame(navigationButton.Command, unreadCountButton.Command);
                Assert.AreEqual(Visibility.Collapsed, folderCount.Visibility);
                Assert.AreEqual(Visibility.Collapsed, actionPanel.Visibility);
                Assert.AreEqual(0, feedRow.ColumnDefinitions[1].ActualWidth, 0.1);
                var labelWidthWithoutHover = label.ActualWidth;

                static Rect GetBounds(FrameworkElement element, Visual relativeTo) =>
                    element.TransformToVisual(relativeTo).TransformBounds(new Rect(new Point(), element.RenderSize));

                var unreadBadgeBounds = GetBounds(unreadBadge, feedRow);
                Assert.IsTrue(feedRow.ActualWidth - unreadBadgeBounds.Right < 20);

                folderRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, folderCount.Visibility);
                folderRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, folderCount.Visibility);

                feedRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
                window.UpdateLayout();

                Assert.AreEqual(Visibility.Visible, actionPanel.Visibility);
                Assert.IsTrue(feedRow.ColumnDefinitions[1].ActualWidth > 0);
                Assert.IsTrue(label.ActualWidth < labelWidthWithoutHover);
                var tagButton = FindVisualChildren<Button>(actionPanel)
                    .Single(button => AutomationProperties.GetName(button) == "Manage tags for Comics by Keith Knight");
                var unfollowButton = FindVisualChildren<Button>(actionPanel)
                    .Single(button => AutomationProperties.GetName(button) == "Unfollow Comics by Keith Knight");
                var destructiveBrush = (SolidColorBrush)window.FindResource("DestructiveBrush");
                var neutralActionBrush = (SolidColorBrush)window.FindResource("NeutralActionBrush");
                Assert.AreEqual(FontWeights.Bold, tagButton.FontWeight);
                Assert.AreEqual(neutralActionBrush.Color, ((SolidColorBrush)tagButton.Foreground).Color);
                Assert.AreEqual(FontWeights.Bold, unfollowButton.FontWeight);
                Assert.AreEqual(destructiveBrush.Color, ((SolidColorBrush)unfollowButton.Foreground).Color);
                Assert.IsTrue(GetBounds(tagButton, feedRow).Right <= GetBounds(unfollowButton, feedRow).Left);
                Assert.IsTrue(GetBounds(unfollowButton, feedRow).Right <= GetBounds(unreadBadge, feedRow).Left);

                feedRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                window.UpdateLayout();

                Assert.AreEqual(Visibility.Collapsed, actionPanel.Visibility);
                Assert.AreEqual(0, feedRow.ColumnDefinitions[1].ActualWidth, 0.1);

                viewModel.SetSidebarPanelWidth(MainWindowViewModel.MinimumSidebarWidth);
                window.Width = 900;
                window.UpdateLayout();
                feedRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
                window.UpdateLayout();
                Assert.IsTrue(
                    GetBounds(unfollowButton, feedRow).Right <= GetBounds(unreadBadge, feedRow).Left,
                    "Feed actions must remain to the left of the unread badge at the narrow sidebar width.");
                feedRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                window.UpdateLayout();

                var deleteButton = FindVisualChildren<Button>(folderRow)
                    .Single(button => AutomationProperties.GetName(button) == "Delete folder");
                folderRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
                window.UpdateLayout();
                Assert.AreEqual(FontWeights.Bold, deleteButton.FontWeight);
                Assert.AreEqual(destructiveBrush.Color, ((SolidColorBrush)deleteButton.Foreground).Color);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void CommandPaletteItemsRunPresentationChangesAndSearchEmptyActionClearsQuery()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var commands = viewModel.CreateCommandPaletteItems();
        commands.Single(item => item.Label == "Show magazine view").Execute();
        Assert.IsTrue(viewModel.IsMagazineView);

        viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Search"));
        viewModel.SearchQuery = "no matching title";
        Assert.IsTrue(viewModel.IsArticleListEmpty);
        Assert.AreEqual("No matching articles", viewModel.ArticleListEmptyTitle);
        Assert.AreEqual("Clear search", viewModel.EmptyStateActionLabel);
        viewModel.EmptyStateActionCommand.Execute(null);
        Assert.AreEqual(string.Empty, viewModel.SearchQuery);
    }

    [TestMethod]
    public void ManagedFeedPreviewCheckButtonClosesAndChecksFeed()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            try
            {
                CatalogFeedPreviewWindow? preview = null;
                var checkFeedRequested = false;
                var previewWasClosedBeforeCheck = false;
                preview = new CatalogFeedPreviewWindow(
                    new CatalogFeedListItem("managed-check-preview", "Managed check preview", "https://example.com/check.xml", null, "Comics"),
                    _ => Task.FromResult(new CatalogFeedPreview("Managed check preview", "Comics", null, [])),
                    checkFeed: () =>
                    {
                        checkFeedRequested = true;
                        previewWasClosedBeforeCheck = !preview!.IsVisible;
                        return Task.CompletedTask;
                    });
                preview.Show();
                preview.UpdateLayout();

                Assert.AreEqual(string.Empty, preview.Title);
                Assert.AreEqual(WindowStyle.None, preview.WindowStyle);
                var checkFeedButton = (Button)preview.FindName("CheckFeedButton");
                var closeButton = (Button)preview.FindName("ClosePreviewButton");
                Assert.AreEqual(Visibility.Visible, checkFeedButton.Visibility);
                Assert.AreEqual(Visibility.Visible, closeButton.Visibility);
                var previewActions = (StackPanel)VisualTreeHelper.GetParent(checkFeedButton);
                Assert.AreEqual(previewActions.Children.IndexOf(checkFeedButton) + 1, previewActions.Children.IndexOf(closeButton));
                Assert.AreEqual("Check Feed", checkFeedButton.Content);
                Assert.AreEqual("Check feed", AutomationProperties.GetName(checkFeedButton));
                Assert.AreEqual("#FF218739", checkFeedButton.Foreground.ToString());

                checkFeedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, checkFeedButton));

                Assert.IsTrue(checkFeedRequested);
                Assert.IsTrue(previewWasClosedBeforeCheck);
                Assert.IsFalse(preview.IsVisible);
                var styleFailures = VerifyAdditionalCustomChrome();
                Assert.AreEqual(0, styleFailures.Count, string.Join(Environment.NewLine, styleFailures));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void FeedPreviewShowsRawXmlOnlyAfterLoadFailure()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            try
            {
                const string rawXml = "<rss><channel><title>Feed XML</title></channel></rss>";
                var feed = new CatalogFeedListItem(
                    "invalid-preview",
                    "Invalid preview",
                    "https://example.com/invalid.xml",
                    null,
                    "Technology");
                var failedPreview = new CatalogFeedPreviewWindow(
                    feed,
                    _ => Task.FromException<CatalogFeedPreview>(new InvalidDataException("Invalid XML character.")),
                    loadRawFeedXml: _ => Task.FromResult(rawXml));
                failedPreview.Show();
                failedPreview.UpdateLayout();

                var showXmlButton = (Button)failedPreview.FindName("ShowRawXmlButton")!;
                Assert.AreEqual(Visibility.Visible, showXmlButton.Visibility);
                showXmlButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, showXmlButton));
                var rawWindow = System.Windows.Application.Current.Windows
                    .OfType<RawFeedWindow>()
                    .Single();
                Assert.AreEqual(feed.FeedUrl, ((TextBlock)rawWindow.FindName("FeedSourceText")!).Text);
                Assert.AreEqual(Visibility.Visible, ((Button)rawWindow.FindName("CopyXmlButton")!).Visibility);
                rawWindow.Close();
                failedPreview.Close();

                var successfulPreview = new CatalogFeedPreviewWindow(
                    feed,
                    _ => Task.FromResult(new CatalogFeedPreview(feed.Name, feed.CategoryName, null, [])),
                    loadRawFeedXml: _ => Task.FromResult(rawXml));
                successfulPreview.Show();
                successfulPreview.UpdateLayout();
                Assert.AreEqual(
                    Visibility.Collapsed,
                    ((Button)successfulPreview.FindName("ShowRawXmlButton")!).Visibility);
                successfulPreview.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void ManagedFeedPreviewCloseButtonOnlyClosesThePreview()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            CatalogFeedPreviewWindow? preview = null;
            var deleteRequested = false;
            var checkRequested = false;
            try
            {
                preview = new CatalogFeedPreviewWindow(
                    new CatalogFeedListItem("managed-preview", "Managed preview", "https://example.com/feed.xml", null, "Comics"),
                    _ => Task.FromResult(new CatalogFeedPreview("Managed preview", "Comics", null, [])),
                    deleteFeed: () =>
                    {
                        deleteRequested = true;
                        return Task.CompletedTask;
                    },
                    checkFeed: () =>
                    {
                        checkRequested = true;
                        return Task.CompletedTask;
                    });
                preview.Show();
                preview.UpdateLayout();

                var closeButton = (Button)preview.FindName("ClosePreviewButton");
                var checkFeedButton = (Button)preview.FindName("CheckFeedButton");
                Assert.AreEqual(Visibility.Visible, closeButton.Visibility);
                Assert.AreEqual(Visibility.Visible, checkFeedButton.Visibility);
                var previewActions = (StackPanel)VisualTreeHelper.GetParent(checkFeedButton);
                Assert.AreEqual(
                    previewActions.Children.IndexOf(checkFeedButton) + 1,
                    previewActions.Children.IndexOf(closeButton));

                closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, closeButton));

                Assert.IsFalse(preview.IsVisible);
                Assert.IsFalse(deleteRequested);
                Assert.IsFalse(checkRequested);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (preview?.IsVisible == true)
                {
                    preview.Close();
                }
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    private static IReadOnlyList<string> VerifyAdditionalCustomChrome()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var styleFailures = new List<string>();

        void ScheduleClose(
            Window window,
            string automationName,
            bool verifyCompactFrame = false,
            string expectedTitle = "",
            Action<Window>? inspect = null)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Assert.AreEqual(expectedTitle, window.Title);
                Assert.AreEqual(WindowStyle.None, window.WindowStyle);
                if (verifyCompactFrame)
                {
                    Assert.IsTrue(FindVisualChildren<Border>(window)
                        .Any(border => border.CornerRadius.TopLeft == 6));
                }

                inspect?.Invoke(window);
                var closeButton = FindVisualChildren<Button>(window)
                    .Single(button => AutomationProperties.GetName(button) == automationName);
                closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
        }

        void ShowAndCloseModal(
            Window window,
            string automationName,
            bool verifyCompactFrame = false,
            string expectedTitle = "",
            Action<Window>? inspect = null)
        {
            ScheduleClose(window, automationName, verifyCompactFrame, expectedTitle, inspect);
            Assert.IsFalse(window.ShowDialog() == true);
        }

        var feedTagEditor = new FeedTagEditorWindow("Gaming News", ["News", "Reviews"], ["Reviews"]);
        feedTagEditor.UpdateLayout();
        Assert.AreEqual(string.Empty, feedTagEditor.Title);
        Assert.AreEqual(WindowStyle.None, feedTagEditor.WindowStyle);
        var tagChoices = (ItemsControl)feedTagEditor.FindName("TagChoicesList")!;
        Assert.AreEqual(2, tagChoices.Items.Count);
        Assert.AreEqual("Gaming News", ((TextBlock)feedTagEditor.FindName("FeedNameText")!).Text);
        var addTagButton = (Button)feedTagEditor.FindName("AddTagButton")!;
        Assert.AreEqual("Add tag", AutomationProperties.GetName(addTagButton));
        var newTagTextBox = (TextBox)feedTagEditor.FindName("NewTagTextBox")!;
        newTagTextBox.Text = " Culture ";
        addTagButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, addTagButton));
        var existingTagChoice = tagChoices.Items.OfType<FeedTagChoice>().Single(choice => choice.Name == "News");
        var assignedStateChanged = false;
        existingTagChoice.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FeedTagChoice.IsAssigned))
            {
                assignedStateChanged = true;
            }
        };
        newTagTextBox.Text = " news ";
        addTagButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, addTagButton));
        Assert.IsTrue(assignedStateChanged);
        CollectionAssert.AreEquivalent(new[] { "Culture", "News", "Reviews" }, feedTagEditor.SelectedTagNames.ToArray());
        feedTagEditor.Close();

        var mainViewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        mainViewModel.IsListView = true;
        var tagManagedParentFolder = new SidebarLink("folder:Gaming", "Gaming", string.Empty)
        {
            IsExpanded = true
        };
        var tagManagedFeedLink = new SidebarLink(
            "feed:tag-feed",
            "Gaming News",
            "\uE774",
            indentLevel: 2,
            parentFolder: tagManagedParentFolder)
        {
            UnreadCount = 108
        };
        mainViewModel.FeedLinks.Add(tagManagedFeedLink);
        mainViewModel.VisibleArticles.Add(new ArticleRowViewModel(
            "Tagged article",
            "Gaming News",
            DateTimeOffset.Now,
            "Gaming",
            ["Reviews", "Analysis"],
            "Article summary",
            feedId: "tag-feed",
            topics: [new ArticleCategory("Publisher topic")]));
        var mainWindow = CreateTestMainWindow(mainViewModel);
        mainWindow.Show();
        mainWindow.UpdateLayout();
        Assert.AreEqual(WindowStyle.None, mainWindow.WindowStyle);
        Assert.AreEqual(ResizeMode.CanResize, mainWindow.ResizeMode);
        Assert.IsTrue(mainWindow.AllowsTransparency);
        Assert.AreEqual(Colors.Transparent, ((SolidColorBrush)mainWindow.Background).Color);
        var windowResizeFrame = (Border)mainWindow.FindName("WindowResizeFrame");
        Assert.AreEqual(18, windowResizeFrame.CornerRadius.TopLeft);
        Assert.AreSame(mainWindow.FindResource("WindowFrameBrush"), windowResizeFrame.Background);
        var mainWindowSurface = (Border)mainWindow.FindName("MainWindowSurface");
        Assert.AreEqual(18, mainWindowSurface.CornerRadius.TopLeft);
        Assert.AreEqual(new Thickness(1), mainWindowSurface.BorderThickness);
        var shellGrid = (Grid)mainWindow.FindName("ShellGrid");
        Assert.IsTrue(shellGrid.Clip is RectangleGeometry { RadiusX: 17, RadiusY: 17 });
        var tagManagedFeedRow = FindVisualChildren<Grid>(mainWindow)
            .Single(grid => grid.Name == "SidebarLinkRoot" && ReferenceEquals(grid.DataContext, tagManagedFeedLink));
        var manageTagsButton = FindVisualChildren<Button>(tagManagedFeedRow)
            .Single(button => AutomationProperties.GetName(button) == "Manage tags for Gaming News");
        tagManagedFeedRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
        mainWindow.UpdateLayout();
        Assert.AreEqual(1, manageTagsButton.Opacity);
        var unfollowFeedButton = FindVisualChildren<Button>(tagManagedFeedRow)
            .Single(button => AutomationProperties.GetName(button) == "Unfollow Gaming News");
        var unreadCountBadge = FindVisualChildren<Border>(tagManagedFeedRow)
            .Single(border => Equals(border.ToolTip, tagManagedFeedLink.UnreadCountAutomationName));
        Assert.AreEqual(Visibility.Visible, unreadCountBadge.Visibility);
        Assert.AreEqual("108", FindVisualChildren<TextBlock>(unreadCountBadge).Single().Text);

        static Rect GetBounds(FrameworkElement element, Visual relativeTo) =>
            element.TransformToVisual(relativeTo).TransformBounds(new Rect(new Point(), element.RenderSize));

        var unreadCountBounds = GetBounds(unreadCountBadge, tagManagedFeedRow);
        Assert.IsTrue(unreadCountBounds.Width > 0 && unreadCountBounds.Height > 0);
        Assert.IsFalse(
            unreadCountBounds.IntersectsWith(GetBounds(manageTagsButton, tagManagedFeedRow)),
            "The manage-tags action overlaps the unread-count badge.");
        Assert.IsFalse(
            unreadCountBounds.IntersectsWith(GetBounds(unfollowFeedButton, tagManagedFeedRow)),
            "The unfollow action overlaps the unread-count badge.");
        var articleLabels = FindVisualChildren<TextBlock>(mainWindow)
            .Select(textBlock => string.Join(
                " ",
                new TextRange(textBlock.ContentStart, textBlock.ContentEnd).Text
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .ToArray();
        Assert.IsTrue(
            articleLabels.Contains("Tagged article"),
            $"Visible articles: {mainViewModel.VisibleArticles.Count}; rendered labels: {string.Join(" | ", articleLabels)}");
        var taggedArticle = mainViewModel.VisibleArticles.Single(article => article.Title == "Tagged article");
        var articleRowsList = FindArticleListElement<ListBox>(mainWindow, "ArticleRowsList");
        articleRowsList.ScrollIntoView(taggedArticle);
        articleRowsList.UpdateLayout();
        var articleRowContainer = articleRowsList.ItemContainerGenerator.ContainerFromItem(taggedArticle)
            ?? throw new AssertFailedException("The title-only article row was not created.");
        var rowReadMarker = FindVisualChildren<ContentControl>(articleRowContainer)
            .Single(control => control.Name == "ArticleRowReadMarker");
        Assert.AreEqual(Visibility.Collapsed, rowReadMarker.Visibility);
        var rowActionButton = FindVisualChildren<Button>(articleRowContainer)
            .Single(button => ReferenceEquals(button.Command, mainViewModel.SelectArticleCommand));
        Assert.IsNotNull(rowActionButton.ContextMenu);
        Assert.AreSame(mainViewModel, rowActionButton.Tag);
        Assert.AreEqual("Open in reader", ((MenuItem)rowActionButton.ContextMenu.Items[0]).Header);
        var rowContextMenu = rowActionButton.ContextMenu;
        rowContextMenu.PlacementTarget = rowActionButton;
        rowContextMenu.IsOpen = true;
        mainWindow.UpdateLayout();
        var rowReadMenuItem = rowContextMenu.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, "Toggle read status"));
        Assert.AreSame(mainViewModel.ToggleArticleReadCommand, rowReadMenuItem.Command);
        Assert.AreSame(taggedArticle, rowReadMenuItem.CommandParameter);
        rowContextMenu.IsOpen = false;
        var articleRowLabels = FindVisualChildren<TextBlock>(articleRowContainer)
            .Select(textBlock => string.Join(
                " ",
                new TextRange(textBlock.ContentStart, textBlock.ContentEnd).Text
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .ToArray();
        Assert.IsTrue(articleRowLabels.Any(label => label.Contains("Tagged article", StringComparison.Ordinal)));
        Assert.IsFalse(articleRowLabels.Any(label => label.Contains("Article summary", StringComparison.Ordinal)));
        Assert.IsFalse(articleRowLabels.Any(label => label.StartsWith("Topics:", StringComparison.Ordinal)));
        Assert.IsFalse(articleRowLabels.Any(label => label.StartsWith("Feed tags:", StringComparison.Ordinal)));
        var rowSaveToggle = FindVisualChildren<ToggleButton>(articleRowContainer)
            .Single(toggle => AutomationProperties.GetName(toggle) == "Add to read later");
        Assert.AreEqual(0, rowSaveToggle.Opacity);
        Assert.IsFalse(rowSaveToggle.IsHitTestVisible);
        taggedArticle.IsSaved = true;
        mainWindow.UpdateLayout();
        Assert.AreEqual(1, rowSaveToggle.Opacity);
        Assert.IsTrue(rowSaveToggle.IsHitTestVisible);
        taggedArticle.IsSaved = false;
        mainWindow.UpdateLayout();
        Assert.AreEqual(0, rowSaveToggle.Opacity);
        mainViewModel.IsCardsView = true;
        mainWindow.UpdateLayout();
        var articleCardsList = FindArticleListElement<ListBox>(mainWindow, "ArticleFolderCardsList");
        articleCardsList.ScrollIntoView(taggedArticle);
        mainWindow.UpdateLayout();
        articleCardsList.UpdateLayout();
        var cardContainer = (FrameworkElement?)articleCardsList.ItemContainerGenerator.ContainerFromItem(taggedArticle)
            ?? throw new AssertFailedException(
                $"The tagged article card was not realized; list visibility: {articleCardsList.Visibility}; " +
                $"list items: {articleCardsList.Items.Count}; articles: {mainViewModel.VisibleArticles.Count}; " +
                $"cards: {mainViewModel.IsCardsView}; item index: {articleCardsList.Items.IndexOf(taggedArticle)}.");
        var cardRoot = FindVisualChildren<Grid>(cardContainer).Single(grid => grid.Name == "ArticleCardRoot");
        var cardSurface = FindVisualChildren<Border>(cardContainer).Single(border => border.Name == "ArticleCardSurface");
        Assert.AreEqual(12, cardSurface.CornerRadius.TopLeft);
        Assert.IsTrue(
            cardContainer.ActualHeight >= cardRoot.ActualHeight + cardRoot.Margin.Bottom - 1,
            $"The card container is {cardContainer.ActualHeight}px high but the card and its bottom margin need " +
            $"{cardRoot.ActualHeight + cardRoot.Margin.Bottom}px.");
        var topicChips = FindVisualChildren<ItemsControl>(cardSurface)
            .Single(control => AutomationProperties.GetName(control) == "Publisher topics");
        var feedTagChips = FindVisualChildren<ItemsControl>(cardSurface)
            .Single(control => AutomationProperties.GetName(control) == "User tags");
        Assert.AreEqual(1, topicChips.Items.Count);
        Assert.AreEqual(2, feedTagChips.Items.Count);
        var cardTopicPill = FindVisualChildren<Button>(topicChips)
            .Single(button => AutomationProperties.GetName(button) == "Publisher topic");
        Assert.AreSame(mainViewModel.SelectArticleTopicCommand, cardTopicPill.Command);
        Assert.AreEqual(taggedArticle.TopicChips.Single(), cardTopicPill.CommandParameter);
        var topicPillBackground = cardTopicPill.Background as SolidColorBrush
            ?? throw new AssertFailedException("The publisher-topic pill background was not a solid color.");
        Assert.AreEqual(Color.FromRgb(0xE8, 0xF2, 0xEC), topicPillBackground.Color);
        var cardFeedTagPill = FindVisualChildren<Button>(feedTagChips)
            .Single(button => AutomationProperties.GetName(button) == taggedArticle.FeedTagChips[0].Name);
        Assert.AreSame(mainViewModel.SelectFeedTagCommand, cardFeedTagPill.Command);
        Assert.AreEqual(taggedArticle.FeedTagChips[0], cardFeedTagPill.CommandParameter);
        var tagPillBackground = cardFeedTagPill.Background as SolidColorBrush
            ?? throw new AssertFailedException("The user-tag pill background was not a solid color.");
        Assert.AreEqual(Color.FromRgb(0xEA, 0xF1, 0xF8), tagPillBackground.Color);
        foreach (var metadataRow in new[] { topicChips, feedTagChips })
        {
            var rowBottom = metadataRow.TransformToAncestor(cardSurface)
                .Transform(new Point(0, metadataRow.ActualHeight)).Y;
            var bottomClearance = cardSurface.ActualHeight - rowBottom;
            Assert.IsTrue(
                bottomClearance >= 10,
                $"A metadata row has {bottomClearance}px of bottom clearance in a {cardSurface.ActualHeight}px card.");
        }
        var unreadIndicator = FindVisualChildren<Ellipse>(cardRoot)
            .Single(ellipse => AutomationProperties.GetName(ellipse) == "Unread article");
        Assert.AreEqual(Visibility.Visible, unreadIndicator.Visibility);
        var saveToggle = FindVisualChildren<ToggleButton>(cardRoot)
            .Single(toggle => AutomationProperties.GetName(toggle) == "Add to read later");
        saveToggle.ApplyTemplate();
        var bookmarkIcon = saveToggle.Template.FindName("BookmarkIcon", saveToggle) as System.Windows.Shapes.Path
            ?? throw new AssertFailedException("The read-later toggle does not expose its bookmark icon.");
        var cardButton = FindVisualChildren<Button>(cardRoot)
            .Single(button => AutomationProperties.GetName(button) == taggedArticle.Title);
        Assert.IsNotNull(cardButton.ContextMenu);
        mainWindow.Activate();
        Assert.IsTrue(cardButton.Focus());
        mainWindow.UpdateLayout();
        Assert.AreEqual(Visibility.Visible, saveToggle.Visibility);
        Assert.AreSame(mainWindow.FindResource("AccentBrush"), cardSurface.BorderBrush);
        Assert.AreEqual(new Thickness(2), cardSurface.BorderThickness);
        taggedArticle.IsSaved = true;
        mainWindow.UpdateLayout();
        Assert.IsTrue(saveToggle.IsChecked);
        Assert.IsTrue(taggedArticle.IsSaved);
        Assert.AreSame(mainWindow.FindResource("AccentBrush"), bookmarkIcon.Fill);
        Assert.AreEqual("Remove from read later", AutomationProperties.GetName(saveToggle));
        Assert.IsNull(mainViewModel.SelectedArticle);
        taggedArticle.IsRead = true;
        mainWindow.UpdateLayout();
        Assert.AreEqual(Visibility.Collapsed, unreadIndicator.Visibility);
        Assert.AreEqual(Visibility.Visible, rowReadMarker.Visibility);
        var cardReadMarker = FindVisualChildren<ContentControl>(cardRoot)
            .Single(control => control.Name == "ArticleCardReadIndicator");
        Assert.AreEqual(Visibility.Visible, cardReadMarker.Visibility);
        mainViewModel.IsMagazineView = true;
        mainWindow.UpdateLayout();
        var magazineList = FindArticleListElement<ListBox>(mainWindow, "ArticleMagazineList");
        magazineList.ScrollIntoView(taggedArticle);
        magazineList.UpdateLayout();
        var magazineContainer = magazineList.ItemContainerGenerator.ContainerFromItem(taggedArticle)
            ?? throw new AssertFailedException("The tagged magazine article was not created.");
        var magazineReadMarker = FindVisualChildren<ContentControl>(magazineContainer)
            .Single(control => control.Name == "MagazineReadMarker");
        Assert.AreEqual(Visibility.Visible, magazineReadMarker.Visibility);
        Assert.IsTrue(FindVisualChildren<Button>(magazineContainer)
            .Any(button => ReferenceEquals(button.Command, mainViewModel.SelectArticleCommand) &&
                           button.ContextMenu is not null));
        var magazineTopicPills = FindVisualChildren<ItemsControl>(magazineContainer)
            .Single(control => AutomationProperties.GetName(control) == "Publisher topics");
        var magazineTopicPill = FindVisualChildren<Button>(magazineTopicPills)
            .Single(button => AutomationProperties.GetName(button) == "Publisher topic");
        Assert.AreSame(mainViewModel.SelectArticleTopicCommand, magazineTopicPill.Command);
        Assert.AreEqual(taggedArticle.TopicChips.Single(), magazineTopicPill.CommandParameter);
        var magazineFeedTagPills = FindVisualChildren<ItemsControl>(magazineContainer)
            .Single(control => AutomationProperties.GetName(control) == "User tags");
        Assert.AreEqual(2, magazineFeedTagPills.Items.Count);
        var magazineFeedTagPill = FindVisualChildren<Button>(magazineFeedTagPills)
            .Single(button => AutomationProperties.GetName(button) == taggedArticle.FeedTagChips[0].Name);
        Assert.AreSame(mainViewModel.SelectFeedTagCommand, magazineFeedTagPill.Command);
        Assert.AreEqual(taggedArticle.FeedTagChips[0], magazineFeedTagPill.CommandParameter);
        mainViewModel.IsListView = true;
        mainWindow.UpdateLayout();
        mainViewModel.IsCardsView = false;
        mainWindow.UpdateLayout();
        var articleTopicFilter = FindArticleListElement<ComboBox>(mainWindow, "ArticleTopicFilterComboBox");
        Assert.AreEqual("Filter articles by topic", AutomationProperties.GetName(articleTopicFilter));
        Assert.AreEqual(nameof(ArticleTopicOption.DisplayName), articleTopicFilter.DisplayMemberPath);
        if (!FindVisualChildren<TextBlock>(articleTopicFilter)
            .Any(textBlock => textBlock.Text == mainViewModel.ArticleTopicOptions[0].DisplayName))
        {
            styleFailures.Add("The article topic ComboBox does not display its explicit option label.");
        }
        CollectionAssert.IsSubsetOf(
            new[] { "Minimize window", "Maximize window", "Restore window", "Close window" },
            FindVisualChildren<Button>(mainWindow).Select(button => AutomationProperties.GetName(button)).ToArray());
        var windowButtons = FindVisualChildren<Button>(mainWindow)
            .Where(button => AutomationProperties.GetName(button) is "Maximize window" or "Restore window" or "Minimize window" or "Close window")
            .ToDictionary(button => AutomationProperties.GetName(button));
        windowButtons["Maximize window"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(WindowState.Maximized, mainWindow.WindowState);
        Assert.AreEqual(0, windowResizeFrame.CornerRadius.TopLeft);
        windowButtons["Restore window"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(WindowState.Normal, mainWindow.WindowState);
        Assert.AreEqual(18, windowResizeFrame.CornerRadius.TopLeft);
        windowButtons["Minimize window"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(WindowState.Minimized, mainWindow.WindowState);
        mainWindow.WindowState = WindowState.Normal;
        var sidebarControl = (UserControl)mainWindow.FindName("SidebarView");
        var sidebarPinButton = (Button)sidebarControl.FindName("SidebarPinButton");
        var sidebarPeekButton = (Button)sidebarControl.FindName("SidebarPeekButton");
        Assert.AreEqual(1, Grid.GetColumn(sidebarPinButton));
        Assert.AreEqual(sidebarPinButton.ToolTip, AutomationProperties.GetName(sidebarPinButton));
        Assert.AreEqual(sidebarPeekButton.ToolTip, AutomationProperties.GetName(sidebarPeekButton));
        mainViewModel.SelectArticleCommand.Execute(taggedArticle);
        mainWindow.UpdateLayout();
        var readerActionsButton = FindReaderElement<Button>(mainWindow, "SelectedArticleActionsButton");
        Assert.AreSame(taggedArticle, readerActionsButton.DataContext);
        Assert.AreSame(mainViewModel, readerActionsButton.Tag);
        readerActionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, readerActionsButton));
        var readerContextMenu = readerActionsButton.ContextMenu
            ?? throw new AssertFailedException("The reader action menu is unavailable.");
        Assert.IsTrue(readerContextMenu.IsOpen);
        var readerReadMenuItem = readerContextMenu.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, "Toggle read status"));
        Assert.AreSame(mainViewModel.ToggleArticleReadCommand, readerReadMenuItem.Command);
        Assert.AreSame(taggedArticle, readerReadMenuItem.CommandParameter);
        readerContextMenu.IsOpen = false;
        var selectedFeedName = FindReaderElement<TextBlock>(mainWindow, "SelectedArticleFeedName");
        Assert.AreEqual(Visibility.Visible, selectedFeedName.Visibility);
        var feedNameText = string.Join(
            " ",
            new TextRange(selectedFeedName.ContentStart, selectedFeedName.ContentEnd).Text
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.AreEqual("Feed:", feedNameText);
        var feedNameLink = selectedFeedName.Inlines.OfType<Hyperlink>().Single();
        Assert.AreEqual("Gaming News", feedNameLink.Inlines.OfType<Run>().Single().Text);
        Assert.AreEqual("Open feed Gaming News", AutomationProperties.GetName(feedNameLink));
        var feedCommand = feedNameLink.Command
            ?? throw new AssertFailedException("The article feed link has no navigation command.");
        var readerTopicPills = FindReaderElement<ItemsControl>(mainWindow, "SelectedArticleTopicPills");
        Assert.AreEqual(1, readerTopicPills.Items.Count);
        var readerTopicPill = FindVisualChildren<Button>(readerTopicPills)
            .Single(button => AutomationProperties.GetName(button) == "Publisher topic");
        Assert.AreSame(mainViewModel.SelectArticleTopicCommand, readerTopicPill.Command);
        Assert.AreEqual(taggedArticle.TopicChips.Single(), readerTopicPill.CommandParameter);
        var readerFeedTagPills = FindReaderElement<ItemsControl>(mainWindow, "SelectedArticleFeedTagPills");
        Assert.AreEqual(2, readerFeedTagPills.Items.Count);
        var readerFeedTagPill = FindVisualChildren<Button>(readerFeedTagPills)
            .Single(button => AutomationProperties.GetName(button) == taggedArticle.FeedTagChips[0].Name);
        Assert.AreSame(mainViewModel.SelectFeedTagCommand, readerFeedTagPill.Command);
        Assert.AreEqual(taggedArticle.FeedTagChips[0], readerFeedTagPill.CommandParameter);
        feedCommand.Execute(null);
        mainWindow.UpdateLayout();
        Assert.AreEqual("feed:tag-feed", mainViewModel.ActiveRoute);
        Assert.IsNull(mainViewModel.SelectedArticle);
        windowButtons["Close window"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsFalse(mainWindow.IsVisible);

        var catalogMainWindow = CreateTestMainWindow(
            new MainWindowViewModel(
                Profile.CreateCatalogMaster(),
                new CatalogService(new SqliteCatalogStore(":memory:"))));
        try
        {
            catalogMainWindow.Show();
            catalogMainWindow.UpdateLayout();
            var catalogAdminView = (UserControl)catalogMainWindow.FindName("CatalogAdminView")!;
            var catalogTabs = (TabControl)catalogAdminView.FindName("CatalogManagementTabs")!;
            catalogTabs.ApplyTemplate();
            Assert.IsNotNull(catalogTabs.Template.FindName("HeaderPanel", catalogTabs));
            var feedsTab = (TabItem)catalogTabs.ItemContainerGenerator.ContainerFromIndex(0);
            feedsTab.ApplyTemplate();
            Assert.IsNotNull(feedsTab.Template.FindName("TabSurface", feedsTab));
            var categoryFilter = (ComboBox)catalogAdminView.FindName("CatalogManagementCategoryFilter")!;
            categoryFilter.ApplyTemplate();
            Assert.IsNotNull(categoryFilter.Template.FindName("FocusRing", categoryFilter));
            var selectedCategory = categoryFilter.SelectedItem as CatalogCategoryFilterOption;
            var categoryTextBlocks = FindVisualChildren<TextBlock>(categoryFilter)
                .Select(textBlock => textBlock.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
            if (selectedCategory is null || !categoryTextBlocks.Contains(selectedCategory.Name))
            {
                styleFailures.Add(
                    $"The catalog category ComboBox does not display the selected option Name. Selected='{selectedCategory?.Name ?? "<null>"}', text='{string.Join(" | ", categoryTextBlocks)}'.");
            }

            var catalogActionsButton = (Button)catalogAdminView.FindName("CatalogActionsButton")!;
            var catalogMenu = catalogActionsButton.ContextMenu!;
            try
            {
                catalogMenu.PlacementTarget = catalogActionsButton;
                catalogMenu.IsOpen = true;
                catalogMenu.ApplyTemplate();
                if (catalogMenu.Template.FindName("MenuFrame", catalogMenu) is null)
                {
                    styleFailures.Add("The catalog ContextMenu does not use the shared menu frame template.");
                }

                var importMenuItem = (MenuItem)catalogMenu.Items[0];
                importMenuItem.ApplyTemplate();
                if (importMenuItem.Template.FindName("MenuItemSurface", importMenuItem) is null)
                {
                    styleFailures.Add("The catalog MenuItem does not use the shared menu item template.");
                }
            }
            catch (Exception exception)
            {
                styleFailures.Add($"The catalog popup menu could not be rendered: {exception.Message}");
            }
            finally
            {
                catalogMenu.IsOpen = false;
            }

            Assert.IsFalse(FindVisualChildren<CheckBox>(catalogTabs)
                .Any(checkBox => checkBox.Content?.ToString() == "Metadata gaps only"));
        }
        finally
        {
            catalogMainWindow.Close();
        }

        var scrollbarTestWindow = new Window
        {
            Width = 220,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer
            {
                Width = 180,
                Height = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Height = 500 }
            }
        };
        try
        {
            scrollbarTestWindow.Show();
            scrollbarTestWindow.UpdateLayout();
            var verticalScrollBar = FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(scrollbarTestWindow)
                .Single(scrollBar => scrollBar.Orientation == Orientation.Vertical);
            if (verticalScrollBar.Margin != new Thickness(2))
            {
                styleFailures.Add("The vertical scrollbar is missing the 2 px viewport inset.");
            }

            if (verticalScrollBar.ActualHeight <= 100)
            {
                styleFailures.Add($"The vertical scrollbar track is too short: {verticalScrollBar.ActualHeight}px.");
            }
        }
        finally
        {
            scrollbarTestWindow.Close();
        }

        ShowAndCloseModal(
            new PreferencesWindow(new ProfilePreferences()),
            "Close dialog",
            verifyCompactFrame: true,
            inspect: window =>
            {
                ((RadioButton)window.FindName("ReadingNavigation")!).RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                window.UpdateLayout();
                var intervalComboBox = (ComboBox)window.FindName("AutoRefreshIntervalComboBox")!;
                intervalComboBox.ApplyTemplate();
                Assert.IsNotNull(intervalComboBox.Template.FindName("FocusRing", intervalComboBox));
                Assert.AreEqual("Refresh interval", AutomationProperties.GetName(intervalComboBox));
                Assert.AreEqual(
                    "Folder article limit",
                    AutomationProperties.GetName((TextBox)window.FindName("FolderArticleLimitBox")!));
                var folderArticleLimitBox = (TextBox)window.FindName("FolderArticleLimitBox")!;
                var refreshWhenOpenedCheckBox = (CheckBox)window.FindName("RefreshFeedsWhenOpenedCheckBox")!;
                var hideReadCheckBox = (CheckBox)window.FindName("HideReadArticlesCheckBox")!;
                hideReadCheckBox.ApplyTemplate();
                Assert.IsNotNull(hideReadCheckBox.Template.FindName("CheckBorder", hideReadCheckBox));
                Assert.AreEqual("Hide read articles", AutomationProperties.GetName(hideReadCheckBox));
                Assert.IsTrue(folderArticleLimitBox.Focus());
                Assert.IsTrue(folderArticleLimitBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.AreSame(refreshWhenOpenedCheckBox, Keyboard.FocusedElement);
                Assert.IsTrue(refreshWhenOpenedCheckBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.AreSame(intervalComboBox, Keyboard.FocusedElement);
            });
        const string rawFeedXml = "<rss xmlns:content=\"http://purl.org/rss/1.0/modules/content/\"><channel><title>Example feed title</title><item><content:encoded><![CDATA[<p>Encoded <strong>bold</strong><br/>after</p>]]></content:encoded><content>&lt;p&gt;heading&lt;/p&gt;&lt;br/&gt;after</content><details><summary>Nested label</summary></details></item></channel></rss>";
        ShowAndCloseModal(
            new RawFeedWindow("Example feed", rawFeedXml),
            "Close dialog",
            inspect: window =>
            {
                Assert.IsTrue(window.Topmost);
                var xmlTree = (TreeView)window.FindName("RawContentTreeView")!;
                Assert.AreEqual(1, xmlTree.Items.Count);
                var root = (RawXmlTreeNode)xmlTree.Items[0];
                Assert.AreEqual(
                    "<rss xmlns:content=\"http://purl.org/rss/1.0/modules/content/\">",
                    root.DisplayText);
                Assert.IsTrue(root.IsExpanded);
                Assert.AreEqual("<title>Example feed title</title>", root.Children[0].Children[0].DisplayText);
                Assert.AreEqual("</rss>", root.Children[^1].DisplayText);
                var rootText = FindVisualChildren<TextBlock>(window).Single(textBlock => textBlock.Text == root.DisplayText);
                Assert.AreEqual(TextWrapping.Wrap, rootText.TextWrapping);
                xmlTree.UpdateLayout();
                var rootItem = (TreeViewItem?)xmlTree.ItemContainerGenerator.ContainerFromItem(root);
                Assert.IsNotNull(rootItem);
                Assert.IsTrue(rootItem.IsExpanded);
                Assert.AreEqual(2, rootItem.Items.Count);
                var channel = (RawXmlTreeNode)rootItem.Items[0];
                Assert.AreEqual("<channel>", channel.DisplayText);
                var channelItem = (TreeViewItem?)rootItem.ItemContainerGenerator.ContainerFromItem(channel);
                Assert.IsNotNull(channelItem);
                Assert.IsTrue(channelItem.IsExpanded);
                channelItem.UpdateLayout();
                Assert.AreEqual(3, channelItem.Items.Count);
                var title = (RawXmlTreeNode)channelItem.Items[0];
                Assert.AreEqual("<title>Example feed title</title>", title.DisplayText);
                Assert.IsNotNull(channelItem.ItemContainerGenerator.ContainerFromItem(title));
                Assert.AreEqual("</channel>", ((RawXmlTreeNode)channelItem.Items[^1]).DisplayText);
                var item = (RawXmlTreeNode)channelItem.Items[1];
                Assert.AreEqual("<item>", item.DisplayText);
                Assert.AreEqual("</item>", item.Children[^1].DisplayText);
                var encodedContent = item.Children[0];
                Assert.AreEqual("<content:encoded>", encodedContent.DisplayText);
                Assert.IsTrue(encodedContent.IsEncoded);
                Assert.AreEqual("</content:encoded>", encodedContent.Children[^1].DisplayText);
                Assert.AreEqual("<p>", encodedContent.Children[0].DisplayText);
                CollectionAssert.AreEqual(
                    new[] { "Encoded", "<strong>bold</strong>", "<br />", "after", "</p>" },
                    encodedContent.Children[0].Children.Select(node => node.DisplayText).ToArray());
                var content = item.Children[1];
                Assert.AreEqual("<content>", content.DisplayText);
                CollectionAssert.AreEqual(
                    new[] { "<p>heading</p>", "<br/>", "after", "</content>" },
                    content.Children.Select(node => node.DisplayText).ToArray());
                var details = item.Children[2];
                Assert.AreEqual("<details>", details.DisplayText);
                Assert.AreEqual("<summary>Nested label</summary>", details.Children[0].DisplayText);
                Assert.AreEqual("</details>", details.Children[^1].DisplayText);

                var itemContainer = (TreeViewItem?)channelItem.ItemContainerGenerator.ContainerFromItem(item);
                Assert.IsNotNull(itemContainer);
                itemContainer.UpdateLayout();
                var encodedContentContainer = (TreeViewItem?)itemContainer.ItemContainerGenerator.ContainerFromItem(encodedContent);
                Assert.IsNotNull(encodedContentContainer);
                encodedContentContainer.UpdateLayout();
                var encodedContentText = FindVisualChildren<TextBlock>(window)
                    .Single(textBlock => textBlock.Text == encodedContent.DisplayText);
                var successBrush = (SolidColorBrush)System.Windows.Application.Current.Resources["SuccessBrush"];
                var encodedContentBrush = (SolidColorBrush)encodedContentText.Foreground;
                Assert.AreEqual(successBrush.Color, encodedContentBrush.Color);
                var contextMenu = encodedContentText.ContextMenu!;
                Assert.AreEqual(1, contextMenu.Items.Count);
                Assert.AreEqual("Copy", ((MenuItem)contextMenu.Items[0]).Header);
                contextMenu.PlacementTarget = encodedContentText;
                contextMenu.ApplyTemplate();
                var copyNodeMenuItem = (MenuItem)contextMenu.Items[0];
                Assert.IsInstanceOfType(copyNodeMenuItem.DataContext, typeof(RawXmlTreeNode));
                copyNodeMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, copyNodeMenuItem));
                var copiedEncodedContent = Clipboard.GetText();
                var copiedXmlDocument = new System.Xml.XmlDocument();
                copiedXmlDocument.LoadXml(copiedEncodedContent);
                Assert.AreEqual("encoded", copiedXmlDocument.DocumentElement?.LocalName);
                Assert.AreEqual(
                    "http://purl.org/rss/1.0/modules/content/",
                    copiedXmlDocument.DocumentElement?.NamespaceURI);
                StringAssert.Contains(copiedEncodedContent, "<![CDATA[<p>Encoded");
                var encodedParagraphContainer = (TreeViewItem?)encodedContentContainer.ItemContainerGenerator
                    .ContainerFromItem(encodedContent.Children[0]);
                Assert.IsNotNull(encodedParagraphContainer);
                encodedParagraphContainer.UpdateLayout();
                Assert.IsNotNull(encodedParagraphContainer.ItemContainerGenerator
                    .ContainerFromItem(encodedContent.Children[0].Children[1]));
                var detailsContainer = (TreeViewItem?)itemContainer.ItemContainerGenerator.ContainerFromItem(details);
                Assert.IsNotNull(detailsContainer);
                detailsContainer.UpdateLayout();
                Assert.IsNotNull(detailsContainer.ItemContainerGenerator.ContainerFromItem(details.Children[0]));
                var copyXmlButton = (Button)window.FindName("CopyXmlButton")!;
                copyXmlButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, copyXmlButton));
                Assert.AreEqual(rawFeedXml, Clipboard.GetText());
                Assert.AreEqual("XML copied to clipboard.", ((TextBlock)window.FindName("CopyStatusText")!).Text);
            });
        ShowAndCloseModal(
            new CreateFolderWindow(Array.Empty<string>(), Array.Empty<string>()),
            "Close dialog",
            verifyCompactFrame: true,
            inspect: window => Assert.AreEqual(
                "Folder name",
                AutomationProperties.GetName((TextBox)window.FindName("FolderNameBox")!)));
        ShowAndCloseModal(
            new FolderSelectionWindow(Array.Empty<string>(), Array.Empty<string>(), _ => Task.CompletedTask),
            "Close dialog",
            verifyCompactFrame: true,
            inspect: window => Assert.AreEqual(
                "Search folders",
                AutomationProperties.GetName((TextBox)window.FindName("FolderSearchBox")!)));

        ShowAndCloseModal(
            new ProfileFeedEntryWindow(["Technology"]),
            "Close dialog",
            inspect: window =>
            {
                var feedNameBox = (TextBox)window.FindName("FeedNameBox")!;
                var feedUrlBox = (TextBox)window.FindName("FeedUrlBox")!;
                var categoryBox = (ComboBox)window.FindName("CategoryBox")!;
                var addFeedButton = (Button)window.FindName("AddFeedButton")!;
                Assert.AreEqual(string.Empty, categoryBox.Text);
                Assert.IsFalse(addFeedButton.IsEnabled);

                feedNameBox.Text = "Technology news";
                Assert.IsFalse(addFeedButton.IsEnabled);
                feedUrlBox.Text = "https://example.com/feed.xml";
                Assert.IsFalse(addFeedButton.IsEnabled);
                categoryBox.Text = "Technology";
                Assert.IsTrue(addFeedButton.IsEnabled);
                categoryBox.Text = " ";
                Assert.IsFalse(addFeedButton.IsEnabled);
            });

        var catalogManagement = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(new SqliteCatalogStore(":memory:")));
        ShowAndCloseModal(
            new CatalogEntryWindow(catalogManagement, CatalogEntryKind.Feed),
            "Close dialog",
            inspect: window =>
            {
                Assert.AreEqual("Feed name", AutomationProperties.GetName((TextBox)window.FindName("FeedNameBox")!));
                Assert.AreEqual("Feed URL", AutomationProperties.GetName((TextBox)window.FindName("FeedUrlBox")!));
                Assert.AreEqual("Feed category", AutomationProperties.GetName((ComboBox)window.FindName("FeedCategoryBox")!));
            });
        var collectionToRename = new CatalogCollection("collection-pc", "PC Gaming");
        catalogManagement.Collections.Add(collectionToRename);
        catalogManagement.PrepareCollectionEdit(collectionToRename);
        ShowAndCloseModal(
            new CatalogEntryWindow(
                catalogManagement,
                CatalogEntryKind.Collection,
                isEditingCollection: true),
            "Close dialog",
            inspect: window =>
            {
                Assert.AreEqual("Rename collection", ((TextBlock)window.FindName("DialogHeading")!).Text);
                Assert.AreEqual("PC Gaming", ((TextBox)window.FindName("CollectionNameBox")!).Text);
                Assert.AreEqual("Save changes", ((Button)window.FindName("SubmitButton")!).Content);
            });

        var standardPreview = new CatalogFeedPreviewWindow(
            new CatalogFeedListItem("feed", "Example feed", "https://example.com/feed.xml", null, "Comics"),
            _ => Task.FromResult(new CatalogFeedPreview("Example feed", "Comics", null, [])));
        standardPreview.Show();
        standardPreview.UpdateLayout();
        Assert.AreEqual(string.Empty, standardPreview.Title);
        Assert.AreEqual(WindowStyle.None, standardPreview.WindowStyle);
        Assert.AreEqual(Visibility.Visible, ((Button)standardPreview.FindName("ClosePreviewButton")).Visibility);
        Assert.AreEqual(Visibility.Collapsed, ((Button)standardPreview.FindName("CheckFeedButton")).Visibility);
        ((Button)standardPreview.FindName("ClosePreviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsFalse(standardPreview.IsVisible);

        ShowAndCloseModal(
            new ProfileChooserWindow(new ProfileChooserViewModel(null!)),
            "Close profile chooser",
            verifyCompactFrame: true,
            expectedTitle: "RSS Reader",
            inspect: window =>
            {
                Assert.AreEqual("Profile name", AutomationProperties.GetName((TextBox)window.FindName("CreateProfileNameBox")!));
                Assert.AreEqual("Create profile password", AutomationProperties.GetName((PasswordBox)window.FindName("CreatePasswordBox")!));
                Assert.AreEqual("Recovery email", AutomationProperties.GetName((TextBox)window.FindName("RecoveryEmailBox")!));
                Assert.AreEqual("Profile password", AutomationProperties.GetName((PasswordBox)window.FindName("UnlockPasswordBox")!));
            });
        ShowAndCloseModal(new SplashWindow(), "Close startup window", expectedTitle: "RSS Reader");

        void ScheduleMessageAction(string automationName)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var dialog = System.Windows.Application.Current.Windows.OfType<MessageDialogWindow>().Single();
                Assert.AreEqual(string.Empty, dialog.Title);
                Assert.AreEqual(WindowStyle.None, dialog.WindowStyle);
                ((Button)dialog.FindName(automationName)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
        }

        ScheduleMessageAction("AcceptButton");
        Assert.AreEqual(
            MessageBoxResult.OK,
            MessageDialogWindow.Show(null, "Notice", MessageBoxButton.OK, MessageBoxImage.Information));
        ScheduleMessageAction("AcceptButton");
        Assert.AreEqual(
            MessageBoxResult.Yes,
            MessageDialogWindow.Show(null, "Continue?", MessageBoxButton.YesNo, MessageBoxImage.Warning));
        ScheduleMessageAction("NoButton");
        Assert.AreEqual(
            MessageBoxResult.No,
            MessageDialogWindow.Show(null, "Continue?", MessageBoxButton.YesNo, MessageBoxImage.Warning));
        ScheduleMessageAction("CloseButton");
        Assert.AreEqual(
            MessageBoxResult.No,
            MessageDialogWindow.Show(null, "Continue?", MessageBoxButton.YesNo, MessageBoxImage.Warning));
        return styleFailures;
    }

    [TestMethod]
    public void SelectingAFolderOrFeedScrollsTheArticleListToTheTop()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            RssReader.App.MainWindow? window = null;
            try
            {
                var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                viewModel.IsListView = true;
                var folderLink = viewModel.FeedLinks.Single(link => link.Route == "folder:Gaming");
                viewModel.NavigateCommand.Execute(folderLink);

                void AddTallArticleList()
                {
                    for (var index = 0; index < 120; index++)
                    {
                        viewModel.VisibleArticles.Add(new ArticleRowViewModel(
                            $"Article {index}",
                            "Test source",
                            DateTimeOffset.Now.AddMinutes(-index),
                            folderLink.Label,
                            [],
                            "Article summary"));
                    }
                }

                void ProcessPendingScrollRequests()
                {
                    var frame = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(
                        DispatcherPriority.ContextIdle,
                        new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                }

                window = CreateTestMainWindow(viewModel);
                window.Show();
                window.UpdateLayout();
                var articleRowsList = FindArticleListElement<ListBox>(window, "ArticleRowsList");
                var scrollViewer = FindVisualChildren<ScrollViewer>(articleRowsList).First();

                AddTallArticleList();
                window.UpdateLayout();
                scrollViewer.ScrollToVerticalOffset(scrollViewer.ScrollableHeight);
                window.UpdateLayout();
                Assert.IsTrue(scrollViewer.VerticalOffset > 0);

                var folderScrollRequest = viewModel.ArticleListScrollToTopRequest;
                viewModel.NavigateCommand.Execute(folderLink);
                ProcessPendingScrollRequests();
                window.UpdateLayout();
                Assert.AreEqual(folderScrollRequest + 1, viewModel.ArticleListScrollToTopRequest);
                Assert.AreEqual(0, scrollViewer.VerticalOffset, 0.5);

                var feedLink = new SidebarLink(
                    "feed:test",
                    "Test feed",
                    "\uE774",
                    indentLevel: 2,
                    parentFolder: folderLink);
                viewModel.FeedLinks.Add(feedLink);
                viewModel.NavigateCommand.Execute(feedLink);
                AddTallArticleList();
                window.UpdateLayout();
                scrollViewer.ScrollToVerticalOffset(scrollViewer.ScrollableHeight);
                window.UpdateLayout();
                Assert.IsTrue(scrollViewer.VerticalOffset > 0);

                var feedScrollRequest = viewModel.ArticleListScrollToTopRequest;
                viewModel.NavigateCommand.Execute(feedLink);
                ProcessPendingScrollRequests();
                window.UpdateLayout();
                Assert.AreEqual(feedScrollRequest + 1, viewModel.ArticleListScrollToTopRequest);
                Assert.AreEqual(0, scrollViewer.VerticalOffset, 0.5);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (window?.IsVisible == true)
                {
                    window.Close();
                }
            }
        });

        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void FollowedCatalogFeedsUseLightGreenCardBackground()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            Window? window = null;
            try
            {
                var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                var followedFeed = new CatalogFeedListItem(
                    "feed-followed",
                    "Followed feed",
                    "https://example.com/followed.xml",
                    null,
                    "Comics")
                {
                    IsSubscribed = true
                };
                var availableFeed = new CatalogFeedListItem(
                    "feed-available",
                    "Available feed",
                    "https://example.com/available.xml",
                    null,
                    "Comics");
                viewModel.CatalogFeeds.Add(followedFeed);
                viewModel.CatalogFeeds.Add(availableFeed);

                window = CreateTestMainWindow(viewModel);
                window.Show();
                window.UpdateLayout();
                viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));
                window.UpdateLayout();

                var catalogBrowser = (UserControl)window.FindName("CatalogBrowserView")!;
                var catalogResults = (ListBox)catalogBrowser.FindName("CatalogFeedResultsList");
                catalogResults.UpdateLayout();

                var followedContainer = catalogResults.ItemContainerGenerator.ContainerFromItem(followedFeed);
                var availableContainer = catalogResults.ItemContainerGenerator.ContainerFromItem(availableFeed);
                Assert.IsNotNull(followedContainer);
                Assert.IsNotNull(availableContainer);
                var followedCard = FindVisualChildren<Border>(followedContainer!)
                    .Single(border => border.ContextMenu is not null);
                var availableCard = FindVisualChildren<Border>(availableContainer!)
                    .Single(border => border.ContextMenu is not null);

                Assert.AreEqual(
                    ((SolidColorBrush)window.FindResource("SuccessWashBrush")).Color,
                    ((SolidColorBrush)followedCard.Background).Color);
                Assert.AreEqual(
                    ((SolidColorBrush)window.FindResource("SidebarBrush")).Color,
                    ((SolidColorBrush)availableCard.Background).Color);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (window?.IsVisible == true)
                {
                    window.Close();
                }
            }
        });

        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void MainWindowCanBeConstructedForARegularProfile()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            try
            {
                var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                var accessibilityFeed = new CatalogFeedListItem(
                    "feed-accessibility",
                    "Example comic feed",
                    "https://example.com/comics.xml",
                    "A sample comic feed",
                    "Comics",
                    "category-comics",
                    "https://example.com/comics");
                var feedWithoutDescription = new CatalogFeedListItem(
                    "feed-no-description",
                    "Example feed without description",
                    "https://example.com/short.xml",
                    null,
                    "Comics",
                    "category-comics");
                viewModel.CatalogFeeds.Add(accessibilityFeed);
                viewModel.CatalogFeeds.Add(feedWithoutDescription);
                var window = CreateTestMainWindow(viewModel);
                var preferencesRequested = false;
                var logoutRequested = false;
                window.PreferencesRequested += () => preferencesRequested = true;
                window.LogoutRequested += () => logoutRequested = true;
                window.Show();
                window.UpdateLayout();
                var folderDeleteButtons = FindVisualChildren<Button>(window)
                    .Where(button => AutomationProperties.GetName(button) == "Delete folder")
                    .ToArray();
                var visibleFolderDeleteButtons = folderDeleteButtons
                    .Where(button => button.Visibility == Visibility.Visible)
                    .ToArray();
                Assert.AreEqual(
                    folderDeleteButtons.Count(button => button.DataContext is SidebarLink { IsFolder: true }),
                    visibleFolderDeleteButtons.Length);
                Assert.IsTrue(visibleFolderDeleteButtons.Length >= viewModel.FeedLinks.Count(link => link.IsFolder));
                Assert.IsTrue(visibleFolderDeleteButtons.All(button => button.Opacity == 0));
                var articleCardsList = FindArticleListElement<ListBox>(window, "ArticleCardsList");
                var folderArticleCardsList = FindArticleListElement<ListBox>(window, "ArticleFolderCardsList");
                viewModel.IsCardsView = true;
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, folderArticleCardsList.Visibility);
                Assert.AreEqual(Visibility.Collapsed, articleCardsList.Visibility);
                viewModel.IsSortByDate = true;
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, articleCardsList.Visibility);
                Assert.AreEqual(Visibility.Collapsed, folderArticleCardsList.Visibility);
                for (var articleIndex = 0; articleIndex < 500; articleIndex++)
                {
                    viewModel.VisibleArticles.Add(new ArticleRowViewModel(
                        $"Virtualized article {articleIndex}",
                        "Test source",
                        DateTimeOffset.Now.AddMinutes(-articleIndex),
                        "Inbox",
                        [],
                        "Article summary"));
                }
                window.UpdateLayout();
                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(articleCardsList));
                Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(articleCardsList));
                Assert.AreEqual(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(articleCardsList));
                var firstCard = (FrameworkElement?)articleCardsList.ItemContainerGenerator.ContainerFromIndex(0);
                var secondCard = (FrameworkElement?)articleCardsList.ItemContainerGenerator.ContainerFromIndex(1);
                Assert.IsNotNull(firstCard);
                Assert.IsNotNull(secondCard);
                var firstCardPosition = firstCard.TransformToAncestor(articleCardsList).Transform(new Point(0, 0));
                var secondCardPosition = secondCard.TransformToAncestor(articleCardsList).Transform(new Point(0, 0));
                Assert.AreEqual(firstCardPosition.Y, secondCardPosition.Y, 1);
                Assert.IsTrue(secondCardPosition.X > firstCardPosition.X);
                var realizedArticleCount = Enumerable.Range(0, articleCardsList.Items.Count)
                    .Count(index => articleCardsList.ItemContainerGenerator.ContainerFromIndex(index) is not null);
                Assert.IsTrue(realizedArticleCount < articleCardsList.Items.Count);
                viewModel.IsSortByFolder = true;
                viewModel.IsCardsView = false;
                window.UpdateLayout();
                var previewWindow = new RssReader.App.CatalogFeedPreviewWindow(
                    new CatalogFeedListItem("preview", "Preview title", "https://example.com/feed.xml", null, "Comics"),
                    _ => Task.FromResult(new CatalogFeedPreview("Preview title", "Comics", null, [])));
                previewWindow.UpdateLayout();
                Assert.AreEqual(WindowStyle.None, previewWindow.WindowStyle);
                Assert.AreEqual("Preview title", ((TextBlock)previewWindow.FindName("FeedNameText")).Text);
                var previewErrorText = (TextBlock)previewWindow.FindName("ErrorMessageText");
                Assert.IsNotNull(previewErrorText.ContextMenu);
                Assert.AreEqual("Copy", ((MenuItem)previewErrorText.ContextMenu.Items[0]).Header);
                var closePreviewButton = (Button)previewWindow.FindName("ClosePreviewButton");
                var followPreviewDeleteButton = (Button)previewWindow.FindName("DeleteCatalogFeedButton");
                Assert.IsTrue(closePreviewButton.IsCancel);
                Assert.AreEqual(Visibility.Collapsed, followPreviewDeleteButton.Visibility);
                Assert.AreEqual(0, Grid.GetRow(closePreviewButton));
                Assert.AreEqual(
                    HorizontalAlignment.Right,
                    ((StackPanel)VisualTreeHelper.GetParent(closePreviewButton)).HorizontalAlignment);
                Assert.AreEqual("Close feed preview", AutomationProperties.GetName(closePreviewButton));
                Assert.AreEqual("\uE711", closePreviewButton.Content);
                previewWindow.Close();
                var managedPreviewDeleteRequested = false;
                var managedPreviewWindow = new RssReader.App.CatalogFeedPreviewWindow(
                    new CatalogFeedListItem("managed-preview", "Managed preview", "https://example.com/managed.xml", null, "Comics"),
                    _ => Task.FromResult(new CatalogFeedPreview("Managed preview", "Comics", null, [])),
                    () =>
                    {
                        managedPreviewDeleteRequested = true;
                        return Task.CompletedTask;
                    },
                    checkFeed: () => Task.CompletedTask)
                {
                    Owner = window
                };
                managedPreviewWindow.Show();
                managedPreviewWindow.UpdateLayout();
                var managedPreviewDeleteButton = (Button)managedPreviewWindow.FindName("DeleteCatalogFeedButton");
                var managedPreviewCloseButton = (Button)managedPreviewWindow.FindName("ClosePreviewButton");
                var managedPreviewCheckFeedButton = (Button)managedPreviewWindow.FindName("CheckFeedButton");
                Assert.AreEqual(Visibility.Visible, managedPreviewDeleteButton.Visibility);
                Assert.AreEqual(Visibility.Visible, managedPreviewCheckFeedButton.Visibility);
                Assert.AreEqual(Visibility.Visible, managedPreviewCloseButton.Visibility);
                var previewActions = (StackPanel)VisualTreeHelper.GetParent(managedPreviewDeleteButton);
                Assert.AreSame(managedPreviewDeleteButton, previewActions.Children[0]);
                Assert.AreSame(managedPreviewCheckFeedButton, previewActions.Children[1]);
                Assert.AreSame(managedPreviewCloseButton, previewActions.Children[2]);
                managedPreviewDeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, managedPreviewDeleteButton));
                Assert.IsTrue(managedPreviewDeleteRequested);
                Assert.IsFalse(managedPreviewWindow.IsVisible);
                var managedPreviewCheckRequested = false;
                var managedCheckPreviewWindow = new RssReader.App.CatalogFeedPreviewWindow(
                    new CatalogFeedListItem("managed-check-preview", "Managed check preview", "https://example.com/check.xml", null, "Comics"),
                    _ => Task.FromResult(new CatalogFeedPreview("Managed check preview", "Comics", null, [])),
                    checkFeed: () =>
                    {
                        managedPreviewCheckRequested = true;
                        return Task.CompletedTask;
                    })
                {
                    Owner = window
                };
                managedCheckPreviewWindow.Show();
                managedCheckPreviewWindow.UpdateLayout();
                var checkFeedButton = (Button)managedCheckPreviewWindow.FindName("CheckFeedButton");
                var managedCheckCloseButton = (Button)managedCheckPreviewWindow.FindName("ClosePreviewButton");
                Assert.AreEqual(Visibility.Visible, checkFeedButton.Visibility);
                Assert.AreEqual(Visibility.Visible, managedCheckCloseButton.Visibility);
                Assert.AreEqual("Check Feed", checkFeedButton.Content);
                Assert.AreEqual("Check feed", AutomationProperties.GetName(checkFeedButton));
                Assert.AreEqual("#FF218739", checkFeedButton.Foreground.ToString());
                checkFeedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, checkFeedButton));
                Assert.IsTrue(managedPreviewCheckRequested);
                Assert.IsFalse(managedCheckPreviewWindow.IsVisible);
                var catalogAdminView = (UserControl)window.FindName("CatalogAdminView")!;
                var catalogManagementList = (ListBox)catalogAdminView.FindName("CatalogManagementFeedList");
                Assert.AreEqual("Catalog feed management results", AutomationProperties.GetName(catalogManagementList));
                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(catalogManagementList));
                Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(catalogManagementList));
                var catalogActionsButton = (Button)catalogAdminView.FindName("CatalogActionsButton");
                Assert.AreEqual("Catalog actions", AutomationProperties.GetName(catalogActionsButton));
                CollectionAssert.AreEqual(
                    new[] { "Import OPML...", "Load starter pack" },
                    catalogActionsButton.ContextMenu!.Items.OfType<MenuItem>().Select(item => item.Header.ToString()).ToArray());
                catalogActionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, catalogActionsButton));
                Assert.IsTrue(catalogActionsButton.ContextMenu.IsOpen);
                catalogActionsButton.ContextMenu.IsOpen = false;
                Assert.AreEqual(2, ((TabControl)catalogAdminView.FindName("CatalogManagementTabs")).Items.Count);
                Assert.AreEqual(Visibility.Collapsed, ((ProgressBar)catalogAdminView.FindName("CatalogManagementLoadProgressBar")).Visibility);
                Assert.IsNull(catalogAdminView.FindName("CatalogFeedPreviewPanel"));
                viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));
                window.UpdateLayout();
                var catalogBrowser = (UserControl)window.FindName("CatalogBrowserView")!;
                var catalogSearchBox = (TextBox)catalogBrowser.FindName("CatalogFeedSearchBox");
                catalogSearchBox.Text = "comic";
                Assert.AreEqual("comic", viewModel.CatalogSearchQuery);
                var categoryFilter = (ComboBox)catalogBrowser.FindName("CatalogCategoryFilter");
                Assert.AreEqual("All categories (0)", ((CatalogCategoryOption)categoryFilter.SelectedItem).Name);
                var collectionFilter = (ComboBox)catalogBrowser.FindName("CatalogCollectionFilter");
                Assert.AreEqual("All collections (0)", ((CatalogCollectionOption)collectionFilter.SelectedItem).Name);
                var hideFollowedCheckBox = (CheckBox)catalogBrowser.FindName("HideFollowedCatalogCheckBox");
                hideFollowedCheckBox.IsChecked = true;
                Assert.IsTrue(viewModel.HideFollowedCatalogFeeds);
                var activeCatalogFilters = (ItemsControl)catalogBrowser.FindName("CatalogActiveFilterChips");
                Assert.AreEqual(2, activeCatalogFilters.Items.Count);
                Assert.AreEqual(Visibility.Visible, activeCatalogFilters.Visibility);
                Assert.AreEqual("Active feed filters", AutomationProperties.GetName(activeCatalogFilters));
                Assert.IsNotNull(viewModel.SelectedCatalogFeed);
                var catalogResults = (ListBox)catalogBrowser.FindName("CatalogFeedResultsList");
                Assert.AreEqual("Catalog feed results", AutomationProperties.GetName(catalogResults));
                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(catalogResults));
                Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(catalogResults));
                catalogResults.UpdateLayout();
                var catalogResultsContainer = (Grid)catalogBrowser.FindName("CatalogFeedResultsContainer");
                Assert.IsTrue(double.IsPositiveInfinity(catalogResultsContainer.MaxWidth));
                Assert.AreEqual(HorizontalAlignment.Stretch, catalogResultsContainer.HorizontalAlignment);
                Assert.AreEqual(
                    ((FrameworkElement)catalogBrowser.FindName("CatalogBrowserLayout")!).ActualWidth,
                    catalogResultsContainer.ActualWidth,
                    1);
                var selectedFeedPreviewPane = (Border)catalogBrowser.FindName("SelectedCatalogFeedPreviewPane");
                Assert.AreEqual(2, Grid.GetColumn(selectedFeedPreviewPane));
                Assert.AreEqual(
                    "Selected feed details and recent articles",
                    AutomationProperties.GetName(selectedFeedPreviewPane));
                var feedContainer = catalogResults.ItemContainerGenerator.ContainerFromItem(accessibilityFeed);
                Assert.IsNotNull(feedContainer);
                var feedRowItem = (ListBoxItem)feedContainer!;
                feedRowItem.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent
                });
                window.UpdateLayout();
                Assert.IsTrue(feedRowItem.Focusable);
                Assert.IsTrue(feedRowItem.IsSelected);
                Assert.AreSame(accessibilityFeed, viewModel.SelectedCatalogFeed);
                Keyboard.ClearFocus();
                window.UpdateLayout();
                var feedWithoutDescriptionContainer = catalogResults.ItemContainerGenerator
                    .ContainerFromItem(feedWithoutDescription);
                Assert.IsNotNull(feedWithoutDescriptionContainer);
                var feedWithoutDescriptionRowItem = (ListBoxItem)feedWithoutDescriptionContainer!;
                feedWithoutDescriptionRowItem.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent
                });
                window.UpdateLayout();
                Assert.IsTrue(feedWithoutDescriptionRowItem.IsSelected);
                Assert.AreSame(feedWithoutDescription, viewModel.SelectedCatalogFeed);
                Assert.IsTrue(FindVisualChildren<TextBlock>(selectedFeedPreviewPane)
                    .Any(text => text.Text == feedWithoutDescription.Name));
                Keyboard.ClearFocus();
                window.UpdateLayout();
                Assert.AreEqual(
                    Colors.Transparent,
                    ((SolidColorBrush)((Border)feedRowItem.Template.FindName("ItemSurface", feedRowItem)!).Background).Color);
                feedRowItem.IsSelected = false;
                var feedRowContainer = (FrameworkElement)feedContainer!;
                var descriptionText = FindVisualChildren<TextBlock>(feedContainer!)
                    .Single(text => AutomationProperties.GetName(text) == "Catalog feed description");
                Assert.AreEqual(Visibility.Visible, descriptionText.Visibility);
                Assert.IsNull(descriptionText.ToolTip);
                var categoryLabel = FindVisualChildren<TextBlock>(feedContainer!)
                    .Single(text => text.Text == "Comics");
                Assert.AreEqual(12, categoryLabel.FontSize);
                Assert.AreEqual(VerticalAlignment.Center, categoryLabel.VerticalAlignment);
                var catalogSourceLinks = FindVisualChildren<TextBlock>(feedContainer!)
                    .SelectMany(text => text.Inlines.OfType<Hyperlink>())
                    .ToArray();
                CollectionAssert.AreEquivalent(
                    new[] { "https://example.com/comics.xml", "https://example.com/comics" },
                    catalogSourceLinks.Select(link => link.NavigateUri.AbsoluteUri).ToArray());
                var feedUrlText = FindVisualChildren<TextBlock>(feedContainer!)
                    .Single(text => text.Inlines.OfType<Hyperlink>().Any(link =>
                        link.NavigateUri.AbsoluteUri == "https://example.com/comics.xml"));
                Assert.AreEqual("Feed: ", feedUrlText.Inlines.OfType<Run>().First().Text);
                var websiteUrlText = FindVisualChildren<TextBlock>(feedContainer!)
                    .Single(text => text.Inlines.OfType<Hyperlink>().Any(link =>
                        link.NavigateUri.AbsoluteUri == "https://example.com/comics"));
                Assert.AreEqual("Website: ", websiteUrlText.Inlines.OfType<Run>().First().Text);
                var feedLink = feedUrlText.Inlines.OfType<Hyperlink>().Single();
                var publisherWebsiteSourceLink = websiteUrlText.Inlines.OfType<Hyperlink>().Single();
                Assert.AreEqual(feedLink.Foreground, publisherWebsiteSourceLink.Foreground);
                Assert.AreEqual("RSS feed", feedLink.Inlines.OfType<Run>().Single().Text);
                Assert.AreEqual("example.com", publisherWebsiteSourceLink.Inlines.OfType<Run>().Single().Text);
                Assert.IsNotNull(feedLink.Style);
                var hoverUnderlineTrigger = feedLink.Style.Triggers.OfType<Trigger>().Single();
                Assert.AreEqual("TextDecorations", hoverUnderlineTrigger.Setters.OfType<Setter>().Single().Property.Name);
                var hiddenDescriptionText = FindVisualChildren<TextBlock>(feedWithoutDescriptionContainer!)
                    .Single(text => AutomationProperties.GetName(text) == "Catalog feed description");
                Assert.AreEqual(Visibility.Collapsed, hiddenDescriptionText.Visibility);
                var feedCheckBox = FindVisualChild<CheckBox>(feedContainer!)
                    ?? throw new AssertFailedException("The catalog feed selection control was not created.");
                var selectableFeedCheckBox = FindVisualChild<CheckBox>(feedWithoutDescriptionContainer!)
                    ?? throw new AssertFailedException("The unfollowed feed selection control was not created.");
                var feedButtons = FindVisualChildren<Button>(feedContainer!).ToArray();
                var previewButton = feedButtons.Single(button =>
                    AutomationProperties.GetName(button) == "Preview Example comic feed");
                var subscriptionButton = feedButtons.Single(button =>
                    AutomationProperties.GetName(button) == "Follow Example comic feed");
                Assert.AreEqual(new FontFamily("Segoe UI"), previewButton.FontFamily);
                Assert.AreEqual(new FontFamily("Segoe UI"), subscriptionButton.FontFamily);
                Assert.AreEqual(FontWeights.Bold, previewButton.FontWeight);
                Assert.AreEqual(FontWeights.Bold, subscriptionButton.FontWeight);
                var previewContent = (StackPanel)previewButton.Content;
                var subscriptionContent = (StackPanel)subscriptionButton.Content;
                var previewIcon = (TextBlock)previewContent.Children[0];
                var subscriptionIcon = (TextBlock)subscriptionContent.Children[0];
                Assert.AreEqual(FontWeights.Bold, previewIcon.FontWeight);
                Assert.AreEqual(FontWeights.Bold, subscriptionIcon.FontWeight);
                Assert.AreEqual(window.FindResource("NeutralActionBrush"), previewIcon.Foreground);
                Assert.AreEqual(window.FindResource("SuccessBrush"), subscriptionIcon.Foreground);
                var rowCommandPanel = FindVisualChildren<StackPanel>(feedContainer!)
                    .Single(panel => Grid.GetColumn(panel) == 2);
                var feedRowGrid = (Grid)VisualTreeHelper.GetParent(rowCommandPanel)!;
                Assert.AreEqual("Select Example comic feed for follow", AutomationProperties.GetName(feedCheckBox));
                Assert.IsTrue(feedCheckBox.IsTabStop);
                Assert.AreEqual(Visibility.Visible, feedCheckBox.Visibility);
                Assert.AreEqual(Visibility.Visible, selectableFeedCheckBox.Visibility);
                Assert.AreEqual(Visibility.Collapsed, rowCommandPanel.Visibility);
                Assert.AreEqual(0, rowCommandPanel.Opacity);
                Assert.IsFalse(rowCommandPanel.IsHitTestVisible);
                Assert.AreEqual(0, feedRowGrid.ColumnDefinitions[2].ActualWidth, 0.1);
                var categoryLabelRight = categoryLabel.TranslatePoint(new Point(categoryLabel.ActualWidth, 0), feedRowContainer).X;
                Assert.AreEqual(feedRowContainer.ActualWidth - 15, categoryLabelRight, 2);
                Assert.AreEqual("Preview Example comic feed", AutomationProperties.GetName(previewButton));
                Assert.AreEqual("Preview", ((TextBlock)previewContent.Children[1]).Text);
                Assert.IsTrue(previewButton.IsTabStop);
                Assert.AreEqual("Follow Example comic feed", AutomationProperties.GetName(subscriptionButton));
                Assert.AreEqual("Follow", ((TextBlock)subscriptionContent.Children[1]).Text);
                accessibilityFeed.IsSubscribed = true;
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, feedCheckBox.Visibility);
                Assert.AreEqual(Visibility.Visible, FindVisualChildren<TextBlock>(feedContainer!)
                    .Single(text => text.Text == "Following").Visibility);
                var followedFeedText = FindVisualChildren<StackPanel>(feedContainer!)
                    .Single(panel => Grid.GetColumn(panel) == 1 &&
                        panel.Children.OfType<TextBlock>()
                            .Any(text => AutomationProperties.GetName(text) == "Catalog feed description"));
                var unfollowedFeedText = FindVisualChildren<StackPanel>(feedWithoutDescriptionContainer!)
                    .Single(panel => Grid.GetColumn(panel) == 1 &&
                        panel.Children.OfType<TextBlock>()
                            .Any(text => AutomationProperties.GetName(text) == "Catalog feed description"));
                var followedFeedTextLeft = followedFeedText.TranslatePoint(new Point(0, 0), catalogResults).X;
                var unfollowedFeedTextLeft = unfollowedFeedText.TranslatePoint(new Point(0, 0), catalogResults).X;
                Assert.AreEqual(followedFeedTextLeft, unfollowedFeedTextLeft, 0.1);
                Assert.AreEqual("Unfollow Example comic feed", AutomationProperties.GetName(subscriptionButton));
                Assert.AreEqual("Unfollow", ((TextBlock)subscriptionContent.Children[1]).Text);
                Assert.AreEqual(window.FindResource("DestructiveBrush"), subscriptionIcon.Foreground);
                var selectAllCatalogFeedsCheckBox = (CheckBox)catalogBrowser.FindName("SelectAllCatalogFeedsCheckBox");
                Assert.IsTrue(selectAllCatalogFeedsCheckBox.IsThreeState);
                Assert.AreEqual(0, Grid.GetColumn(selectAllCatalogFeedsCheckBox));
                Assert.AreEqual(18, selectAllCatalogFeedsCheckBox.Width);
                Assert.IsTrue(double.IsNaN(selectAllCatalogFeedsCheckBox.Height));
                Assert.AreEqual(18, selectAllCatalogFeedsCheckBox.ActualWidth);
                Assert.AreEqual(24, selectAllCatalogFeedsCheckBox.ActualHeight);
                var selectionToolbar = (Grid)VisualTreeHelper.GetParent(selectAllCatalogFeedsCheckBox)!;
                Assert.AreEqual(32, selectionToolbar.ColumnDefinitions[0].ActualWidth);
                var batchFollowButton = (Button)catalogBrowser.FindName("BatchFollowSelectedButton");
                Assert.AreEqual(new FontFamily("Segoe UI"), batchFollowButton.FontFamily);
                Assert.IsTrue(double.IsNaN(batchFollowButton.Width));
                Assert.IsTrue(batchFollowButton.ActualWidth > 80);
                Assert.AreEqual("Follow selected feeds", AutomationProperties.GetName(batchFollowButton));
                Assert.AreEqual("Follow selected (0)", AutomationProperties.GetHelpText(batchFollowButton));
                Assert.AreEqual("Follow selected (0)", FindVisualChildren<TextBlock>(batchFollowButton)
                    .Single(text => text.Text == "Follow selected (0)").Text);
                Assert.AreEqual(FontWeights.Bold, batchFollowButton.FontWeight);
                Assert.AreEqual("#FF218739", batchFollowButton.Foreground.ToString());
                batchFollowButton.ApplyTemplate();
                Assert.AreEqual(
                    "#FFE8F4EA",
                    ((Border)batchFollowButton.Template.FindName("ActionBorder", batchFollowButton)!).Background.ToString());
                Assert.IsFalse(batchFollowButton.IsEnabled);
                var unfollowAllButton = (Button)catalogBrowser.FindName("UnfollowAllCatalogFeedsButton");
                Assert.AreEqual(new FontFamily("Segoe UI"), unfollowAllButton.FontFamily);
                Assert.IsTrue(double.IsNaN(unfollowAllButton.Width));
                Assert.IsTrue(unfollowAllButton.ActualWidth > 80);
                Assert.AreEqual("Unfollow visible feeds", AutomationProperties.GetName(unfollowAllButton));
                var unfollowContent = (StackPanel)unfollowAllButton.Content;
                Assert.AreEqual("\uE738", ((TextBlock)unfollowContent.Children[0]).Text);
                var unfollowLabel = ((TextBlock)unfollowContent.Children[1]).Text;
                Assert.AreEqual(unfollowLabel, AutomationProperties.GetHelpText(unfollowAllButton));
                StringAssert.StartsWith(unfollowLabel, "Unfollow visible (");
                Assert.AreEqual(FontWeights.Bold, unfollowAllButton.FontWeight);
                Assert.AreEqual("#FFA33C35", unfollowAllButton.Foreground.ToString());
                unfollowAllButton.ApplyTemplate();
                Assert.AreEqual(
                    "#FFFCE8E6",
                    ((Border)unfollowAllButton.Template.FindName("ActionBorder", unfollowAllButton)!).Background.ToString());
                Assert.IsNull(window.FindName("ClearSelectedCatalogFeedsButton"));
                viewModel.ClearCatalogFiltersCommand.Execute(null);
                window.UpdateLayout();

                var preferences = new ProfilePreferences(
                    ProfileStartPage.FirstFolder,
                    ProfileArticlePresentation.Magazine,
                    ProfileArticleSort.Newest,
                    true,
                    25,
                    false,
                    60,
                    true);
                var preferencesWindow = new RssReader.App.PreferencesWindow(preferences) { Owner = window };
                preferencesWindow.Show();
                preferencesWindow.UpdateLayout();
                Assert.IsTrue(((RadioButton)preferencesWindow.FindName("StartFirstFolderOption")).IsChecked);
                Assert.IsTrue(((RadioButton)preferencesWindow.FindName("PresentationMagazineOption")).IsChecked);
                Assert.AreEqual("25", ((TextBox)preferencesWindow.FindName("FolderArticleLimitBox")).Text);
                Assert.IsFalse(((CheckBox)preferencesWindow.FindName("RefreshFeedsWhenOpenedCheckBox")).IsChecked);
                Assert.AreEqual("Every hour", ((ComboBoxItem)((ComboBox)preferencesWindow.FindName("AutoRefreshIntervalComboBox")).SelectedItem).Content);
                Assert.IsTrue(((CheckBox)preferencesWindow.FindName("LimitArticleWidthCheckBox")).IsChecked);
                var showXmlCheckBox = (CheckBox)preferencesWindow.FindName("ShowRawFeedButtonCheckBox");
                Assert.IsTrue(showXmlCheckBox.IsChecked);
                Assert.AreEqual("Show XML in the article viewer", showXmlCheckBox.Content);
                preferencesWindow.Close();
                var rawWindow = new RssReader.App.RawFeedWindow("Test feed", "<?xml version=\"1.0\"?><rss />") { Owner = window };
                rawWindow.Show();
                rawWindow.UpdateLayout();
                var rawContentTree = (TreeView)rawWindow.FindName("RawContentTreeView");
                Assert.AreEqual("<rss />", ((RssReader.App.RawXmlTreeNode)rawContentTree.Items[0]).DisplayText);
                var closeRawFeedButton = FindVisualChildren<Button>(rawWindow)
                    .Single(button => Equals(button.Content, "Close"));
                closeRawFeedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, closeRawFeedButton));
                Assert.IsFalse(rawWindow.IsVisible);

                var sidebarControl = (UserControl)window.FindName("SidebarView")!;
                var profileButton = (Button)sidebarControl.FindName("ProfileMenuButton");
                profileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, profileButton));
                Assert.IsTrue(profileButton.ContextMenu?.IsOpen);
                Assert.IsFalse(logoutRequested);
                var menuItems = profileButton.ContextMenu!.Items.OfType<MenuItem>().ToArray();
                menuItems.Single(item => Equals(item.Header, "Preferences"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.IsTrue(preferencesRequested);
                Assert.IsFalse(logoutRequested);

                profileButton.ContextMenu.IsOpen = false;
                profileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, profileButton));
                menuItems.Single(item => Equals(item.Header, "Log Out"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.IsTrue(logoutRequested);
                profileButton.ContextMenu.IsOpen = false;

                var sidebarPanel = (Border)sidebarControl.FindName("SidebarPanel");
                var sidebarPeekButton = (Button)sidebarControl.FindName("SidebarPeekButton");
                var shellGrid = (Grid)window.FindName("ShellGrid");
                viewModel.ToggleSidebarCommand.Execute(null);
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, sidebarPeekButton.Visibility);
                Assert.AreEqual(Visibility.Collapsed, sidebarPanel.Visibility);
                Assert.AreEqual(0, shellGrid.ColumnDefinitions[0].ActualWidth);

                sidebarPeekButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                {
                    RoutedEvent = UIElement.MouseEnterEvent
                });
                Assert.AreEqual(Visibility.Visible, sidebarPanel.Visibility);
                Assert.AreEqual(0, shellGrid.ColumnDefinitions[0].ActualWidth);

                viewModel.ToggleSidebarCommand.Execute(null);
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, sidebarPeekButton.Visibility);
                Assert.AreEqual(Visibility.Visible, sidebarPanel.Visibility);
                Assert.AreEqual(286, shellGrid.ColumnDefinitions[0].ActualWidth);

                var comicArticle = new ArticleRowViewModel(
                    "9 Chickweed Lane",
                    "Arcamax",
                    DateTimeOffset.UtcNow,
                    "Comics",
                    [],
                    "Source",
                    articleId: "comic-story",
                    feedId: "comic-feed",
                    link: "https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031",
                    content: "<p>Comic panel <img src=\"https://example.com/inline-panel.png\" alt=\"Inline panel\" /> follows.</p>" +
                        string.Concat(Enumerable.Repeat("<p>Long article text for scroll restoration.</p>", 80)),
                    imageUrl: "https://resources.arcamax.com/newspics/396/39604/3960483.gif",
                    websiteUrl: "https://www.arcamax.com/thefunnies/ninechickweedlane",
                    author: "Jayme Lozano Carver, The Texas Tribune");
                viewModel.SelectArticleCommand.Execute(comicArticle);
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, ((DockPanel)window.FindName("WorkspaceHeader")).Visibility);
                var articleViewer = FindReaderElement<RssReader.App.ArticleHtmlViewer>(window, "SelectedArticleHtmlViewer");
                Assert.AreEqual(Visibility.Visible, articleViewer.Visibility);
                var initialViewerWidth = articleViewer.ActualWidth;
                var initialViewerHeight = articleViewer.ActualHeight;
                StringAssert.Contains(articleViewer.CurrentDocument, "max-width: 900px");
                StringAssert.Contains(articleViewer.CurrentDocument, "https://example.com/inline-panel.png");
                Assert.IsFalse(articleViewer.CurrentDocument.Contains(
                    "https://resources.arcamax.com/newspics/396/39604/3960483.gif",
                    StringComparison.Ordinal));
                viewModel.ApplyPreferences(new ProfilePreferences(LimitArticleWidth: false));
                Assert.IsFalse(articleViewer.CurrentDocument.Contains("max-width: 900px", StringComparison.Ordinal));
                StringAssert.Contains(articleViewer.CurrentDocument, "max-width: none");
                viewModel.ApplyPreferences(new ProfilePreferences());
                var browser = (Microsoft.Web.WebView2.Wpf.WebView2?)articleViewer.FindName("Browser");
                Assert.IsNotNull(browser);
                Assert.AreEqual(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RssReader",
                        "WebView2"),
                    browser.CreationProperties.UserDataFolder);
                Assert.IsTrue(Directory.Exists(browser.CreationProperties.UserDataFolder));
                PumpDispatcherUntil(articleViewer.InitializationTask);
                Assert.IsFalse(articleViewer.InitializationError is UnauthorizedAccessException,
                    articleViewer.InitializationError?.ToString());
                if (articleViewer.InitializationError is System.Runtime.InteropServices.COMException comException)
                {
                    Assert.AreNotEqual(unchecked((int)0x80070005), comException.HResult, comException.ToString());
                }
                PumpDispatcherUntil(articleViewer.NavigationCompletion);
                Assert.IsTrue(
                    articleViewer.LastNavigationSucceeded,
                    $"Navigation to {articleViewer.LastNavigationUriScheme} failed with {articleViewer.LastNavigationErrorStatus}.");
                var bottomReached = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                articleViewer.ReadingProgressChanged += progress =>
                {
                    if (progress >= 0.95)
                    {
                        bottomReached.TrySetResult(progress);
                    }
                };
                PumpDispatcherUntil(browser.CoreWebView2.ExecuteScriptAsync(
                    "window.scrollTo(0, document.documentElement.scrollHeight);"));
                PumpDispatcherUntil(bottomReached.Task);
                Assert.IsTrue(articleViewer.ReadingProgress >= 0.95);
                var navigationCountBeforeResize = articleViewer.DocumentNavigationCount;
                var scrollPositionRestored = new TaskCompletionSource<double>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                articleViewer.ReadingProgressChanged += progress =>
                {
                    if (progress >= 0.95)
                    {
                        scrollPositionRestored.TrySetResult(progress);
                    }
                };
                var redrawNavigationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                articleViewer.ArticleDocumentNavigationCompleted += () => redrawNavigationCompleted.TrySetResult();
                var articleStatusPanel = (FrameworkElement)articleViewer.FindName("StatusPanel");
                articleViewer.HandleNavigationResult(
                    false,
                    Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.OperationCanceled);
                Assert.AreEqual(Visibility.Visible, browser.Visibility);
                Assert.AreEqual(Visibility.Collapsed, articleStatusPanel.Visibility);
                articleViewer.HandleNavigationResult(
                    false,
                    Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.HostNameNotResolved);
                Assert.AreEqual(Visibility.Collapsed, browser.Visibility);
                Assert.AreEqual(Visibility.Visible, articleStatusPanel.Visibility);
                articleViewer.HandleNavigationResult(
                    true,
                    Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.Unknown);
                Assert.AreEqual(Visibility.Visible, browser.Visibility);
                Assert.AreEqual(Visibility.Collapsed, articleStatusPanel.Visibility);
                window.Width += 180;
                window.UpdateLayout();
                window.Width += 60;
                window.Height += 120;
                window.UpdateLayout();
                Assert.IsTrue(articleViewer.ActualWidth > initialViewerWidth);
                Assert.IsTrue(articleViewer.ActualHeight > initialViewerHeight);
                Assert.AreEqual(articleViewer.ActualWidth, browser.ActualWidth, 1);
                Assert.AreEqual(articleViewer.ActualHeight, browser.ActualHeight, 1);
                var articleTitle = FindReaderElement<TextBlock>(window, "SelectedArticleTitle");
                Assert.AreEqual(
                    new Uri("https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031"),
                    ((Hyperlink)articleTitle.Inlines.Single()).NavigateUri);
                var websiteLink = FindReaderElement<TextBlock>(window, "SelectedArticleWebsiteLink");
                Assert.AreEqual(Visibility.Visible, websiteLink.Visibility);
                Assert.AreEqual(
                    new Uri("https://www.arcamax.com/thefunnies/ninechickweedlane"),
                    ((Hyperlink)websiteLink.Inlines.Single()).NavigateUri);
                var selectedArticleAuthor = FindReaderElement<TextBlock>(window, "SelectedArticleAuthor");
                Assert.AreEqual(Visibility.Visible, selectedArticleAuthor.Visibility);
                var authorInlines = selectedArticleAuthor.Inlines.OfType<Run>()
                    .Select(run => run.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray();
                CollectionAssert.AreEqual(
                    new[] { "By: ", "Jayme Lozano Carver, The Texas Tribune", " (", comicArticle.PublishedDateLabel, ")" },
                    authorInlines,
                    $"Actual author inlines: [{string.Join(" | ", authorInlines)}]");
                PumpDispatcherUntil(redrawNavigationCompleted.Task);
                Assert.AreEqual(navigationCountBeforeResize + 1, articleViewer.DocumentNavigationCount);
                PumpDispatcherUntil(scrollPositionRestored.Task);
                Assert.IsTrue(articleViewer.ReadingProgress >= 0.95);
                Assert.IsTrue(
                    articleViewer.LastNavigationSucceeded,
                    $"The resized article document failed with {articleViewer.LastNavigationErrorStatus}.");
                Assert.AreEqual(Visibility.Visible, browser.Visibility);

                var navigationCountBeforeAppearanceChange = articleViewer.DocumentNavigationCount;
                var appearanceNavigationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                articleViewer.ArticleDocumentNavigationCompleted += () => appearanceNavigationCompleted.TrySetResult();
                viewModel.ApplyPreferences(new ProfilePreferences(
                    ReaderTheme: ProfileReaderTheme.Warm,
                    ReaderTextSize: ProfileReaderTextSize.Large,
                    ReaderLineSpacing: ProfileReaderLineSpacing.Relaxed,
                    ReaderFontFamily: ProfileReaderFontFamily.Serif));
                PumpDispatcherUntil(appearanceNavigationCompleted.Task);
                Assert.AreEqual(
                    navigationCountBeforeAppearanceChange + 1,
                    articleViewer.DocumentNavigationCount,
                    "Applying all reader appearance settings should redraw the document once.");
                Assert.IsTrue(
                    articleViewer.LastNavigationSucceeded,
                    $"The reader appearance update failed with {articleViewer.LastNavigationErrorStatus}.");
                StringAssert.Contains(articleViewer.CurrentDocument, "--reader-background: #f8f2e6");
                StringAssert.Contains(articleViewer.CurrentDocument, "font: 18px/1.85");
                StringAssert.Contains(articleViewer.CurrentDocument, "\"Georgia\", \"Times New Roman\", serif");

                viewModel.BackCommand.Execute(null);
                window.UpdateLayout();

                viewModel.IsMagazineView = true;
                window.UpdateLayout();
                viewModel.IsListView = true;
                window.UpdateLayout();
                viewModel.IsCardsView = true;
                window.UpdateLayout();
                window.Close();

                var catalogMasterViewModel = new MainWindowViewModel(
                    Profile.CreateCatalogMaster(),
                    new CatalogService(new SqliteCatalogStore("unused-catalog.db")));
                var catalogManagement = catalogMasterViewModel.CatalogManagement!;
                var checkedAt = new DateTimeOffset(2026, 10, 3, 16, 14, 0, TimeSpan.Zero);
                var managedFeed = new CatalogFeedListItem(
                    "comic-feed",
                    "(th)ink by Keith Knight",
                    "https://www.comicrss.com/rss/think.rss",
                    null,
                    "Comics RSS",
                    "comics",
                    null,
                    lastHealthCheckedAt: checkedAt,
                    lastHealthCheckSucceeded: false,
                    isEnabled: false);
                catalogManagement.Feeds.Add(managedFeed);
                var managedCategory = new CatalogCategory("comics", "Comics RSS");
                catalogManagement.Categories.Add(managedCategory);
                var categoryTreeNode = new CatalogTaxonomyTreeNode(
                    CatalogTaxonomyTreeNodeKind.Category,
                    managedCategory.Id,
                    managedCategory.Name,
                    catalogManagement,
                    category: managedCategory);
                categoryTreeNode.Children.Add(new CatalogTaxonomyTreeNode(
                    CatalogTaxonomyTreeNodeKind.Placeholder,
                    string.Empty,
                    string.Empty,
                    catalogManagement));
                catalogManagement.CategoryTreeNodes.Add(categoryTreeNode);
                var disabledCategory = new CatalogCategory("disabled-category", "Disabled category", false);
                catalogManagement.Categories.Add(disabledCategory);
                var disabledCategoryTreeNode = new CatalogTaxonomyTreeNode(
                    CatalogTaxonomyTreeNodeKind.Category,
                    disabledCategory.Id,
                    disabledCategory.Name,
                    catalogManagement,
                    category: disabledCategory);
                disabledCategoryTreeNode.Children.Add(new CatalogTaxonomyTreeNode(
                    CatalogTaxonomyTreeNodeKind.Placeholder,
                    string.Empty,
                    string.Empty,
                    catalogManagement));
                catalogManagement.CategoryTreeNodes.Add(disabledCategoryTreeNode);

                var managedCollection = new CatalogCollection("editor-picks", "Editor's picks");
                catalogManagement.Collections.Add(managedCollection);
                var collectionTreeNode = new CatalogTaxonomyTreeNode(
                    CatalogTaxonomyTreeNodeKind.Collection,
                    managedCollection.Id,
                    managedCollection.Name,
                    catalogManagement,
                    collection: managedCollection);
                collectionTreeNode.Children.Add(new CatalogTaxonomyTreeNode(
                    CatalogTaxonomyTreeNodeKind.Feed,
                    managedFeed.Id,
                    managedFeed.Name,
                    catalogManagement,
                    feed: managedFeed,
                    parentId: managedCollection.Id));
                collectionTreeNode.IsChildrenLoaded = true;
                catalogManagement.CollectionTreeNodes.Add(collectionTreeNode);

                var catalogMasterWindow = CreateTestMainWindow(catalogMasterViewModel);
                catalogMasterWindow.Show();
                catalogMasterWindow.UpdateLayout();
                var catalogMasterAdminView = (UserControl)catalogMasterWindow.FindName("CatalogAdminView")!;
                var checkAllButton = FindVisualChildren<Button>(catalogMasterAdminView)
                    .Single(button => AutomationProperties.GetName(button) == "Check All");
                Assert.AreEqual("Check All", checkAllButton.Content);
                var catalogImportReport = (Grid)catalogMasterAdminView.FindName("CatalogImportReport");
                var dismissImportReportButton = (Button)catalogMasterAdminView.FindName("DismissCatalogImportReportButton");
                Assert.AreEqual(Visibility.Collapsed, catalogImportReport.Visibility);
                Assert.AreEqual("Dismiss import report", AutomationProperties.GetName(dismissImportReportButton));
                Assert.AreSame(catalogManagement.DismissImportMessageCommand, dismissImportReportButton.Command);
                Assert.AreEqual(1, catalogManagement.VisibleFeedCount);
                ((TabControl)catalogMasterAdminView.FindName("CatalogManagementTabs")).SelectedIndex = 0;
                catalogMasterWindow.UpdateLayout();
                var managementList = (ListBox)catalogMasterAdminView.FindName("CatalogManagementFeedList");
                Assert.AreSame(
                    catalogMasterViewModel,
                    managementList.DataContext,
                    $"Unexpected feed list context: {managementList.DataContext?.GetType().FullName ?? "null"}");
                Assert.AreEqual(1, managementList.Items.Count);
                Assert.IsTrue(catalogMasterViewModel.IsCatalogAdminVisible);
                Assert.IsTrue(managementList.IsLoaded, $"Feed list loaded: {managementList.IsLoaded}; height: {managementList.ActualHeight}");
                Assert.IsTrue(managementList.ActualHeight > 0, $"Feed list height: {managementList.ActualHeight}");
                Assert.AreEqual(1280, managementList.Parent is Grid feedResultsContainer
                    ? feedResultsContainer.Width
                    : double.NaN);
                managementList.ScrollIntoView(managedFeed);
                catalogMasterWindow.UpdateLayout();
                managementList.UpdateLayout();
                var managedFeedContainer = managementList.ItemContainerGenerator.ContainerFromItem(managedFeed)
                    ?? throw new AssertFailedException("The Catalog Master feed row was not created.");
                var managedFeedRow = FindVisualChildren<Border>(managedFeedContainer)
                    .Single(border => ReferenceEquals(border.DataContext, managedFeed) && border.ContextMenu is not null);
                Assert.AreSame(catalogMasterWindow.FindResource("DestructiveWashBrush"), managedFeedRow.Background);
                var managementActions = FindVisualChildren<Button>(managedFeedContainer).ToArray();
                CollectionAssert.AreEquivalent(
                    new[] { "Preview feed", "Edit feed", "Choose collections for feed", "Check feed", "Remove feed" },
                    managementActions.Select(button => AutomationProperties.GetName(button)).ToArray());
                Assert.IsTrue(managementActions.All(button => button.FontWeight == FontWeights.Bold));
                Assert.IsTrue(managementActions.All(button =>
                    FindVisualChildren<TextBlock>(button).Single().FontWeight == FontWeights.Bold));
                Assert.AreEqual(
                    catalogMasterWindow.FindResource("NeutralActionBrush"),
                    FindVisualChildren<TextBlock>(managementActions.Single(button =>
                        AutomationProperties.GetName(button) == "Preview feed")).Single().Foreground);
                Assert.AreEqual(
                    catalogMasterWindow.FindResource("SuccessBrush"),
                    FindVisualChildren<TextBlock>(managementActions.Single(button =>
                        AutomationProperties.GetName(button) == "Check feed")).Single().Foreground);
                Assert.AreEqual(
                    catalogMasterWindow.FindResource("DestructiveBrush"),
                    FindVisualChildren<TextBlock>(managementActions.Single(button =>
                        AutomationProperties.GetName(button) == "Remove feed")).Single().Foreground);
                var managementActionPanel = FindVisualChildren<StackPanel>(managedFeedContainer)
                    .Single(panel => Grid.GetColumn(panel) == 1 && panel.Children.OfType<Button>().Any());
                Assert.AreEqual(Visibility.Collapsed, managementActionPanel.Visibility);
                var managementDeleteButton = managementActions.Single(button =>
                    AutomationProperties.GetName(button) == "Remove feed");
                Assert.AreEqual(28, managementDeleteButton.Width);
                Assert.AreEqual(28, managementActionPanel.Margin.Right);
                var managedCategoryLabel = FindVisualChildren<TextBlock>(managedFeedContainer)
                    .Single(text => text.Text == managedFeed.CategoryName);
                var managedCategoryLabelRight = managedCategoryLabel
                    .TranslatePoint(new Point(managedCategoryLabel.ActualWidth, 0), managedFeedRow).X;
                Assert.AreEqual(managedFeedRow.ActualWidth - 14, managedCategoryLabelRight, 3);
                managementActionPanel.Visibility = Visibility.Visible;
                managementActionPanel.Opacity = 1;
                catalogMasterWindow.UpdateLayout();
                var managementDeleteRight = managementDeleteButton
                    .TranslatePoint(new Point(managementDeleteButton.ActualWidth, 0), managementList).X;
                Assert.IsTrue(
                    managementDeleteRight <= managementList.ActualWidth - 20,
                    $"The delete icon is too close to the list edge ({managementDeleteRight} of {managementList.ActualWidth}).");
                managementActionPanel.ClearValue(UIElement.VisibilityProperty);
                managementActionPanel.ClearValue(UIElement.OpacityProperty);
                catalogMasterWindow.UpdateLayout();
                var collectionsAction = managementActions.Single(button =>
                    AutomationProperties.GetName(button) == "Choose collections for feed");
                Assert.AreEqual("\uE8D4", (collectionsAction.Content as TextBlock)?.Text);
                var managedTitle = FindVisualChildren<TextBlock>(managedFeedContainer).Single(text => text.Text == managedFeed.Name);
                Assert.AreEqual(14, managedTitle.FontSize);
                Assert.IsTrue(managedTitle.TextDecorations.Any(decoration =>
                    decoration.Location == TextDecorationLocation.Strikethrough));
                var managedDescription = FindVisualChildren<TextBlock>(managedFeedContainer)
                    .Single(text => AutomationProperties.GetName(text) == "Catalog feed description");
                Assert.AreEqual(Visibility.Collapsed, managedDescription.Visibility);
                Assert.IsFalse(FindVisualChildren<TextBlock>(managedFeedContainer)
                    .Any(text => text.Text == "\uE7BA"));
                var healthIcon = FindVisualChildren<TextBlock>(managedFeedContainer)
                    .Single(text => AutomationProperties.GetName(text).StartsWith("Check failed - checked", StringComparison.Ordinal));
                Assert.AreEqual(managedFeed.HealthCheckDisplay, healthIcon.ToolTip);
                Assert.AreEqual("\uE711", healthIcon.Text);

                var disabledFeed = new CatalogFeedListItem(
                    "disabled-comic-feed",
                    "Disabled comic feed",
                    "https://example.com/disabled-comic.xml",
                    null,
                    "Comics RSS",
                    managedCategory.Id,
                    isEnabledInCategory: false);
                catalogManagement.Feeds.Add(disabledFeed);

                var managementTabs = (TabControl)catalogMasterAdminView.FindName("CatalogManagementTabs");
                managementTabs.SelectedIndex = 1;
                catalogMasterWindow.UpdateLayout();
                catalogManagement.LoadTaxonomyChildren(categoryTreeNode);
                categoryTreeNode.IsExpanded = true;
                catalogMasterWindow.UpdateLayout();
                var renameButton = FindVisualChildren<Button>(catalogMasterWindow)
                    .First(button => button.IsVisible && AutomationProperties.GetName(button) == "Rename category");
                var removeCategoryButton = FindVisualChildren<Button>(catalogMasterWindow)
                    .First(button => button.IsVisible && AutomationProperties.GetName(button) == "Remove category");
                var checkCategoryFeedsButton = FindVisualChildren<Button>(catalogMasterWindow)
                    .First(button => button.IsVisible && AutomationProperties.GetName(button) == "Check feeds");
                Assert.AreEqual(1, renameButton.Opacity);
                Assert.AreEqual(1, removeCategoryButton.Opacity);
                Assert.AreEqual(FontWeights.Bold, renameButton.FontWeight);
                Assert.AreEqual(FontWeights.Bold, checkCategoryFeedsButton.FontWeight);
                Assert.AreEqual(FontWeights.Bold, removeCategoryButton.FontWeight);
                Assert.AreEqual("\uE72A", FindVisualChildren<TextBlock>(checkCategoryFeedsButton).Single().Text);
                Assert.AreEqual(FontWeights.Bold, FindVisualChildren<TextBlock>(checkCategoryFeedsButton).Single().FontWeight);
                Assert.AreSame(
                    catalogMasterWindow.FindResource("SuccessBrush"),
                    FindVisualChildren<TextBlock>(checkCategoryFeedsButton).Single().Foreground);
                var renameCollectionButton = FindVisualChildren<Button>(catalogMasterWindow)
                    .Single(button => button.IsVisible && AutomationProperties.GetName(button) == "Rename collection");
                var manageCollectionFeedsButton = FindVisualChildren<Button>(catalogMasterWindow)
                    .Single(button => button.IsVisible && AutomationProperties.GetName(button) == "Manage feeds in collection");
                var removeCollectionButton = FindVisualChildren<Button>(catalogMasterWindow)
                    .Single(button => button.IsVisible && AutomationProperties.GetName(button) == "Remove collection");
                Assert.AreEqual(1, manageCollectionFeedsButton.Opacity);
                Assert.AreEqual(1, renameCollectionButton.Opacity);
                Assert.AreEqual(1, removeCollectionButton.Opacity);
                var addCategoryButton = FindVisualChildren<Button>(catalogMasterWindow)
                    .Single(button => button.IsVisible && AutomationProperties.GetName(button) == "Add category to collection");
                Assert.AreEqual(1, addCategoryButton.Opacity);
                Assert.AreNotEqual(
                    FindVisualChildren<TextBlock>(addCategoryButton).Single().Text,
                    FindVisualChildren<TextBlock>(manageCollectionFeedsButton).Single().Text);
                Assert.AreEqual(catalogMasterWindow.FindResource("NeutralActionBrush"),
                    FindVisualChildren<TextBlock>(renameButton).Single().Foreground);
                Assert.AreEqual(catalogMasterWindow.FindResource("DestructiveBrush"),
                    FindVisualChildren<TextBlock>(removeCategoryButton).Single().Foreground);
                Assert.AreEqual(catalogMasterWindow.FindResource("SuccessBrush"),
                    FindVisualChildren<TextBlock>(addCategoryButton).Single().Foreground);
                Assert.AreEqual(catalogMasterWindow.FindResource("NeutralActionBrush"),
                    FindVisualChildren<TextBlock>(manageCollectionFeedsButton).Single().Foreground);

                var taxonomyTrees = FindVisualChildren<TreeView>(catalogMasterAdminView);
                var categoryTree = taxonomyTrees.Single(tree =>
                    ReferenceEquals(tree.ItemsSource, catalogManagement.CategoryTreeNodes));
                var taxonomyViewport = (Grid)catalogMasterAdminView.FindName("CatalogTaxonomyViewport");
                var taxonomyLayout = (Grid)catalogMasterAdminView.FindName("CatalogTaxonomyLayout");
                Assert.AreEqual(HorizontalAlignment.Left, taxonomyLayout.HorizontalAlignment);
                Assert.AreEqual(0, taxonomyLayout.TranslatePoint(new Point(0, 0), taxonomyViewport).X, 1);
                Assert.IsTrue(taxonomyLayout.ActualWidth <= 1280);
                var initialCategoryTreeWidth = categoryTree.ActualWidth;
                Assert.AreEqual(
                    Color.FromRgb(217, 234, 248),
                    ((SolidColorBrush)categoryTree.FindResource(SystemColors.HighlightBrushKey)).Color);
                Assert.AreEqual(
                    Color.FromRgb(32, 41, 39),
                    ((SolidColorBrush)categoryTree.FindResource(SystemColors.HighlightTextBrushKey)).Color);
                var initialCollectionTreeWidth = taxonomyTrees.Single(tree =>
                    ReferenceEquals(tree.ItemsSource, catalogManagement.CollectionTreeNodes)).ActualWidth;
                for (var layoutPass = 0; layoutPass < 4; layoutPass++)
                {
                    catalogMasterWindow.UpdateLayout();
                }

                Assert.IsTrue(categoryTree.ActualWidth > 300, "The category tree should retain a practical pane width.");
                Assert.AreEqual(initialCategoryTreeWidth, categoryTree.ActualWidth, 1);
                var collectionTree = taxonomyTrees.Single(tree =>
                    ReferenceEquals(tree.ItemsSource, catalogManagement.CollectionTreeNodes));
                Assert.IsTrue(collectionTree.ActualWidth > 300, "The collection tree should retain a practical pane width.");
                Assert.AreEqual(initialCollectionTreeWidth, collectionTree.ActualWidth, 1);
                var categoryTreeItem = (TreeViewItem?)categoryTree.ItemContainerGenerator.ContainerFromItem(categoryTreeNode)
                    ?? throw new AssertFailedException("The category tree item was not created.");
                var disabledFeedNode = categoryTreeNode.Children.Single(node => node.Feed?.Id == disabledFeed.Id);
                var disabledFeedItem = (TreeViewItem?)categoryTreeItem.ItemContainerGenerator.ContainerFromItem(disabledFeedNode)
                    ?? throw new AssertFailedException("The disabled category feed tree item was not created.");
                var categoryRow = FindVisualChildren<Border>(categoryTreeItem)
                    .Single(border => ReferenceEquals(border.DataContext, categoryTreeNode) && border.ContextMenu is not null);
                var disabledFeedRow = FindVisualChildren<Border>(disabledFeedItem)
                    .Single(border => ReferenceEquals(border.DataContext, disabledFeedNode) && border.ContextMenu is not null);
                var failedCategoryFeedNode = categoryTreeNode.Children.Single(node => node.Feed?.Id == managedFeed.Id);
                var failedCategoryFeedItem = (TreeViewItem?)categoryTreeItem.ItemContainerGenerator
                    .ContainerFromItem(failedCategoryFeedNode)
                    ?? throw new AssertFailedException("The failed category feed tree item was not created.");
                var failedCategoryFeedRow = FindVisualChildren<Border>(failedCategoryFeedItem)
                    .Single(border => ReferenceEquals(border.DataContext, failedCategoryFeedNode) && border.ContextMenu is not null);
                Assert.AreEqual(40, categoryRow.Height);
                Assert.AreEqual(40, disabledFeedRow.Height);
                Assert.AreEqual(HorizontalAlignment.Stretch, categoryRow.HorizontalAlignment);
                Assert.AreEqual(HorizontalAlignment.Stretch, disabledFeedRow.HorizontalAlignment);
                Assert.AreEqual(0, ((SolidColorBrush)categoryRow.Background).Color.A);
                Assert.AreEqual(0, ((SolidColorBrush)disabledFeedRow.Background).Color.A);
                Assert.AreSame(catalogMasterWindow.FindResource("DestructiveWashBrush"), failedCategoryFeedRow.Background);
                Assert.AreEqual(1, categoryRow.BorderThickness.Bottom);
                Assert.AreEqual(1, disabledFeedRow.BorderThickness.Bottom);
                Assert.AreSame(catalogMasterWindow.FindResource("SeparatorBrush"), disabledFeedRow.BorderBrush);
                var categoryDisableButton = FindVisualChildren<Button>(categoryRow)
                    .Single(button => AutomationProperties.GetName(button) == "Disable");
                Assert.AreEqual("\uE8D7", FindVisualChildren<TextBlock>(categoryDisableButton).Single().Text);
                Assert.AreSame(
                    catalogMasterWindow.FindResource("DestructiveBrush"),
                    FindVisualChildren<TextBlock>(categoryDisableButton).Single().Foreground);
                var categoryDisableRight = categoryDisableButton
                    .TranslatePoint(new Point(categoryDisableButton.ActualWidth, 0), categoryRow).X;
                var categoryDeleteLeft = removeCategoryButton
                    .TranslatePoint(new Point(0, 0), categoryRow).X;
                Assert.IsTrue(
                    categoryDisableRight < categoryDeleteLeft,
                    "The category Disable action should be immediately left of Delete.");
                var removeCategoryButtonRight = removeCategoryButton
                    .TranslatePoint(new Point(removeCategoryButton.ActualWidth, 0), categoryRow).X;
                Assert.AreEqual(categoryRow.ActualWidth - 6, removeCategoryButtonRight, 3);
                var disabledFeedActions = FindVisualChildren<Button>(disabledFeedItem)
                    .Where(button => button.IsVisible)
                    .ToArray();
                CollectionAssert.AreEquivalent(
                    new[] { "Preview feed", "Enable", "Remove feed permanently" },
                    disabledFeedActions.Select(button => AutomationProperties.GetName(button)).ToArray());
                var disabledFeedPreviewButton = disabledFeedActions.Single(button =>
                    AutomationProperties.GetName(button) == "Preview feed");
                var enableFeedButton = disabledFeedActions.Single(button => AutomationProperties.GetName(button) == "Enable");
                var removeFeedButton = disabledFeedActions.Single(button =>
                    AutomationProperties.GetName(button) == "Remove feed permanently");
                Assert.AreEqual("\uE890", FindVisualChildren<TextBlock>(disabledFeedPreviewButton).Single().Text);
                Assert.AreSame(
                    catalogMasterWindow.FindResource("AccentBrush"),
                    FindVisualChildren<TextBlock>(disabledFeedPreviewButton).Single().Foreground);
                Assert.AreEqual("\uE8D7", FindVisualChildren<TextBlock>(enableFeedButton).Single().Text);
                Assert.AreSame(
                    catalogMasterWindow.FindResource("SuccessBrush"),
                    FindVisualChildren<TextBlock>(enableFeedButton).Single().Foreground);
                Assert.AreSame(
                    catalogMasterWindow.FindResource("DestructiveBrush"),
                    FindVisualChildren<TextBlock>(removeFeedButton).Single().Foreground);
                var removeFeedButtonRight = removeFeedButton
                    .TranslatePoint(new Point(removeFeedButton.ActualWidth, 0), disabledFeedRow).X;
                Assert.AreEqual(disabledFeedRow.ActualWidth - 6, removeFeedButtonRight, 3);
                var categoryActionsRight = removeCategoryButton
                    .TranslatePoint(new Point(removeCategoryButton.ActualWidth, 0), categoryTree).X;
                var categoryFeedActionsRight = removeFeedButton
                    .TranslatePoint(new Point(removeFeedButton.ActualWidth, 0), categoryTree).X;
                Assert.AreEqual(
                    categoryActionsRight,
                    categoryFeedActionsRight,
                    3,
                    "Category and child actions should align at the tree's right edge.");
                var disabledFeedLabel = FindVisualChildren<TextBlock>(disabledFeedItem)
                    .Single(text => text.Text == disabledFeed.Name);
                Assert.AreSame(catalogMasterWindow.FindResource("MutedTextBrush"), disabledFeedLabel.Foreground);
                Assert.IsTrue(disabledFeedLabel.TextDecorations.Any(decoration =>
                    decoration.Location == TextDecorationLocation.Strikethrough));

                var categoryContextMenu = categoryRow.ContextMenu!;
                categoryContextMenu.PlacementTarget = categoryRow;
                categoryContextMenu.IsOpen = true;
                catalogMasterWindow.UpdateLayout();
                Assert.AreEqual(
                    Visibility.Visible,
                    categoryContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Rename category")).Visibility);
                Assert.AreEqual(
                    Visibility.Visible,
                    categoryContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Remove category")).Visibility);
                var disableCategoryMenuItem = categoryContextMenu.Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, "Disable"));
                Assert.AreEqual(Visibility.Visible, disableCategoryMenuItem.Visibility);
                Assert.AreSame(
                    catalogMasterWindow.FindResource("DestructiveBrush"),
                    ((TextBlock)disableCategoryMenuItem.Icon).Foreground);
                categoryContextMenu.IsOpen = false;
                var disabledFeedContextMenu = disabledFeedRow.ContextMenu!;
                disabledFeedContextMenu.PlacementTarget = disabledFeedRow;
                disabledFeedContextMenu.IsOpen = true;
                catalogMasterWindow.UpdateLayout();
                Assert.AreEqual(
                    Visibility.Visible,
                    disabledFeedContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Preview feed")).Visibility);
                disabledFeedContextMenu.IsOpen = false;

                var disabledCategoryTreeItem = (TreeViewItem?)categoryTree.ItemContainerGenerator
                    .ContainerFromItem(disabledCategoryTreeNode)
                    ?? throw new AssertFailedException("The disabled category tree item was not created.");
                var disabledCategoryRow = FindVisualChildren<Border>(disabledCategoryTreeItem)
                    .Single(border => ReferenceEquals(border.DataContext, disabledCategoryTreeNode)
                        && border.ContextMenu is not null);
                var disabledCategoryLabel = FindVisualChildren<TextBlock>(disabledCategoryTreeItem)
                    .Single(text => text.Text == disabledCategory.Name);
                Assert.IsTrue(disabledCategoryLabel.TextDecorations.Any(decoration =>
                    decoration.Location == TextDecorationLocation.Strikethrough));
                var disabledCategoryContextMenu = disabledCategoryRow.ContextMenu!;
                var enableCategoryButton = FindVisualChildren<Button>(disabledCategoryRow)
                    .Single(button => AutomationProperties.GetName(button) == "Enable");
                Assert.AreSame(
                    catalogMasterWindow.FindResource("SuccessBrush"),
                    FindVisualChildren<TextBlock>(enableCategoryButton).Single().Foreground);
                disabledCategoryContextMenu.PlacementTarget = disabledCategoryRow;
                disabledCategoryContextMenu.IsOpen = true;
                catalogMasterWindow.UpdateLayout();
                var enableCategoryMenuItem = disabledCategoryContextMenu.Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, "Enable"));
                Assert.AreEqual(Visibility.Visible, enableCategoryMenuItem.Visibility);
                Assert.AreSame(
                    catalogMasterWindow.FindResource("SuccessBrush"),
                    ((TextBlock)enableCategoryMenuItem.Icon).Foreground);
                disabledCategoryContextMenu.IsOpen = false;

                var collectionTreeItem = (TreeViewItem?)collectionTree.ItemContainerGenerator.ContainerFromItem(collectionTreeNode)
                    ?? throw new AssertFailedException("The collection tree item was not created.");
                var collectionRow = FindVisualChildren<Border>(collectionTreeItem)
                    .Single(border => ReferenceEquals(border.DataContext, collectionTreeNode) && border.ContextMenu is not null);
                var collectionContextMenu = collectionRow.ContextMenu!;
                collectionContextMenu.PlacementTarget = collectionRow;
                collectionContextMenu.IsOpen = true;
                catalogMasterWindow.UpdateLayout();
                Assert.AreEqual(
                    Visibility.Visible,
                    collectionContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Add category")).Visibility);
                Assert.AreEqual(
                    Visibility.Visible,
                    collectionContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Manage feeds")).Visibility);
                collectionContextMenu.IsOpen = false;

                collectionTreeNode.IsExpanded = true;
                catalogMasterWindow.UpdateLayout();
                var collectionFeedNode = collectionTreeNode.Children.Single(node => node.Feed?.Id == managedFeed.Id);
                var collectionFeedItem = (TreeViewItem?)collectionTreeItem.ItemContainerGenerator
                    .ContainerFromItem(collectionFeedNode)
                    ?? throw new AssertFailedException("The collection feed tree item was not created.");
                var removeCollectionTreeButton = removeCollectionButton;
                var collectionFeedRow = FindVisualChildren<Border>(collectionFeedItem)
                    .Single(border => ReferenceEquals(border.DataContext, collectionFeedNode) && border.ContextMenu is not null);
                Assert.AreSame(catalogMasterWindow.FindResource("DestructiveWashBrush"), collectionFeedRow.Background);
                var collectionFeedActions = FindVisualChildren<Button>(collectionFeedItem)
                    .Where(button => button.IsVisible)
                    .ToArray();
                CollectionAssert.AreEquivalent(
                    new[] { "Preview feed", "Enable globally", "Remove from collection" },
                    collectionFeedActions.Select(button => AutomationProperties.GetName(button)).ToArray());
                Assert.AreEqual(40, collectionFeedRow.Height);
                Assert.AreEqual(1, collectionFeedRow.BorderThickness.Bottom);
                var collectionFeedLabel = FindVisualChildren<TextBlock>(collectionFeedItem)
                    .Single(text => text.Text == managedFeed.Name);
                Assert.AreSame(catalogMasterWindow.FindResource("MutedTextBrush"), collectionFeedLabel.Foreground);
                Assert.IsFalse(collectionFeedLabel.TextDecorations.Any(decoration =>
                    decoration.Location == TextDecorationLocation.Strikethrough));
                var removeCollectionFeedButton = collectionFeedActions.Single(button =>
                    AutomationProperties.GetName(button) == "Remove from collection");
                var collectionFeedPreviewButton = collectionFeedActions.Single(button =>
                    AutomationProperties.GetName(button) == "Preview feed");
                Assert.AreEqual("\uE890", FindVisualChildren<TextBlock>(collectionFeedPreviewButton).Single().Text);
                var removeCollectionFeedButtonRight = removeCollectionFeedButton
                    .TranslatePoint(new Point(removeCollectionFeedButton.ActualWidth, 0), collectionFeedRow).X;
                Assert.AreEqual(collectionFeedRow.ActualWidth - 6, removeCollectionFeedButtonRight, 2);
                var collectionActionsRight = removeCollectionTreeButton
                    .TranslatePoint(new Point(removeCollectionTreeButton.ActualWidth, 0), collectionTree).X;
                var collectionFeedActionsRight = removeCollectionFeedButton
                    .TranslatePoint(new Point(removeCollectionFeedButton.ActualWidth, 0), collectionTree).X;
                Assert.AreEqual(collectionActionsRight, collectionFeedActionsRight, 3);
                var collectionFeedContextMenu = collectionFeedRow.ContextMenu!;
                collectionFeedContextMenu.PlacementTarget = collectionFeedRow;
                collectionFeedContextMenu.IsOpen = true;
                catalogMasterWindow.UpdateLayout();
                Assert.AreEqual(
                    Visibility.Visible,
                    collectionFeedContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Preview feed")).Visibility);
                collectionFeedContextMenu.IsOpen = false;

                var collectionRenameDialogShown = false;
                var closeCollectionRenameDialogTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(50)
                };
                closeCollectionRenameDialogTimer.Tick += (_, _) =>
                {
                    closeCollectionRenameDialogTimer.Stop();
                    var dialog = System.Windows.Application.Current.Windows.OfType<CatalogEntryWindow>().Single();
                    Assert.AreEqual("Rename collection", ((TextBlock)dialog.FindName("DialogHeading")!).Text);
                    Assert.AreEqual("Editor's picks", ((TextBox)dialog.FindName("CollectionNameBox")!).Text);
                    dialog.DialogResult = false;
                    collectionRenameDialogShown = true;
                };
                closeCollectionRenameDialogTimer.Start();
                renameCollectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, renameCollectionButton));
                Assert.IsTrue(collectionRenameDialogShown);
                catalogMasterWindow.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void FeedTreeFolderCanExpandAndCollapseWithoutWpfAnimationErrors()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            try
            {
                var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                var folder = new SidebarLink("folder:Comics", "Comics", string.Empty, "(1)", indentLevel: 1);
                var feed = new SidebarLink("feed:comics", "Example comic", "\uE774", indentLevel: 2, parentFolder: folder);
                viewModel.FeedLinks.Add(folder);
                viewModel.FeedLinks.Add(feed);
                var window = CreateTestMainWindow(viewModel);
                window.Show();
                window.UpdateLayout();
                var refreshButton = (Button)window.FindName("RefreshButton");
                Assert.AreEqual(38, refreshButton.Height);
                Assert.AreEqual(112, refreshButton.MinWidth);
                Assert.AreEqual("#FF476F63", refreshButton.Background.ToString());
                Assert.AreEqual("#FFFFFFFF", refreshButton.Foreground.ToString());
                Assert.AreEqual("Refresh followed feeds", AutomationProperties.GetName(refreshButton));
                var folderRow = FindVisualChildren<Grid>(window)
                    .Single(grid => ReferenceEquals(grid.DataContext, folder) && grid.Name == "SidebarLinkRoot");
                var folderButton = folderRow.Children.OfType<Button>().First();
                var toggleFolderButton = FindVisualChildren<Button>(folderRow)
                    .Single(button => ReferenceEquals(button.Command, viewModel.ToggleSidebarFolderCommand));
                var deleteFolderButton = FindVisualChildren<Button>(folderRow)
                    .Single(button => AutomationProperties.GetName(button) == "Delete folder");
                Assert.AreEqual(0, deleteFolderButton.Opacity);
                viewModel.ActivateSidebarLinkCommand.Execute(folder);
                window.UpdateLayout();
                Assert.IsFalse(folder.IsExpanded);
                Assert.IsTrue(folder.IsSelected);
                Assert.AreEqual("folder:Comics", viewModel.ActiveRoute);
                var selectedBorder = FindVisualChildren<Border>(folderButton)
                    .Single(border => border.Name == "ButtonBorder");
                var selectedBrush = (SolidColorBrush)window.FindResource("NeutralPressedBrush");
                Assert.AreEqual(selectedBrush.Color, ((SolidColorBrush)selectedBorder.Background).Color);

                folderRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
                window.UpdateLayout();
                Assert.AreEqual(1, deleteFolderButton.Opacity);
                folderRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                window.UpdateLayout();
                Assert.AreEqual(0, deleteFolderButton.Opacity);

                folderRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
                window.UpdateLayout();
                Assert.AreEqual(1, deleteFolderButton.Opacity);
                folderRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                window.UpdateLayout();
                Assert.AreEqual(0, deleteFolderButton.Opacity);

                Assert.IsTrue(folderButton.Focus());
                window.UpdateLayout();
                Assert.IsTrue(folderRow.IsKeyboardFocusWithin);
                Assert.AreEqual(0, deleteFolderButton.Opacity);

                Assert.AreEqual(0, deleteFolderButton.Opacity);
                Assert.AreSame(viewModel.ToggleSidebarFolderCommand, toggleFolderButton.Command);
                Assert.AreSame(folder, toggleFolderButton.CommandParameter);
                toggleFolderButton.Command.Execute(toggleFolderButton.CommandParameter);
                window.UpdateLayout();
                Assert.IsTrue(folder.IsExpanded);
                Assert.AreEqual("folder:Comics", viewModel.ActiveRoute);
                toggleFolderButton.Command.Execute(toggleFolderButton.CommandParameter);
                window.UpdateLayout();
                Assert.IsFalse(folder.IsExpanded);
                Assert.AreEqual("folder:Comics", viewModel.ActiveRoute);
                window.Close();
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject =>
        FindVisualChildren<T>(parent).FirstOrDefault();

    private static RssReader.App.MainWindow CreateTestMainWindow(MainWindowViewModel viewModel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rss-reader-window-test-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "main-window-placement.json");
        var window = new RssReader.App.MainWindow(
            viewModel,
            new RssReader.App.WindowPlacementStore(filePath));
        window.Closed += (_, _) => DeleteWindowPlacementFiles(filePath);
        return window;
    }

    private static void DeleteWindowPlacementFiles(string filePath)
    {
        if (!File.Exists(filePath) && !File.Exists($"{filePath}.tmp"))
        {
            return;
        }

        File.Delete(filePath);
        File.Delete($"{filePath}.tmp");
        var directory = Path.GetDirectoryName(filePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory);
        }
    }

    private static nint MakeScreenPoint(int x, int y)
    {
        var coordinates = unchecked((uint)(ushort)x | ((uint)(ushort)y << 16));
        return new nint(unchecked((int)coordinates));
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeWindowRectangle rectangle);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint windowHandle, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var childIndex = 0; childIndex < childCount; childIndex++)
        {
            var child = VisualTreeHelper.GetChild(parent, childIndex);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    [TestMethod]
    public async Task IdenticalArticlesFromDifferentFeedsCollapseInCombinedViewsAndShareState()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var firstFeed = new CatalogFeed("bbc-feed-1", "BBC World", "https://example.com/first.xml", "News", null);
            var secondFeed = new CatalogFeed("bbc-feed-2", "BBC Headlines", "https://example.com/second.xml", "News", null);
            await catalogStore.AddFeedAsync(firstFeed);
            await catalogStore.AddFeedAsync(secondFeed);
            await readerStore.AddFolderAsync(profile.Id, "News");
            await readerStore.SubscribeAsync(profile.Id, firstFeed.Id, "News");
            await readerStore.SubscribeAsync(profile.Id, secondFeed.Id, "News");

            var publishedAt = DateTimeOffset.Parse("2026-10-10T05:00:18Z");
            const string storyLink = "https://www.bbc.co.uk/news/articles/cm70pee7l57do?at_medium=RSS&at_campaign=rss";
            const string storyGuid = "https://www.bbc.co.uk/news/articles/cm70pee7l57do#3";
            const string storyTitle = "Queen reflects on reading 'forbidden' Lady Chatterley's Lover under the pillow";
            const string storySummary =
                "In BBC documentary The Book That Changed My Life, the Queen speaks about memorable moments in her own reading.";
            var firstArticle = new FeedArticle(
                "bbc-article-1",
                firstFeed.Id,
                storyGuid,
                storyTitle,
                storyLink,
                publishedAt,
                storySummary,
                null);
            var secondArticle = firstArticle with { Id = "bbc-article-2", FeedId = secondFeed.Id };
            await readerStore.SaveArticlesAsync(firstFeed.Id, [firstArticle]);
            await readerStore.SaveArticlesAsync(secondFeed.Id, [secondArticle]);
            Assert.AreEqual(2, (await readerStore.GetArticlesAsync(profile.Id)).Count);

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null,
                new ProfilePreferences(StartPage: ProfileStartPage.All));
            await viewModel.InitializeAsync();

            Assert.AreEqual(1, viewModel.VisibleArticles.Count);
            Assert.AreEqual(storyLink, viewModel.VisibleArticles.Single().Link);
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "folder:News"));
            Assert.AreEqual(1, viewModel.VisibleArticles.Count);

            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == $"feed:{firstFeed.Id}"));
            Assert.AreEqual("bbc-article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == $"feed:{secondFeed.Id}"));
            Assert.AreEqual("bbc-article-2", viewModel.VisibleArticles.Single().ArticleId);

            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));
            viewModel.VisibleArticles.Single().IsRead = true;
            await WaitForArticleStateAsync(item => item.IsRead);
            Assert.AreEqual(0, viewModel.FeedLinks.Single(link => link.Route == "All").UnreadCount);
            viewModel.UnreadOnly = true;
            Assert.AreEqual(0, viewModel.VisibleArticles.Count);

            viewModel.UnreadOnly = false;
            viewModel.VisibleArticles.Single().IsSaved = true;
            await WaitForArticleStateAsync(item => item.IsSaved);
            Assert.IsTrue((await readerStore.GetArticlesAsync(profile.Id)).All(item => item.IsSaved));

            async Task WaitForArticleStateAsync(Func<ArticleForProfile, bool> stateIsSet)
            {
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    var storedArticles = await readerStore.GetArticlesAsync(profile.Id);
                    if (storedArticles.Count == 2 && storedArticles.All(stateIsSet))
                    {
                        return;
                    }

                    await Task.Delay(10);
                }

                Assert.Fail("The combined story state was not persisted to every feed copy.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task FeedTagAssignmentsUpdateSidebarAndArticleRows()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var feed = new CatalogFeed("tag-feed", "Gaming News", "https://example.com/feed.xml", "News", null);
            await catalogStore.AddFeedAsync(feed);
            await readerStore.AddFolderAsync(profile.Id, "Gaming");
            await readerStore.SubscribeAsync(profile.Id, feed.Id, "Gaming");
            await readerStore.SaveArticlesAsync(feed.Id,
            [
                new FeedArticle("tag-article", feed.Id, "item-1", "Handheld review", null, DateTimeOffset.UtcNow, "Portable hardware", "Full story")
            ]);

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();

            await viewModel.UpdateFeedTagsAsync(feed.Id, ["Reviews", "News", " reviews "]);

            Assert.AreEqual(2, viewModel.TagLinks.Count);
            Assert.AreEqual("(1)", viewModel.TagLinks.Single(link => link.Label == "Reviews").Count);
            CollectionAssert.AreEquivalent(
                new[] { "Reviews", "News" },
                viewModel.VisibleArticles.Single().Tags.ToArray());
            var editorData = await viewModel.GetFeedTagEditorDataAsync(feed.Id);
            CollectionAssert.AreEqual(new[] { "News", "Reviews" }, editorData.AvailableTags.ToArray());
            CollectionAssert.AreEqual(new[] { "News", "Reviews" }, editorData.AssignedTags.ToArray());

            viewModel.SelectFeedTagCommand.Execute(
                viewModel.VisibleArticles.Single().FeedTagChips.Single(chip => chip.Name == "Reviews"));
            Assert.AreEqual("tag:Reviews", viewModel.ActiveRoute);
            await viewModel.UpdateFeedTagsAsync(feed.Id, ["News"]);
            Assert.AreEqual("All", viewModel.ActiveRoute);
            Assert.AreEqual("(1)", viewModel.TagLinks.Single().Count);

            await viewModel.UpdateFeedTagsAsync(feed.Id, []);
            Assert.AreEqual(0, viewModel.TagLinks.Count);
            Assert.AreEqual(0, viewModel.VisibleArticles.Single().Tags.Count);

            viewModel.SelectArticleCommand.Execute(viewModel.VisibleArticles.Single());
            Assert.IsTrue(viewModel.IsReadingViewVisible);
            viewModel.NavigateToSelectedArticleFeedCommand.Execute(null);
            Assert.AreEqual($"feed:{feed.Id}", viewModel.ActiveRoute);
            Assert.IsFalse(viewModel.IsReadingViewVisible);
            Assert.IsNull(viewModel.SelectedArticle);
            Assert.AreEqual("tag-article", viewModel.VisibleArticles.Single().ArticleId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task InitializeLoadsPersistedArticlesAndProfileNavigation()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var feed = new CatalogFeed(
                "feed-1",
                "Gaming News",
                "https://example.com/feed.xml",
                "News",
                null,
                WebsiteUrl: "https://example.com");
            await catalogStore.AddFeedAsync(feed);
            await readerStore.AddFolderAsync(profile.Id, "Gaming");
            await readerStore.SubscribeAsync(profile.Id, feed.Id, "Gaming");
            await readerStore.AddFeedTagAsync(profile.Id, feed.Id, "Reviews");
            await readerStore.SaveArticlesAsync(feed.Id,
            [
                new FeedArticle("article-1", feed.Id, "item-1", "Handheld review", null, DateTimeOffset.UtcNow, "Portable hardware & accessories", "Full story")
                {
                    Categories = [new ArticleCategory("Press Releases")],
                    Author = "Jayme Lozano Carver"
                },
                new FeedArticle("article-2", feed.Id, "item-2", "Console update", null, DateTimeOffset.UtcNow.AddDays(-3), "System changes", "Update details")
                {
                    Categories = [new ArticleCategory("Announcements")]
                }
            ]);

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null,
                new ProfilePreferences(ShowRawFeedButton: true));
            await viewModel.InitializeAsync();

            Assert.AreEqual(1, viewModel.VisibleArticles.Count);
            var loadedArticle = viewModel.VisibleArticles.Single(article => article.ArticleId == "article-1");
            Assert.AreEqual("https://example.com", loadedArticle.WebsiteUrl);
            Assert.IsTrue(loadedArticle.IsWebsiteLinkVisible);
            Assert.IsFalse(viewModel.IsRawFeedButtonVisible);
            var pressReleaseTopic = viewModel.ArticleTopicOptions.Single(option => option.Term == "Press Releases");
            Assert.AreEqual(1, pressReleaseTopic.Count);
            viewModel.SelectedArticleTopic = pressReleaseTopic;
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            Assert.AreEqual("Press Releases", viewModel.VisibleArticles.Single().TopicSummary);
            viewModel.ClearArticleTopicCommand.Execute(null);
            Assert.AreEqual(1, viewModel.VisibleArticles.Count);

            viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Search"));
            viewModel.SearchQuery = "Press Releases";
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.SearchQuery = "release";
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.SearchQuery = "Jayme Lozano Carver";
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.SearchQuery = "LOZANO";
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.SearchQuery = "gaming news";
            Assert.AreEqual(2, viewModel.VisibleArticles.Count);
            viewModel.SearchQuery = "gaming";
            Assert.AreEqual(2, viewModel.VisibleArticles.Count);
            viewModel.SearchQuery = "PORTABLE";
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.SearchQuery = "Portable hardware & accessories";
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.SearchQuery = "hardware &";
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.SearchQuery = "full story";
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.SearchQuery = "full";
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.SearchQuery = string.Empty;
            Assert.AreEqual(0, viewModel.VisibleArticles.Count);
            viewModel.NavigateCommand.Execute(viewModel.TagLinks.Single(link => link.Route == "tag:Reviews"));
            viewModel.SearchQuery = "LOZANO";
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.SearchQuery = "release";
            viewModel.SelectedArticleTopic = viewModel.ArticleTopicOptions.Single(option => option.Term == "Press Releases");
            Assert.AreEqual("article-1", viewModel.VisibleArticles.Single().ArticleId);
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));
            viewModel.SearchQuery = string.Empty;
            viewModel.SelectedArticleTopic = viewModel.ArticleTopicOptions.Single(option => option.Term is null);
            Assert.AreEqual(2, viewModel.VisibleArticles.Count);
            viewModel.SelectArticleCommand.Execute(viewModel.VisibleArticles.Single(article => article.ArticleId == "article-1"));
            Assert.IsTrue(viewModel.IsRawFeedButtonVisible);
            Assert.AreEqual("Handheld review", viewModel.VisibleArticles.Single(article => article.ArticleId == "article-1").Title);
            Assert.AreEqual("Full story", viewModel.VisibleArticles.Single(article => article.ArticleId == "article-1").Content);
            Assert.IsTrue(viewModel.CatalogFeeds.Single().IsSubscribed);
            Assert.IsTrue(viewModel.FeedLinks.Any(link => link.Route == "folder:Gaming"));
            Assert.IsTrue(viewModel.FeedLinks.Any(link => link.Route == "feed:feed-1"));
            var allLink = viewModel.FeedLinks.Single(link => link.Route == "All");
            var folderLink = viewModel.FeedLinks.Single(link => link.Route == "folder:Gaming");
            var feedLink = viewModel.FeedLinks.Single(link => link.Route == "feed:feed-1");
            Assert.IsTrue(allLink.IndentMargin.Left < folderLink.IndentMargin.Left);
            Assert.IsTrue(folderLink.IndentMargin.Left < feedLink.IndentMargin.Left);
            Assert.AreEqual("(1)", folderLink.Count);
            Assert.IsFalse(folderLink.IsExpanded);
            Assert.AreSame(folderLink, feedLink.ParentFolder);
            viewModel.ActivateSidebarLinkCommand.Execute(folderLink);
            Assert.IsFalse(folderLink.IsExpanded);
            Assert.AreEqual("folder:Gaming", viewModel.ActiveRoute);
            viewModel.ToggleSidebarFolderCommand.Execute(folderLink);
            Assert.IsTrue(folderLink.IsExpanded);
            Assert.AreEqual("folder:Gaming", viewModel.ActiveRoute);
            viewModel.ToggleSidebarFolderCommand.Execute(folderLink);
            Assert.IsFalse(folderLink.IsExpanded);
            Assert.IsTrue(viewModel.TagLinks.Any(link => link.Route == "tag:Reviews"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task CatalogBrowserFiltersBySearchCategoryAndFollowedStateAndCanClearFilters()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-catalog-filter-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Catalog Reader");
            await profileStore.AddAsync(profile);
            var comics = new CatalogCategory("category-comics", "Comics");
            var science = new CatalogCategory("category-science", "Science");
            await catalogStore.AddCategoryAsync(comics);
            await catalogStore.AddCategoryAsync(science);
            var collection = new CatalogCollection("collection-editors-picks", "Editor's picks");
            await catalogStore.AddCollectionAsync(collection);

            var chickweed = new CatalogFeed(
                "feed-chickweed",
                "9 Chickweed Lane by Brooke McEldowney",
                "https://feeds.example.com/chickweed.xml",
                "Daily comic strip",
                comics.Id,
                "https://www.9chickweedlane.com");
            var dilbert = new CatalogFeed(
                "feed-dilbert",
                "Dilbert by Scott Adams",
                "https://feeds.example.com/dilbert.xml",
                "Office satire",
                comics.Id);
            var spaceNews = new CatalogFeed(
                "feed-space-news",
                "Space News",
                "https://science.example.com/feed.xml",
                "Daily astronomy updates",
                science.Id);
            await catalogStore.AddFeedAsync(chickweed);
            await catalogStore.AddFeedAsync(dilbert);
            await catalogStore.AddFeedAsync(spaceNews);
            await catalogStore.AddFeedToCollectionAsync(collection.Id, chickweed.Id);
            await catalogStore.AddFeedToCollectionAsync(collection.Id, spaceNews.Id);
            await readerStore.AddFolderAsync(profile.Id, "Comics");
            await readerStore.SubscribeAsync(profile.Id, dilbert.Id, "Comics");

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            var catalogFeedChangeCount = 0;
            viewModel.CatalogFeeds.CollectionChanged += (_, _) => catalogFeedChangeCount++;
            await viewModel.InitializeAsync();

            Assert.AreEqual(1, catalogFeedChangeCount);
            Assert.AreEqual(chickweed.Id, viewModel.SelectedCatalogFeed?.Id);
            CollectionAssert.AreEqual(
                new[] { chickweed.Name, dilbert.Name, spaceNews.Name },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Name).ToArray());
            Assert.AreEqual("Editor's picks (2)", viewModel.CatalogCollectionOptions.Single(option => option.CollectionId == collection.Id).Name);

            viewModel.CatalogSearchQuery = "  brooke   chickweed ";
            Assert.AreEqual("Search: brooke   chickweed", viewModel.ActiveCatalogFilterChips.Single().Label);
            viewModel.RemoveCatalogFilterCommand.Execute(viewModel.ActiveCatalogFilterChips.Single());
            Assert.AreEqual(string.Empty, viewModel.CatalogSearchQuery);
            viewModel.CatalogSearchQuery = "  brooke   chickweed ";
            CollectionAssert.AreEqual(
                new[] { chickweed.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());

            viewModel.CatalogSearchQuery = "9chickweedlane";
            CollectionAssert.AreEqual(
                new[] { chickweed.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());

            viewModel.CatalogSearchQuery = "office satire";
            CollectionAssert.AreEqual(
                new[] { dilbert.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());

            viewModel.CatalogSearchQuery = string.Empty;
            viewModel.SelectedCatalogCategory = viewModel.CatalogCategoryOptions.Single(option => option.CategoryId == comics.Id);
            Assert.AreEqual($"Category: {comics.Name}", viewModel.ActiveCatalogFilterChips.Single().Label);
            viewModel.RemoveCatalogFilterCommand.Execute(viewModel.ActiveCatalogFilterChips.Single());
            Assert.IsNull(viewModel.SelectedCatalogCategory?.CategoryId);
            viewModel.SelectedCatalogCategory = viewModel.CatalogCategoryOptions.Single(option => option.CategoryId == comics.Id);
            Assert.AreEqual(2, viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Count());
            Assert.AreEqual("Unfollow visible (1)", viewModel.UnfollowVisibleCatalogFeedsLabel);

            viewModel.SelectedCatalogCollection = viewModel.CatalogCollectionOptions.Single(option => option.CollectionId == collection.Id);
            Assert.AreEqual("Unfollow visible (0)", viewModel.UnfollowVisibleCatalogFeedsLabel);
            Assert.AreEqual(
                $"Collection: {collection.Name}",
                viewModel.ActiveCatalogFilterChips.Single(chip => chip.Kind == CatalogFilterKind.Collection).Label);
            Assert.AreEqual("Curated by Catalog Master", viewModel.SelectedCatalogCollectionCuratorLabel);
            CollectionAssert.AreEqual(
                new[] { chickweed.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());
            viewModel.SelectedCatalogCollection = viewModel.CatalogCollectionOptions.Single(option => option.CollectionId is null);
            Assert.AreEqual(string.Empty, viewModel.SelectedCatalogCollectionCuratorLabel);
            viewModel.SelectedCatalogCollection = viewModel.CatalogCollectionOptions.Single(option => option.CollectionId is null);

            viewModel.HideFollowedCatalogFeeds = true;
            Assert.IsTrue(viewModel.ActiveCatalogFilterChips.Any(chip => chip.Kind == CatalogFilterKind.HideFollowed));
            viewModel.RemoveCatalogFilterCommand.Execute(
                viewModel.ActiveCatalogFilterChips.Single(chip => chip.Kind == CatalogFilterKind.HideFollowed));
            Assert.IsFalse(viewModel.HideFollowedCatalogFeeds);
            viewModel.HideFollowedCatalogFeeds = true;
            CollectionAssert.AreEqual(
                new[] { chickweed.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());

            viewModel.CatalogSearchQuery = "no such feed";
            Assert.IsTrue(viewModel.IsCatalogResultsEmpty);
            Assert.AreEqual("0 of 3 feeds", viewModel.CatalogResultsSummary);
            Assert.AreEqual("No feeds match these filters.", viewModel.CatalogResultsEmptyMessage);

            viewModel.ClearCatalogFiltersCommand.Execute(null);
            Assert.IsFalse(viewModel.IsCatalogFilterActive);
            Assert.IsFalse(viewModel.IsCatalogResultsEmpty);
            Assert.AreEqual(3, viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Count());
            Assert.AreEqual("3 feeds", viewModel.CatalogResultsSummary);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task FolderSuggestionsPrioritizeTheSelectedCatalogCategory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-folder-suggestions-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Suggestion Reader");
            await profileStore.AddAsync(profile);
            var blockchain = new CatalogCategory("category-blockchain", "Blockchain");
            var comics = new CatalogCategory("category-comics", "Comics");
            await catalogStore.AddCategoryAsync(blockchain);
            await catalogStore.AddCategoryAsync(comics);
            await catalogStore.AddFeedAsync(new CatalogFeed(
                "feed-blockchain",
                "A Blockchain feed",
                "https://example.com/blockchain.xml",
                null,
                blockchain.Id));
            var comicsFeed = new CatalogFeed(
                "feed-comics",
                "Z Comics feed",
                "https://example.com/comics.xml",
                null,
                comics.Id);
            await catalogStore.AddFeedAsync(comicsFeed);

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();
            viewModel.SelectedCatalogCategory = viewModel.CatalogCategoryOptions
                .Single(option => option.CategoryId == comics.Id);
            IReadOnlyList<string>? receivedSuggestions = null;
            viewModel.FolderSelectionRequested = (_, suggestions, _) =>
            {
                receivedSuggestions = suggestions;
                return Task.FromResult<string?>(null);
            };

            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(
                viewModel.CatalogFeeds.Single(feed => feed.Id == comicsFeed.Id));

            Assert.IsNotNull(receivedSuggestions);
            Assert.AreEqual("Comics", receivedSuggestions[0]);
            Assert.AreEqual("Blockchain", receivedSuggestions[1]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task CatalogPreviewCachesFiveLightweightItemsUntilRefreshed()
    {
        var previewItems = Enumerable.Range(1, 7)
            .Select(index => new DownloadedFeedItem(
                $"item-{index}",
                $"Preview headline {index}",
                $"https://example.com/articles/{index}",
                DateTimeOffset.UtcNow.AddDays(-index),
                $"Summary {index}",
                "Long article content",
                $"https://example.com/images/{index}.jpg"))
            .ToArray();
        var downloader = new RecordingFeedDownloader { ItemsToReturn = previewItems };
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Preview Reader"),
            null,
            null,
            null,
            catalogFeedPreviewService: new CatalogFeedPreviewService(downloader));
        var feed = new CatalogFeedListItem(
            "preview-feed",
            "Preview feed",
            "https://example.com/feed.xml",
            "Feed description",
            "Technology",
            "category-tech");
        viewModel.CatalogFeeds.Add(feed);

        var preview = await viewModel.LoadCatalogFeedPreviewAsync(feed);

        Assert.AreEqual(5, preview.Items.Count);
        Assert.AreEqual("Preview headline 1", preview.Items[0].Title);
        Assert.IsTrue(preview.Items.All(item => item.Content is null));
        CollectionAssert.AreEqual(
            Enumerable.Range(1, 5).Select(index => $"https://example.com/images/{index}.jpg").ToArray(),
            preview.Items.Select(item => item.ImageUrl).ToArray());
        Assert.IsFalse(feed.IsSubscribed);
        Assert.AreEqual(1, downloader.RequestedFeedIds.Count);

        var cachedPreview = await viewModel.LoadCatalogFeedPreviewAsync(feed);
        Assert.AreSame(preview, cachedPreview);
        Assert.AreEqual(1, downloader.RequestedFeedIds.Count);

        var refreshedPreview = await viewModel.LoadCatalogFeedPreviewAsync(feed, forceRefresh: true);
        Assert.AreEqual(2, downloader.RequestedFeedIds.Count);
        Assert.AreNotSame(preview, refreshedPreview);

        await viewModel.LoadCatalogFeedPreviewAsync(feed);
        Assert.AreEqual(2, downloader.RequestedFeedIds.Count);

        downloader.DownloadHandler = _ => Task.FromException<IReadOnlyList<DownloadedFeedItem>>(
            new System.Net.Http.HttpRequestException("The feed is offline."));
        var failure = await Assert.ThrowsExceptionAsync<System.Net.Http.HttpRequestException>(
            () => viewModel.LoadCatalogFeedPreviewAsync(feed, forceRefresh: true));
        StringAssert.Contains(failure.Message, "The feed is offline.");

        downloader.DownloadHandler = null;
        var recoveredPreview = await viewModel.LoadCatalogFeedPreviewAsync(feed, forceRefresh: true);
        Assert.IsNotNull(recoveredPreview);
    }

    [TestMethod]
    public async Task CatalogPreviewCanLoadRawFeedXmlOnDemand()
    {
        const string rawXml = "<rss><channel><title>Malformed\u001fFeed</title></channel></rss>";
        var downloader = new RecordingFeedDownloader { RawFeedXmlToReturn = rawXml };
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Preview Reader"),
            null,
            null,
            null,
            catalogFeedPreviewService: new CatalogFeedPreviewService(downloader));
        var feed = new CatalogFeedListItem(
            "raw-preview-feed",
            "Raw preview",
            "https://example.com/feed.xml",
            null,
            null);

        var result = await viewModel.LoadCatalogFeedRawXmlAsync(feed);

        Assert.AreEqual(rawXml, result);
        CollectionAssert.AreEqual(new[] { feed.Id }, downloader.RequestedRawXmlFeedIds.ToArray());
    }

    [TestMethod]
    public async Task CatalogPreviewCancelsWithDialogToken()
    {
        var downloadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedToken = CancellationToken.None;
        var downloader = new RecordingFeedDownloader
        {
            DownloadHandler = async cancellationToken =>
            {
                observedToken = cancellationToken;
                downloadStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Array.Empty<DownloadedFeedItem>();
            }
        };
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Preview Reader"),
            null,
            null,
            null,
            catalogFeedPreviewService: new CatalogFeedPreviewService(downloader));
        var feed = new CatalogFeedListItem(
            "cancel-feed",
            "Cancel feed",
            "https://example.com/cancel.xml",
            null,
            null);
        viewModel.CatalogFeeds.Add(feed);

        using var cancellation = new CancellationTokenSource();
        var previewTask = viewModel.LoadCatalogFeedPreviewAsync(feed, cancellation.Token);
        await downloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => previewTask);

        Assert.IsTrue(observedToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task CatalogBrowsePreviewFollowAndReturnWorksEndToEnd()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-catalog-workflow-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Discovery Workflow Reader");
            await profileStore.AddAsync(profile);
            var category = new CatalogCategory("category-comics", "Comics");
            var feed = new CatalogFeed(
                "feed-indie-comic",
                "Indie Webcomic",
                "https://example.com/comic.xml",
                "An independent comic",
                category.Id,
                "https://example.com");
            await catalogStore.AddCategoryAsync(category);
            await catalogStore.AddFeedAsync(feed);
            await readerStore.AddFolderAsync(profile.Id, "Comics");

            var downloader = new RecordingFeedDownloader();
            var catalogService = new CatalogService(catalogStore);
            var viewModel = new MainWindowViewModel(
                profile,
                catalogService,
                new ReadingService(readerStore, catalogStore),
                new FeedRefreshService(readerStore, catalogStore, downloader),
                new ProfilePreferences(RefreshFeedsWhenOpened: false),
                new CatalogFeedPreviewService(downloader));
            await viewModel.InitializeAsync();
            viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));
            viewModel.CatalogSearchQuery = "indie comics";

            var catalogItem = viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Single();
            var preview = await viewModel.LoadCatalogFeedPreviewAsync(catalogItem);
            Assert.AreEqual("New headline", preview.Items.Single().Title);
            Assert.AreEqual(1, downloader.RequestedFeedIds.Count);

            viewModel.FolderSelectionRequested = (_, _, _) => Task.FromResult<string?>("Comics");
            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(catalogItem);
            Assert.IsTrue(catalogItem.IsSubscribed);
            Assert.AreEqual("Comics", (await readerStore.GetSubscriptionsAsync(profile.Id)).Single().FolderName);
            Assert.AreEqual("indie comics", viewModel.CatalogSearchQuery);

            viewModel.HideFollowedCatalogFeeds = true;
            Assert.IsTrue(viewModel.CatalogFeedListView.IsEmpty);
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));
            viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));
            viewModel.HideFollowedCatalogFeeds = false;

            Assert.AreEqual("indie comics", viewModel.CatalogSearchQuery);
            Assert.AreEqual(feed.Id, viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Single().Id);
            Assert.IsTrue(viewModel.CatalogFeeds.Single().IsSubscribed);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [DataTestMethod]
    [DataRow(575)]
    [DataRow(5000)]
    [DataRow(25000)]
    public void CatalogBrowserFiltersGeneratedLargeCatalog(int catalogSize)
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Large Catalog Reader"));
        var scienceCount = (catalogSize + 1) / 2;
        var scienceCategory = new CatalogCategoryOption("category-science", $"Science ({scienceCount:N0})");
        viewModel.CatalogCategoryOptions.Add(scienceCategory);
        viewModel.CatalogCategoryOptions.Add(new CatalogCategoryOption("category-comics", $"Comics ({catalogSize / 2:N0})"));

        var populationTimer = Stopwatch.StartNew();
        for (var feedIndex = 0; feedIndex < catalogSize; feedIndex++)
        {
            var isScienceFeed = feedIndex % 2 == 0;
            var categoryName = isScienceFeed ? "Science" : "Comics";
            var categoryId = isScienceFeed ? "category-science" : "category-comics";
            viewModel.CatalogFeeds.Add(new CatalogFeedListItem(
                $"feed-{feedIndex:D4}",
                $"Feed {feedIndex:D4}",
                $"https://feeds.example.com/{feedIndex:D4}.xml",
                isScienceFeed ? "Research and astronomy" : "Daily strip",
                categoryName,
                categoryId));
        }
        populationTimer.Stop();

        var matchingFeedIndex = catalogSize - 1;
        if (matchingFeedIndex % 2 != 0)
        {
            matchingFeedIndex--;
        }

        var filterTimer = Stopwatch.StartNew();
        viewModel.SelectedCatalogCategory = scienceCategory;
        viewModel.CatalogSearchQuery = $"science {matchingFeedIndex:D4}";
        var visibleFeedIds = viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray();
        filterTimer.Stop();

        CollectionAssert.AreEqual(
            new[] { $"feed-{matchingFeedIndex:D4}" },
            visibleFeedIds);
        Assert.AreEqual($"1 of {catalogSize} feeds", viewModel.CatalogResultsSummary);
        TestContext.WriteLine(
            $"Catalog size {catalogSize:N0}: populate {populationTimer.Elapsed.TotalMilliseconds:F1} ms; filter {filterTimer.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [TestMethod]
    public async Task CatalogBrowserShowsRecoverableLoadFailureAndRetries()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-catalog-load-{Guid.NewGuid():N}.db");
        await File.WriteAllTextAsync(databasePath, "invalid database content");
        try
        {
            var catalogStore = new SqliteCatalogStore(databasePath);
            var viewModel = new MainWindowViewModel(
                Profile.CreateRegular("Catalog Recovery Reader"),
                new CatalogService(catalogStore));

            await viewModel.InitializeAsync();

            Assert.IsFalse(viewModel.IsCatalogLoading);
            Assert.IsFalse(viewModel.IsCatalogLoaded);
            Assert.IsTrue(viewModel.HasCatalogLoadError);
            Assert.IsFalse(viewModel.IsCatalogResultsEmpty);
            StringAssert.StartsWith(viewModel.CatalogLoadErrorMessage, "Could not load the feed catalog:");

            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            await catalogStore.InitializeAsync();
            await catalogStore.AddFeedAsync(new CatalogFeed(
                "recovered-feed",
                "Recovered feed",
                "https://example.com/recovered.xml",
                null,
                null));

            await viewModel.RetryCatalogLoadCommand.ExecuteAsync();

            Assert.IsFalse(viewModel.IsCatalogLoading);
            Assert.IsTrue(viewModel.IsCatalogLoaded);
            Assert.IsFalse(viewModel.HasCatalogLoadError);
            Assert.AreEqual("recovered-feed", viewModel.CatalogFeeds.Single().Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task BatchFollowUsesOneFolderChoiceAndKeepsSelectionWhenCancelled()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-catalog-batch-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Batch Reader");
            await profileStore.AddAsync(profile);
            var firstFeed = new CatalogFeed("feed-first", "First comic", "https://example.com/first.xml", null, null);
            var secondFeed = new CatalogFeed("feed-second", "Second comic", "https://example.com/second.xml", null, null);
            await catalogStore.AddFeedAsync(firstFeed);
            await catalogStore.AddFeedAsync(secondFeed);
            await readerStore.AddFolderAsync(profile.Id, "Comics");

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();
            var selectedFeeds = viewModel.CatalogFeeds.ToArray();

            viewModel.CatalogSearchQuery = "First comic";
            Assert.AreEqual(false, viewModel.VisibleCatalogFeedSelectionState);
            selectedFeeds.Single(feed => feed.Name == "First comic").IsSelectedForFollow = true;
            Assert.AreEqual(true, viewModel.VisibleCatalogFeedSelectionState);
            viewModel.CatalogSearchQuery = string.Empty;
            Assert.IsNull(viewModel.VisibleCatalogFeedSelectionState);
            viewModel.ToggleVisibleCatalogFeedSelectionCommand.Execute(null);
            Assert.AreEqual(true, viewModel.VisibleCatalogFeedSelectionState);
            viewModel.ToggleVisibleCatalogFeedSelectionCommand.Execute(null);
            Assert.AreEqual(false, viewModel.VisibleCatalogFeedSelectionState);
            Assert.AreEqual(0, viewModel.SelectedCatalogFeedCount);

            selectedFeeds[0].IsSelectedForFollow = true;
            selectedFeeds[1].IsSelectedForFollow = true;
            Assert.AreEqual(2, viewModel.SelectedCatalogFeedCount);
            Assert.IsTrue(viewModel.FollowSelectedCatalogFeedsCommand.CanExecute(null));
            viewModel.CatalogSearchQuery = "no matching feeds";
            Assert.AreEqual("0 of 2 feeds; 2 selected outside these results", viewModel.CatalogResultsSummary);
            Assert.AreEqual("Follow selected (2)", viewModel.FollowSelectedCatalogFeedsLabel);
            viewModel.CatalogSearchQuery = string.Empty;

            var pickerCallCount = 0;
            viewModel.FolderSelectionRequested = (_, _, _) =>
            {
                pickerCallCount++;
                return Task.FromResult<string?>(null);
            };
            await viewModel.FollowSelectedCatalogFeedsCommand.ExecuteAsync();

            Assert.AreEqual(1, pickerCallCount);
            Assert.AreEqual(0, (await readerStore.GetSubscriptionsAsync(profile.Id)).Count);
            Assert.AreEqual(2, viewModel.SelectedCatalogFeedCount);

            viewModel.FolderSelectionRequested = (_, _, _) =>
            {
                pickerCallCount++;
                return Task.FromResult<string?>("Comics");
            };
            await viewModel.FollowSelectedCatalogFeedsCommand.ExecuteAsync();

            Assert.AreEqual(2, pickerCallCount);
            var subscriptions = await readerStore.GetSubscriptionsAsync(profile.Id);
            Assert.AreEqual(2, subscriptions.Count);
            Assert.IsTrue(subscriptions.All(subscription => subscription.FolderName == "Comics"));
            Assert.AreEqual(0, viewModel.SelectedCatalogFeedCount);
            Assert.AreEqual("Following 2 feeds in Comics.", viewModel.StatusMessage);
            Assert.IsTrue(viewModel.CanUnfollowAllCatalogFeeds);

            viewModel.CatalogSearchQuery = "First comic";
            Assert.AreEqual(1, viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Count(feed => feed.IsSubscribed));
            var confirmedFeedCount = 0;
            viewModel.ConfirmUnfollowAllRequested = feedCount =>
            {
                confirmedFeedCount = feedCount;
                return Task.FromResult(false);
            };
            await viewModel.UnfollowAllCatalogFeedsCommand.ExecuteAsync();
            Assert.AreEqual(1, confirmedFeedCount);
            Assert.AreEqual(2, (await readerStore.GetSubscriptionsAsync(profile.Id)).Count);

            viewModel.ConfirmUnfollowAllRequested = feedCount =>
            {
                confirmedFeedCount = feedCount;
                return Task.FromResult(true);
            };
            await viewModel.UnfollowAllCatalogFeedsCommand.ExecuteAsync();
            Assert.AreEqual(1, confirmedFeedCount);
            var remainingSubscriptions = await readerStore.GetSubscriptionsAsync(profile.Id);
            Assert.AreEqual(secondFeed.Id, remainingSubscriptions.Single().FeedId);
            Assert.IsFalse(viewModel.CanUnfollowAllCatalogFeeds);
            Assert.AreEqual("Unfollowed 1 feed.", viewModel.StatusMessage);
            viewModel.CatalogSearchQuery = string.Empty;
            Assert.IsTrue(viewModel.CanUnfollowAllCatalogFeeds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task FolderPickerCreationReusesExistingFolderAndRefreshesFolderNames()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-folder-picker-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Folder Picker Reader");
            await profileStore.AddAsync(profile);
            var feed = new CatalogFeed("feed-advertising", "Advertising feed", "https://example.com/ads.xml", null, null);
            var newFeed = new CatalogFeed("feed-new", "New feed", "https://example.com/new.xml", null, null);
            await catalogStore.AddFeedAsync(feed);
            await catalogStore.AddFeedAsync(newFeed);

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();

            await readerStore.AddFolderAsync(profile.Id, "Advertising");
            viewModel.FolderSelectionRequested = async (_, _, createFolderAsync) =>
            {
                await createFolderAsync("Advertising");
                Assert.IsTrue(viewModel.FeedLinks.Any(link => link.Route == "folder:Advertising"));
                return "Advertising";
            };

            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(
                viewModel.CatalogFeeds.Single(item => item.Id == feed.Id));

            Assert.AreEqual("Advertising", viewModel.FolderNames.Single());
            var subscription = (await readerStore.GetSubscriptionsAsync(profile.Id)).Single();
            Assert.AreEqual("Advertising", subscription.FolderName);
            Assert.AreEqual("Following Advertising feed.", viewModel.StatusMessage);

            viewModel.FolderSelectionRequested = async (_, _, createFolderAsync) =>
            {
                await createFolderAsync("Comics");
                Assert.IsTrue(viewModel.FeedLinks.Any(link => link.Route == "folder:Comics"));
                return "Comics";
            };
            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(
                viewModel.CatalogFeeds.Single(item => item.Id == newFeed.Id));

            CollectionAssert.AreEquivalent(new[] { "Advertising", "Comics" }, viewModel.FolderNames.ToArray());
            var subscriptions = await readerStore.GetSubscriptionsAsync(profile.Id);
            Assert.AreEqual(2, subscriptions.Count);
            Assert.IsTrue(subscriptions.Any(item => item.FeedId == newFeed.Id && item.FolderName == "Comics"));
            Assert.AreEqual("Following New feed.", viewModel.StatusMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task DeletingFolderUnfollowsOnlyItsFeedsAndReturnsToAllView()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-delete-folder-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Folder Delete Reader");
            await profileStore.AddAsync(profile);
            var comicsFeed = new CatalogFeed("feed-comics", "Comics feed", "https://example.com/comics.xml", null, null);
            var newsFeed = new CatalogFeed("feed-news", "News feed", "https://example.com/news.xml", null, null);
            await catalogStore.AddFeedAsync(comicsFeed);
            await catalogStore.AddFeedAsync(newsFeed);
            await readerStore.AddFolderAsync(profile.Id, "Comics");
            await readerStore.AddFolderAsync(profile.Id, "News");
            await readerStore.SubscribeAsync(profile.Id, comicsFeed.Id, "Comics");
            await readerStore.SubscribeAsync(profile.Id, newsFeed.Id, "News");

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();
            var comicsFolder = viewModel.FeedLinks.Single(link => link.Route == "folder:Comics");
            viewModel.NavigateCommand.Execute(comicsFolder);

            string? confirmedFolder = null;
            var confirmedFeedCount = 0;
            var expectedLocation = new System.Windows.Point(302, 188);
            System.Windows.Point? confirmedLocation = null;
            viewModel.FolderDeleteDialogLocationProvider = () => expectedLocation;
            viewModel.ConfirmDeleteFolderRequested = (folderName, feedCount, location) =>
            {
                confirmedFolder = folderName;
                confirmedFeedCount = feedCount;
                confirmedLocation = location;
                return Task.FromResult(false);
            };
            await viewModel.DeleteFolderCommand.ExecuteAsync(comicsFolder);
            Assert.AreEqual(2, (await readerStore.GetSubscriptionsAsync(profile.Id)).Count);
            Assert.IsTrue(viewModel.FolderNames.Contains("Comics", StringComparer.OrdinalIgnoreCase));

            viewModel.ConfirmDeleteFolderRequested = (folderName, feedCount, location) =>
            {
                confirmedFolder = folderName;
                confirmedFeedCount = feedCount;
                confirmedLocation = location;
                return Task.FromResult(true);
            };
            await viewModel.DeleteFolderCommand.ExecuteAsync(comicsFolder);

            Assert.AreEqual("Comics", confirmedFolder);
            Assert.AreEqual(1, confirmedFeedCount);
            Assert.AreEqual(expectedLocation, confirmedLocation);
            var remainingSubscriptions = await readerStore.GetSubscriptionsAsync(profile.Id);
            Assert.AreEqual(newsFeed.Id, remainingSubscriptions.Single().FeedId);
            CollectionAssert.AreEqual(new[] { "News" }, viewModel.FolderNames.ToArray());
            Assert.IsFalse(viewModel.FeedLinks.Any(link => link.Route == "folder:Comics"));
            Assert.IsFalse(viewModel.FeedLinks.Any(link => link.Route == $"feed:{comicsFeed.Id}"));
            Assert.AreEqual("All", viewModel.ActiveRoute);
            Assert.AreEqual("Deleted folder Comics and unfollowed 1 feed.", viewModel.StatusMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task BatchFollowKeepsFailedFeedSelectedWhenCatalogChanges()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-catalog-batch-partial-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Batch Reader");
            await profileStore.AddAsync(profile);
            var availableFeed = new CatalogFeed("feed-available", "Available source", "https://example.com/available.xml", null, null);
            var removedFeed = new CatalogFeed("feed-removed", "Removed source", "https://example.com/removed.xml", null, null);
            await catalogStore.AddFeedAsync(availableFeed);
            await catalogStore.AddFeedAsync(removedFeed);
            await readerStore.AddFolderAsync(profile.Id, "News");

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();
            var availableItem = viewModel.CatalogFeeds.Single(feed => feed.Id == availableFeed.Id);
            var removedItem = viewModel.CatalogFeeds.Single(feed => feed.Id == removedFeed.Id);
            availableItem.IsSelectedForFollow = true;
            removedItem.IsSelectedForFollow = true;
            await catalogStore.DeleteFeedAsync(removedFeed.Id);
            viewModel.FolderSelectionRequested = (_, _, _) => Task.FromResult<string?>("News");

            await viewModel.FollowSelectedCatalogFeedsCommand.ExecuteAsync();

            var subscriptions = await readerStore.GetSubscriptionsAsync(profile.Id);
            Assert.AreEqual(1, subscriptions.Count);
            Assert.AreEqual(availableFeed.Id, subscriptions.Single().FeedId);
            Assert.IsFalse(availableItem.IsSelectedForFollow);
            Assert.IsTrue(availableItem.IsSubscribed);
            Assert.IsTrue(removedItem.IsSelectedForFollow);
            Assert.AreEqual(1, viewModel.SelectedCatalogFeedCount);
            Assert.AreEqual("Followed 1 feeds; 1 failed: Removed source.", viewModel.StatusMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task StartupRefreshChecksSubscriptionsWhileKeepingTodayAsStartPage()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-startup-refresh-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Startup Reader");
            await profileStore.AddAsync(profile);
            var firstFeed = new CatalogFeed("feed-first", "First", "https://example.com/first.xml", null, null);
            var secondFeed = new CatalogFeed("feed-second", "Second", "https://example.com/second.xml", null, null);
            await catalogStore.AddFeedAsync(firstFeed);
            await catalogStore.AddFeedAsync(secondFeed);
            await readerStore.AddFolderAsync(profile.Id, "News");
            await readerStore.SubscribeAsync(profile.Id, firstFeed.Id, "News");
            await readerStore.SubscribeAsync(profile.Id, secondFeed.Id, "News");

            var downloader = new RecordingFeedDownloader();
            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                new FeedRefreshService(readerStore, catalogStore, downloader),
                new ProfilePreferences(StartPage: ProfileStartPage.Today, RefreshFeedsWhenOpened: false));
            await viewModel.InitializeAsync();

            Assert.AreEqual("Today", viewModel.ActiveRoute);
            await viewModel.RefreshNowAsync();

            CollectionAssert.AreEquivalent(
                new[] { firstFeed.Id, secondFeed.Id },
                downloader.RequestedFeedIds.ToArray());
            Assert.AreEqual("Today", viewModel.ActiveRoute);
            Assert.IsFalse(viewModel.IsRefreshing);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task FirstOpenRefreshesNewFeedAndOpenPreferenceRefreshesFolderFeeds()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var existingFeed = new CatalogFeed("feed-existing", "Existing", "https://example.com/existing.xml", null, null);
            var newFeed = new CatalogFeed("feed-new", "New", "https://example.com/new.xml", null, null);
            await catalogStore.AddFeedAsync(existingFeed);
            await catalogStore.AddFeedAsync(newFeed);
            await readerStore.AddFolderAsync(profile.Id, "News");
            await readerStore.SubscribeAsync(profile.Id, existingFeed.Id, "News");
            await readerStore.SaveArticlesAsync(existingFeed.Id,
            [
                new FeedArticle("existing-article", existingFeed.Id, "existing-item", "Existing article", null, DateTimeOffset.UtcNow, null, null)
            ]);

            var downloader = new RecordingFeedDownloader();
            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                new FeedRefreshService(readerStore, catalogStore, downloader),
                new ProfilePreferences(RefreshFeedsWhenOpened: false));
            await viewModel.InitializeAsync();
            viewModel.FolderSelectionRequested = (_, _, _) => Task.FromResult<string?>("News");
            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(
                viewModel.CatalogFeeds.Single(feed => feed.Id == newFeed.Id));

            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == $"feed:{newFeed.Id}"));
            await WaitForRefreshCompletionAsync(viewModel);

            CollectionAssert.AreEqual(new[] { newFeed.Id }, downloader.RequestedFeedIds.ToArray());
            Assert.AreEqual(1, viewModel.VisibleArticles.Count);
            Assert.AreEqual("New headline", viewModel.VisibleArticles[0].Title);

            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == $"feed:{newFeed.Id}"));
            await WaitForRefreshCompletionAsync(viewModel);
            Assert.AreEqual(1, downloader.RequestedFeedIds.Count);

            viewModel.ApplyPreferences(new ProfilePreferences(RefreshFeedsWhenOpened: true, ShowRawFeedButton: true));
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "folder:News"));
            await WaitForRefreshCompletionAsync(viewModel);

            Assert.AreEqual(3, downloader.RequestedFeedIds.Count);
            CollectionAssert.AreEquivalent(
                new[] { existingFeed.Id, newFeed.Id },
                downloader.RequestedFeedIds.Skip(1).ToArray());

            var newFeedArticle = viewModel.VisibleArticles.Single(article => article.FeedId == newFeed.Id);
            viewModel.SelectArticleCommand.Execute(newFeedArticle);
            Assert.IsTrue(viewModel.IsRawFeedButtonVisible);
            Assert.AreEqual(
                $"<item><guid>{newFeed.Id}-item</guid><title>New headline</title></item>",
                (await viewModel.GetRawArticleContentAsync(newFeedArticle)).Xml);

            var visibleArticleChanges = 0;
            viewModel.VisibleArticles.CollectionChanged += (_, _) => visibleArticleChanges++;
            downloader.ItemsByFeed = new Dictionary<string, IReadOnlyList<DownloadedFeedItem>>
            {
                [existingFeed.Id] =
                [new DownloadedFeedItem($"{existingFeed.Id}-item", "Existing headline", null, null, "Cached summary", "<p>Cached article body</p>")],
                [newFeed.Id] =
                [new DownloadedFeedItem(
                    $"{newFeed.Id}-item",
                    "Updated headline",
                    null,
                    null,
                    "Updated summary",
                    "<p>Updated article body <img src=\"https://example.com/updated.jpg\" /></p>")]
            };
            await viewModel.RefreshNowAsync();

            Assert.AreSame(newFeedArticle, viewModel.SelectedArticle);
            Assert.AreEqual("New headline", viewModel.SelectedArticle?.Title);
            Assert.AreEqual(0, visibleArticleChanges);
            Assert.AreEqual("Checked 2 feeds; fetched 2 articles; New 0.", viewModel.StatusMessage);

            downloader.ItemsByFeed = new Dictionary<string, IReadOnlyList<DownloadedFeedItem>>
            {
                [existingFeed.Id] =
                [new DownloadedFeedItem($"{existingFeed.Id}-item", "Existing headline", null, null, "Cached summary", "<p>Cached article body</p>")],
                [newFeed.Id] =
                [
                    new DownloadedFeedItem(
                        $"{newFeed.Id}-item",
                        "Updated headline",
                        null,
                        null,
                        "Updated summary",
                        "<p>Updated article body <img src=\"https://example.com/updated.jpg\" /></p>"),
                    new DownloadedFeedItem(
                        $"{newFeed.Id}-new-item",
                        "New article",
                        null,
                        DateTimeOffset.UtcNow,
                        "New summary",
                        "New content")
                ]
            };
            await viewModel.RefreshNowAsync();

            Assert.AreNotSame(newFeedArticle, viewModel.SelectedArticle);
            Assert.AreEqual("Updated headline", viewModel.SelectedArticle?.Title);
            Assert.IsTrue(visibleArticleChanges > 0);
            Assert.AreEqual("Checked 2 feeds; fetched 3 articles; New 1.", viewModel.StatusMessage);
            Assert.AreEqual(
                "<p>Updated article body <img src=\"https://example.com/updated.jpg\" /></p>",
                viewModel.SelectedArticle?.Content);
            StringAssert.Contains(viewModel.SelectedArticleRefreshStatusMessage!, "Last successful refresh:");

            var cachedExistingArticle = (await readerStore.GetArticlesAsync(profile.Id))
                .Single(article => article.Article.FeedId == existingFeed.Id && article.Article.Title == "Existing headline")
                .Article with
                {
                    Title = "Cached before failed refresh",
                    Summary = "Cached summary",
                    Content = "<p>Cached article body</p>",
                    ImageUrl = "https://example.com/cached-image.jpg",
                    Categories = [new ArticleCategory("Science", "https://example.com/topics")],
                    Author = "Example Author"
                };
            await readerStore.SaveArticlesAsync(existingFeed.Id, [cachedExistingArticle]);
            await readerStore.SetArticleReadAsync(profile.Id, cachedExistingArticle.Id, true);
            await readerStore.SetArticleSavedAsync(profile.Id, cachedExistingArticle.Id, true);

            downloader.FailingFeedIds.Add(existingFeed.Id);
            await viewModel.RefreshNowAsync();

            Assert.IsTrue(viewModel.HasRefreshFailure);
            StringAssert.Contains(viewModel.RefreshFailureMessage!, "Existing: The feed is unavailable.");
            Assert.IsFalse(viewModel.RefreshFailureMessage!.Contains("New:", StringComparison.Ordinal));
            Assert.AreEqual(1, viewModel.FeedWarningCount);
            Assert.AreEqual(existingFeed.Id, viewModel.SelectedFeedWarning?.FeedId);
            StringAssert.Contains(viewModel.SelectedFeedWarning!.Message, "Existing: The feed is unavailable.");
            Assert.AreEqual("Updated headline", viewModel.SelectedArticle?.Title);
            Assert.IsFalse(viewModel.HasVisibleRefreshFailure);
            Assert.IsNull(viewModel.VisibleRefreshFailureMessage);
            Assert.IsFalse(viewModel.HasVisibleFailedRefreshFeeds);
            Assert.IsFalse(viewModel.RetryFailedFeedsCommand.CanExecute(null));
            var persistedExistingArticle = (await readerStore.GetArticlesAsync(profile.Id))
                .Single(article => article.Article.Id == cachedExistingArticle.Id);
            Assert.AreEqual("Cached before failed refresh", persistedExistingArticle.Article.Title);
            Assert.AreEqual("Cached summary", persistedExistingArticle.Article.Summary);
            Assert.AreEqual("<p>Cached article body</p>", persistedExistingArticle.Article.Content);
            Assert.AreEqual("https://example.com/cached-image.jpg", persistedExistingArticle.Article.ImageUrl);
            Assert.AreEqual("Science", persistedExistingArticle.Article.Categories.Single().Term);
            Assert.AreEqual("Example Author", persistedExistingArticle.Article.Author);
            Assert.IsTrue(persistedExistingArticle.IsRead);
            Assert.IsTrue(persistedExistingArticle.IsSaved);
            var failedFeedArticle = viewModel.VisibleArticles.Single(article => article.ArticleId == cachedExistingArticle.Id);
            viewModel.SelectArticleCommand.Execute(failedFeedArticle);
            Assert.IsTrue(viewModel.HasVisibleRefreshFailure);
            StringAssert.Contains(viewModel.VisibleRefreshFailureMessage!, "Existing: The feed is unavailable.");
            Assert.IsTrue(viewModel.HasVisibleFailedRefreshFeeds);
            Assert.IsTrue(viewModel.RetryFailedFeedsCommand.CanExecute(null));
            Assert.AreEqual("<p>Cached article body</p>", viewModel.SelectedArticle?.Content);
            StringAssert.Contains(viewModel.SelectedArticleRefreshStatusMessage!, "Last successful refresh:");
            Assert.IsFalse(viewModel.SelectedArticleRefreshStatusMessage!.Contains(
                "Existing: The feed is unavailable.",
                StringComparison.Ordinal));

            viewModel.ApplyPreferences(new ProfilePreferences(RefreshFeedsWhenOpened: false, ShowRawFeedButton: true));
            viewModel.BackCommand.Execute(null);
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == $"feed:{newFeed.Id}"));
            await WaitForRefreshCompletionAsync(viewModel);
            Assert.IsTrue(viewModel.VisibleArticles.Count > 0);
            Assert.IsTrue(viewModel.VisibleArticles.All(article => article.FeedId == newFeed.Id));
            Assert.IsFalse(viewModel.HasVisibleRefreshFailure);
            Assert.IsFalse(viewModel.HasVisibleFailedRefreshFeeds);
            Assert.IsFalse(viewModel.RetryFailedFeedsCommand.CanExecute(null));
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == $"feed:{existingFeed.Id}"));
            await WaitForRefreshCompletionAsync(viewModel);
            Assert.IsTrue(viewModel.VisibleArticles.Count > 0);
            Assert.IsTrue(viewModel.VisibleArticles.All(article => article.FeedId == existingFeed.Id));
            Assert.IsTrue(viewModel.HasVisibleRefreshFailure);
            Assert.IsTrue(viewModel.HasVisibleFailedRefreshFeeds);
            Assert.IsTrue(viewModel.RetryFailedFeedsCommand.CanExecute(null));

            var reopenedViewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                new FeedRefreshService(readerStore, catalogStore, downloader),
                new ProfilePreferences(RefreshFeedsWhenOpened: false));
            await reopenedViewModel.InitializeAsync();

            Assert.IsTrue(reopenedViewModel.HasFailedRefreshFeeds);
            Assert.IsTrue(reopenedViewModel.RetryFailedFeedsCommand.CanExecute(null));
            StringAssert.Contains(reopenedViewModel.RefreshFailureMessage!, "Existing: The feed is unavailable.");
            Assert.AreEqual(1, reopenedViewModel.FeedWarningCount);
            Assert.AreEqual(existingFeed.Id, reopenedViewModel.SelectedFeedWarning?.FeedId);
            var reopenedFailedArticle = reopenedViewModel.VisibleArticles.Single(article => article.ArticleId == cachedExistingArticle.Id);
            reopenedViewModel.SelectArticleCommand.Execute(reopenedFailedArticle);
            Assert.AreEqual("<p>Cached article body</p>", reopenedViewModel.SelectedArticle?.Content);
            StringAssert.Contains(reopenedViewModel.SelectedArticleRefreshStatusMessage!, "Last successful refresh:");
            Assert.IsFalse(reopenedViewModel.SelectedArticleRefreshStatusMessage!.Contains(
                "Existing: The feed is unavailable.",
                StringComparison.Ordinal));

            var requestedFeedCount = downloader.RequestedFeedIds.Count;
            downloader.FailingFeedIds.Clear();
            viewModel.RetryFailedFeedsCommand.Execute(null);
            await WaitForRefreshCompletionAsync(viewModel);

            CollectionAssert.AreEqual(
                new[] { existingFeed.Id },
                downloader.RequestedFeedIds.Skip(requestedFeedCount).ToArray());
            Assert.IsFalse(viewModel.HasRefreshFailure);
            Assert.IsFalse(viewModel.HasFailedRefreshFeeds);
            Assert.IsNull(viewModel.VisibleRefreshFailureMessage);
            Assert.IsFalse(viewModel.HasVisibleFailedRefreshFeeds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task FeedWarningsCanBeNavigatedAndDismissedPersistently()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-feed-warnings-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var alphaFeed = new CatalogFeed("feed-alpha", "Alpha", "https://example.com/alpha.xml", null, null);
            var betaFeed = new CatalogFeed("feed-beta", "Beta", "https://example.com/beta.xml", null, null);
            await catalogStore.AddFeedAsync(alphaFeed);
            await catalogStore.AddFeedAsync(betaFeed);
            await readerStore.AddFolderAsync(profile.Id, "Warnings");
            await readerStore.SubscribeAsync(profile.Id, alphaFeed.Id, "Warnings");
            await readerStore.SubscribeAsync(profile.Id, betaFeed.Id, "Warnings");
            var attemptedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            foreach (var feed in new[] { alphaFeed, betaFeed })
            {
                await readerStore.RecordFeedRefreshAttemptAsync(profile.Id, feed.Id, attemptedAt);
                await readerStore.RecordFeedRefreshResultAsync(
                    profile.Id,
                    feed.Id,
                    successfulAt: null,
                    $"{feed.Name}: the server could not be reached.");
            }

            var readingService = new ReadingService(readerStore, catalogStore);
            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                readingService,
                new FeedRefreshService(readerStore, catalogStore, new RecordingFeedDownloader()),
                new ProfilePreferences(RefreshFeedsWhenOpened: false));
            await viewModel.InitializeAsync();

            Assert.AreEqual(2, viewModel.FeedWarningCount);
            Assert.AreEqual(alphaFeed.Id, viewModel.SelectedFeedWarning?.FeedId);
            Assert.AreEqual("1 of 2", viewModel.FeedWarningPositionLabel);
            viewModel.NextFeedWarningCommand.Execute(null);
            Assert.AreEqual(betaFeed.Id, viewModel.SelectedFeedWarning?.FeedId);
            Assert.AreEqual("2 of 2", viewModel.FeedWarningPositionLabel);
            viewModel.PreviousFeedWarningCommand.Execute(null);
            Assert.AreEqual(alphaFeed.Id, viewModel.SelectedFeedWarning?.FeedId);
            viewModel.IsFeedWarningsPopupOpen = true;
            Assert.IsTrue(viewModel.IsFeedWarningsPopupOpen);
            viewModel.NextFeedWarningCommand.Execute(null);

            await viewModel.DismissCurrentFeedWarningCommand.ExecuteAsync();

            Assert.AreEqual(1, viewModel.FeedWarningCount);
            Assert.AreEqual(alphaFeed.Id, viewModel.SelectedFeedWarning?.FeedId);
            var states = await readerStore.GetFeedRefreshStatesAsync(profile.Id);
            Assert.IsTrue(states.Single(state => state.FeedId == alphaFeed.Id).LastFailure is not null);
            Assert.IsNull(states.Single(state => state.FeedId == betaFeed.Id).LastFailure);

            await viewModel.DismissAllFeedWarningsCommand.ExecuteAsync();

            Assert.AreEqual(0, viewModel.FeedWarningCount);
            Assert.IsFalse(viewModel.HasFeedWarnings);
            Assert.IsFalse(viewModel.IsFeedWarningsPopupOpen);
            Assert.IsTrue((await readerStore.GetFeedRefreshStatesAsync(profile.Id))
                .All(state => state.LastFailure is null));

            var reopenedViewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                readingService,
                new FeedRefreshService(readerStore, catalogStore, new RecordingFeedDownloader()),
                new ProfilePreferences(RefreshFeedsWhenOpened: false));
            await reopenedViewModel.InitializeAsync();
            Assert.AreEqual(0, reopenedViewModel.FeedWarningCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public async Task FollowUsesPickerFolderAndCancelLeavesFeedUnfollowed()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();
            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var selectedFeed = new CatalogFeed("feed-selected", "DIY Source", "https://example.com/diy.xml", null, null);
            var cancelledFeed = new CatalogFeed("feed-cancelled", "Other Source", "https://example.com/other.xml", null, null);
            await catalogStore.AddFeedAsync(selectedFeed);
            await catalogStore.AddFeedAsync(cancelledFeed);

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();
            viewModel.FolderSelectionRequested = async (folders, _, createFolderAsync) =>
            {
                Assert.AreEqual(0, folders.Count);
                await createFolderAsync("DIY");
                return "DIY";
            };

            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(
                viewModel.CatalogFeeds.Single(feed => feed.Id == selectedFeed.Id));

            var subscription = (await readerStore.GetSubscriptionsAsync(profile.Id)).Single();
            Assert.AreEqual(selectedFeed.Id, subscription.FeedId);
            Assert.AreEqual("DIY", subscription.FolderName);
            Assert.IsTrue(viewModel.FeedLinks.Any(link => link.Route == "folder:DIY"));

            viewModel.FolderSelectionRequested = (_, _, _) => Task.FromResult<string?>(null);
            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(
                viewModel.CatalogFeeds.Single(feed => feed.Id == cancelledFeed.Id));

            Assert.AreEqual(1, (await readerStore.GetSubscriptionsAsync(profile.Id)).Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public void NavigationToFolderFiltersArticleRows()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        viewModel.NavigateCommand.Execute(viewModel.FeedLinks[1]);

        Assert.AreEqual("Gaming", viewModel.WorkspaceTitle);
        Assert.AreEqual(2, viewModel.VisibleArticles.Count);
        Assert.IsTrue(viewModel.IsArticleListVisible);
    }

    [TestMethod]
    public async Task FolderRouteShowsAtMostTenNewestArticlesPerFeedOnlyWhenSortedByFolder()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var feeds = new[]
            {
                new CatalogFeed("feed-one", "Source One", "https://example.com/one.xml", null, null),
                new CatalogFeed("feed-two", "Source Two", "https://example.com/two.xml", null, null)
            };
            await readerStore.AddFolderAsync(profile.Id, "Gaming");
            foreach (var feed in feeds)
            {
                await catalogStore.AddFeedAsync(feed);
                await readerStore.SubscribeAsync(profile.Id, feed.Id, "Gaming");
                await readerStore.SaveArticlesAsync(feed.Id, Enumerable.Range(0, 12)
                    .Select(index => new FeedArticle(
                        $"{feed.Id}-article-{index}",
                        feed.Id,
                        $"item-{index}",
                        $"{feed.Name} article {index}",
                        null,
                        DateTimeOffset.UtcNow.AddMinutes(-index),
                        null,
                        null))
                    .ToArray());
            }

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null);
            await viewModel.InitializeAsync();
            viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "folder:Gaming"));

            Assert.AreEqual(20, viewModel.VisibleArticles.Count);
            var feedGroups = viewModel.VisibleArticles.GroupBy(article => article.FeedId).ToArray();
            Assert.AreEqual(2, feedGroups.Length);
            foreach (var group in feedGroups)
            {
                Assert.AreEqual(10, group.Count());
                Assert.IsTrue(group.Any(article => article.Title.EndsWith("article 0", StringComparison.Ordinal)));
                Assert.IsFalse(group.Any(article => article.Title.EndsWith("article 10", StringComparison.Ordinal)));
                Assert.IsFalse(group.Any(article => article.Title.EndsWith("article 11", StringComparison.Ordinal)));
            }

            viewModel.ApplyPreferences(new ProfilePreferences(
                ProfileStartPage.FirstFolder,
                FolderArticleLimitPerFeed: 5));

            Assert.AreEqual(10, viewModel.VisibleArticles.Count);

            viewModel.IsSortByDate = true;

            Assert.AreEqual(24, viewModel.VisibleArticles.Count);

            viewModel.IsSortByFolder = true;

            Assert.AreEqual(10, viewModel.VisibleArticles.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public void ArticleViewModeCanSwitchBetweenCardsTitleOnlyAndMagazine()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        Assert.IsTrue(viewModel.IsCardsView);
        Assert.IsFalse(viewModel.IsListView);

        viewModel.IsListView = true;

        Assert.IsFalse(viewModel.IsCardsView);
        Assert.IsTrue(viewModel.IsListView);

        viewModel.IsMagazineView = true;

        Assert.IsFalse(viewModel.IsCardsView);
        Assert.IsFalse(viewModel.IsListView);
        Assert.IsTrue(viewModel.IsMagazineView);

        viewModel.IsCardsView = true;

        Assert.IsTrue(viewModel.IsCardsView);
        Assert.IsFalse(viewModel.IsListView);
        Assert.IsFalse(viewModel.IsMagazineView);
    }

    [TestMethod]
    public void DateSortHidesFolderNamesAndGroupsAcrossAllArticlePresentations()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            try
            {
                foreach (var presentation in Enum.GetValues<ProfileArticlePresentation>())
                {
                    RssReader.App.MainWindow? window = null;
                    try
                    {
                        var preferences = new ProfilePreferences(
                            Presentation: presentation,
                            Sort: ProfileArticleSort.Newest);
                        var viewModel = new MainWindowViewModel(
                            Profile.CreateRegular("Reader"),
                            null,
                            null,
                            null,
                            preferences);

                        Assert.IsTrue(viewModel.IsSortByDate);
                        Assert.IsNull(viewModel.ArticleListView.Groups);
                        var article = viewModel.ArticleListView.Cast<ArticleRowViewModel>().First();
                        window = CreateTestMainWindow(viewModel);
                        WpfTestHost.Application.MainWindow = window;
                        window.Show();
                        window.UpdateLayout();

                        var articleListName = presentation switch
                        {
                            ProfileArticlePresentation.TitleOnly => "ArticleRowsList",
                            ProfileArticlePresentation.Magazine => "ArticleMagazineList",
                            _ => "ArticleCardsList"
                        };
                        var articleList = FindArticleListElement<ListBox>(window, articleListName);
                        Assert.AreEqual(Visibility.Visible, articleList.Visibility);
                        articleList.ScrollIntoView(article);
                        articleList.UpdateLayout();
                        var articleContainer = articleList.ItemContainerGenerator.ContainerFromItem(article)
                            ?? throw new AssertFailedException(
                                $"The {presentation} date-sorted article was not created.");
                        var folderLabels = FindVisualChildren<TextBlock>(articleContainer)
                            .Where(textBlock =>
                                BindingOperations.GetBinding(textBlock, TextBlock.TextProperty)?.Path.Path ==
                                nameof(ArticleRowViewModel.Folder))
                            .ToArray();
                        Assert.IsTrue(
                            folderLabels.All(label => !label.IsVisible),
                            $"Folder labels were visible in {presentation} mode.");
                        window.Close();
                    }
                    finally
                    {
                        if (window?.IsVisible == true)
                        {
                            window.Close();
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void WindowPlacementStoreRoundTripsAndClampsBoundsToTheVisibleDesktop()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rss-reader-placement-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "main-window-placement.json");
        try
        {
            var store = new RssReader.App.WindowPlacementStore(filePath);
            var placement = new RssReader.App.WindowPlacement(-1900, -800, 2200, 1200, true, 410);
            store.Save(placement);

            Assert.AreEqual(placement, store.Load());
            Assert.IsTrue(placement.TryGetVisibleBounds(new Rect(0, 0, 1600, 900), 760, 540, out var bounds));
            Assert.AreEqual(new Rect(0, 0, 1600, 900), bounds);
            Assert.IsFalse(
                new RssReader.App.WindowPlacement(double.NaN, 0, 900, 600, false)
                    .TryGetVisibleBounds(new Rect(0, 0, 1600, 900), 760, 540, out _));

            File.WriteAllText(filePath, "{");
            Assert.IsNull(store.Load());
            File.WriteAllText(
                filePath,
                "{\"Left\":10,\"Top\":20,\"Width\":960,\"Height\":620,\"IsMaximized\":false}");
            Assert.IsNull(store.Load()!.SidebarWidth);
        }
        finally
        {
            DeleteWindowPlacementFiles(filePath);
        }
    }

    [TestMethod]
    public void MainWindowRestoresAndPersistsItsNormalBoundsAcrossSessions()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rss-reader-window-placement-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "main-window-placement.json");
        Exception? failure = null;
        try
        {
            WpfTestHost.Run(() =>
            {
                RssReader.App.MainWindow? window = null;
                RssReader.App.MainWindow? reopenedWindow = null;
                try
                {
                    var visibleArea = new Rect(
                        SystemParameters.VirtualScreenLeft,
                        SystemParameters.VirtualScreenTop,
                        SystemParameters.VirtualScreenWidth,
                        SystemParameters.VirtualScreenHeight);
                    var store = new RssReader.App.WindowPlacementStore(filePath);
                    var initialWidth = Math.Min(960, visibleArea.Width);
                    var initialHeight = Math.Min(620, visibleArea.Height);
                    var initialPlacement = new RssReader.App.WindowPlacement(
                        visibleArea.Left + Math.Min(10, visibleArea.Width - initialWidth),
                        visibleArea.Top + Math.Min(10, visibleArea.Height - initialHeight),
                        initialWidth,
                        initialHeight,
                        false,
                        324);
                    store.Save(initialPlacement);

                    var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                    window = new RssReader.App.MainWindow(
                        viewModel,
                        store);
                    Assert.AreEqual(initialPlacement.Left, window.Left);
                    Assert.AreEqual(initialPlacement.Top, window.Top);
                    Assert.AreEqual(initialPlacement.Width, window.Width);
                    Assert.AreEqual(initialPlacement.Height, window.Height);
                    Assert.AreEqual(324, viewModel.SidebarPanelWidth);

                    WpfTestHost.Application.MainWindow = window;
                    window.Show();
                    window.UpdateLayout();
                    var shellGrid = (Grid)window.FindName("ShellGrid")!;
                    var sidebarResizeSplitter = (GridSplitter)window.FindName("SidebarResizeSplitter")!;
                    Assert.AreEqual(Visibility.Visible, sidebarResizeSplitter.Visibility);
                    Assert.AreEqual(AutomationProperties.GetName(sidebarResizeSplitter), "Resize sidebar");
                    Assert.AreEqual(GridResizeBehavior.CurrentAndNext, sidebarResizeSplitter.ResizeBehavior);
                    shellGrid.ColumnDefinitions[0].Width = new GridLength(354);
                    window.UpdateLayout();
                    Assert.AreEqual(354, viewModel.SidebarPanelWidth);
                    var sidebarControl = (UserControl)window.FindName("SidebarView")!;
                    Assert.AreEqual(354, ((Border)sidebarControl.FindName("SidebarPanel")!).Width);

                    var resizedWidth = Math.Min(1040, visibleArea.Width);
                    var resizedHeight = Math.Min(680, visibleArea.Height);
                    var expectedBounds = new Rect(
                        visibleArea.Left + Math.Min(90, visibleArea.Width - resizedWidth),
                        visibleArea.Top + Math.Min(70, visibleArea.Height - resizedHeight),
                        resizedWidth,
                        resizedHeight);
                    window.Left = expectedBounds.Left;
                    window.Top = expectedBounds.Top;
                    window.Width = expectedBounds.Width;
                    window.Height = expectedBounds.Height;
                    window.Close();

                    var savedPlacement = store.Load()
                        ?? throw new AssertFailedException("Closing the main window did not persist its placement.");
                    Assert.AreEqual(expectedBounds.Left, savedPlacement.Left);
                    Assert.AreEqual(expectedBounds.Top, savedPlacement.Top);
                    Assert.AreEqual(expectedBounds.Width, savedPlacement.Width);
                    Assert.AreEqual(expectedBounds.Height, savedPlacement.Height);
                    Assert.AreEqual(354, savedPlacement.SidebarWidth);
                    Assert.IsFalse(savedPlacement.IsMaximized);

                    var reopenedViewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                    reopenedWindow = new RssReader.App.MainWindow(
                        reopenedViewModel,
                        store);
                    Assert.AreEqual(savedPlacement.Left, reopenedWindow.Left);
                    Assert.AreEqual(savedPlacement.Top, reopenedWindow.Top);
                    Assert.AreEqual(savedPlacement.Width, reopenedWindow.Width);
                    Assert.AreEqual(savedPlacement.Height, reopenedWindow.Height);
                    Assert.AreEqual(savedPlacement.SidebarWidth, reopenedViewModel.SidebarPanelWidth);
                    Assert.AreEqual(354, reopenedViewModel.SidebarPanelWidth);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    if (reopenedWindow?.IsVisible == true)
                    {
                        reopenedWindow.Close();
                    }

                    if (window?.IsVisible == true)
                    {
                        window.Close();
                    }
                }
            });
            Assert.IsNull(failure, failure?.ToString());
        }
        finally
        {
            DeleteWindowPlacementFiles(filePath);
        }
    }

    [TestMethod]
    public void MainWindowFrameProvidesNativeResizeHitTargets()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            RssReader.App.MainWindow? window = null;
            try
            {
                window = CreateTestMainWindow(new MainWindowViewModel(Profile.CreateRegular("Reader")));
                WpfTestHost.Application.MainWindow = window;
                window.Show();
                window.UpdateLayout();

                var handle = new WindowInteropHelper(window).Handle;
                Assert.IsTrue(GetWindowRect(handle, out var rectangle));
                Assert.AreEqual(
                    10,
                    SendMessage(
                        handle,
                        0x0084,
                        nint.Zero,
                        MakeScreenPoint(rectangle.Left + 2, (rectangle.Top + rectangle.Bottom) / 2))
                    .ToInt32());
                Assert.AreEqual(
                    15,
                    SendMessage(
                        handle,
                        0x0084,
                        nint.Zero,
                        MakeScreenPoint((rectangle.Left + rectangle.Right) / 2, rectangle.Bottom - 2))
                    .ToInt32());
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (window?.IsVisible == true)
                {
                    window.Close();
                }
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void CatalogFeedSubscriptionLabelReflectsItsFollowingState()
    {
        var feed = new CatalogFeedListItem(
            "feed-1",
            "Example",
            "https://example.com/feed.xml",
            null,
            null);

        Assert.AreEqual("Follow", feed.SubscriptionLabel);

        feed.IsSubscribed = true;
        Assert.AreEqual("Unfollow", feed.SubscriptionLabel);
    }

    [TestMethod]
    public void ArticleCountSubtitleHidesOnFollowSources()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var followSources = viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources");

        Assert.IsTrue(viewModel.IsArticleCountVisible);

        viewModel.NavigateCommand.Execute(followSources);

        Assert.IsFalse(viewModel.IsArticleCountVisible);
        Assert.IsFalse(viewModel.IsWorkspaceHeaderVisible);
    }

    [TestMethod]
    public void WorkspaceHeaderIsHiddenForViewsWithTheirOwnTopBar()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var viewsWithoutWorkspaceHeader = new[]
        {
            viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"),
            viewModel.PrimaryLinks.Single(link => link.Route == "Search"),
            viewModel.ReadingLinks.Single(link => link.Route == "Read later"),
            viewModel.ReadingLinks.Single(link => link.Route == "Recently read")
        };

        foreach (var link in viewsWithoutWorkspaceHeader)
        {
            viewModel.NavigateCommand.Execute(link);
            Assert.IsFalse(viewModel.IsWorkspaceHeaderVisible, $"Workspace header was visible on '{link.Route}'.");
        }

        viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));
        Assert.IsTrue(viewModel.IsWorkspaceHeaderVisible);

        var catalogMaster = new MainWindowViewModel(Profile.CreateCatalogMaster());
        Assert.IsFalse(catalogMaster.IsWorkspaceHeaderVisible);
    }

    [TestMethod]
    public void TodayRouteShowsOnlyArticlesFromThePrevious24Hours()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var now = DateTimeOffset.UtcNow;

        Assert.AreEqual("Today", viewModel.ActiveRoute);
        Assert.AreEqual(3, viewModel.VisibleArticles.Count);
        Assert.IsTrue(viewModel.VisibleArticles.All(article =>
            article.PublishedAt > now.AddHours(-24) && article.PublishedAt <= now));
        Assert.IsFalse(viewModel.VisibleArticles.Any(article => article.Title == "The small tools making a big difference"));
        Assert.IsFalse(viewModel.VisibleArticles.Any(article => article.Title == "Why open standards still matter"));

        viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));

        Assert.AreEqual(5, viewModel.VisibleArticles.Count);
    }

    [TestMethod]
    public void SearchFiltersByTitleSourceAndSummary()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var searchLink = viewModel.PrimaryLinks.Single(link => link.Route == "Search");
        viewModel.NavigateCommand.Execute(searchLink);

        viewModel.SearchQuery = "handheld";

        Assert.AreEqual(1, viewModel.VisibleArticles.Count);
        StringAssert.Contains(viewModel.VisibleArticles[0].Title, "handheld");
        viewModel.SearchQuery = "THE NEXT GENERATION OF HANDHELD GAMING IS HERE";
        Assert.AreEqual(1, viewModel.VisibleArticles.Count);
    }

    [TestMethod]
    public void SearchEmptyStateDistinguishesBlankQueryFromNoMatches()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Search"));

        Assert.IsTrue(viewModel.IsArticleListEmpty);
        Assert.AreEqual("Enter a search term to find articles.", viewModel.ArticleListEmptyMessage);

        viewModel.SearchQuery = "no matching article";

        Assert.IsTrue(viewModel.IsArticleListEmpty);
        Assert.AreEqual("No articles match this search.", viewModel.ArticleListEmptyMessage);
    }

    [TestMethod]
    public void SearchQueryDoesNotLeakBetweenProfileViewModels()
    {
        var firstProfile = new MainWindowViewModel(Profile.CreateRegular("First Reader"));
        var secondProfile = new MainWindowViewModel(Profile.CreateRegular("Second Reader"));
        firstProfile.NavigateCommand.Execute(firstProfile.PrimaryLinks.Single(link => link.Route == "Search"));
        secondProfile.NavigateCommand.Execute(secondProfile.PrimaryLinks.Single(link => link.Route == "Search"));

        firstProfile.SearchQuery = "handheld";
        secondProfile.SearchQuery = "Hyrule";

        Assert.AreEqual("handheld", firstProfile.SearchQuery);
        Assert.AreEqual("Hyrule", secondProfile.SearchQuery);
        Assert.AreEqual("The next generation of handheld gaming is here", firstProfile.VisibleArticles.Single().Title);
        Assert.AreEqual("A new chapter for the world of Hyrule", secondProfile.VisibleArticles.Single().Title);
    }

    [TestMethod]
    public void SelectingArticleReplacesListAndBackReturnsToList()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Search"));
        viewModel.SearchQuery = "handheld";
        var article = viewModel.VisibleArticles[0];

        viewModel.SelectArticleCommand.Execute(article);

        Assert.IsFalse(viewModel.IsArticleListVisible);
        Assert.IsTrue(viewModel.IsReadingViewVisible);
        Assert.AreSame(article, viewModel.SelectedArticle);

        viewModel.BackCommand.Execute(null);

        Assert.IsTrue(viewModel.IsArticleListVisible);
        Assert.IsFalse(viewModel.IsReadingViewVisible);
        Assert.AreEqual("handheld", viewModel.SearchQuery);
        Assert.AreSame(article, viewModel.VisibleArticles.Single());
    }

    [TestMethod]
    public void BackCommandRestoresPreviousArticleAndRouteViews()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var todayRoute = viewModel.ActiveRoute;

        viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Search"));
        viewModel.SearchQuery = "handheld";
        var article = viewModel.VisibleArticles.Single();
        viewModel.SelectArticleCommand.Execute(article);

        Assert.IsTrue(viewModel.CanGoBack);
        viewModel.BackCommand.Execute(null);

        Assert.AreSame(article, viewModel.VisibleArticles.Single());
        Assert.IsNull(viewModel.SelectedArticle);
        Assert.AreEqual("Search", viewModel.ActiveRoute);
        Assert.AreEqual("handheld", viewModel.SearchQuery);
        Assert.IsTrue(viewModel.CanGoBack);

        viewModel.BackCommand.Execute(null);

        Assert.AreEqual(todayRoute, viewModel.ActiveRoute);
        Assert.AreEqual(string.Empty, viewModel.SearchQuery);
        Assert.IsFalse(viewModel.CanGoBack);
    }

    [TestMethod]
    public async Task TopicPillsFilterTheListAndReturnFromTheArticleWithTopicSelected()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var profile = Profile.CreateRegular("Reader");
            await profileStore.AddAsync(profile);
            var feed = new CatalogFeed("topic-feed", "Topic News", "https://example.com/feed.xml", "News", null);
            await catalogStore.AddFeedAsync(feed);
            await readerStore.AddFolderAsync(profile.Id, "News");
            await readerStore.SubscribeAsync(profile.Id, feed.Id, "News");
            await readerStore.SaveArticlesAsync(feed.Id,
            [
                new FeedArticle(
                    "topic-one",
                    feed.Id,
                    "item-1",
                    "Article with two topics",
                    null,
                    DateTimeOffset.UtcNow,
                    "Article summary",
                    "Full story")
                {
                    Categories = [new ArticleCategory("science"), new ArticleCategory("reviews")]
                },
                new FeedArticle(
                    "topic-two",
                    feed.Id,
                    "item-2",
                    "Article about reviews",
                    null,
                    DateTimeOffset.UtcNow,
                    "Article summary",
                    "Full story")
                {
                    Categories = [new ArticleCategory("reviews")]
                },
                new FeedArticle(
                    "topic-three",
                    feed.Id,
                    "item-3",
                    "Article about science",
                    null,
                    DateTimeOffset.UtcNow,
                    "Article summary",
                    "Full story")
                {
                    Categories = [new ArticleCategory("science")]
                }
            ]);

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null,
                new ProfilePreferences(HideReadArticles: false));
            await viewModel.InitializeAsync();

            viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Search"));
            viewModel.SearchQuery = "article";
            var article = viewModel.VisibleArticles.Single(item => item.ArticleId == "topic-one");
            var scienceTopic = article.TopicChips.Single(topic => topic.Name == "science");

            Assert.IsTrue(viewModel.SelectArticleTopicCommand.CanExecute(scienceTopic));
            viewModel.SelectArticleTopicCommand.Execute(scienceTopic);
            Assert.AreEqual("science", viewModel.SelectedArticleTopic?.Term);
            CollectionAssert.AreEquivalent(
                new[] { "topic-one", "topic-three" },
                viewModel.VisibleArticles.Select(item => item.ArticleId).ToArray());

            viewModel.SelectArticleCommand.Execute(article);
            var reviewsTopic = article.TopicChips.Single(topic => topic.Name == "reviews");
            Assert.IsTrue(viewModel.SelectArticleTopicCommand.CanExecute(reviewsTopic));
            viewModel.SelectArticleTopicCommand.Execute(reviewsTopic);

            Assert.IsTrue(viewModel.IsArticleListVisible);
            Assert.AreEqual("Search", viewModel.ActiveRoute);
            Assert.AreEqual("article", viewModel.SearchQuery);
            Assert.AreEqual("reviews", viewModel.SelectedArticleTopic?.Term);
            CollectionAssert.AreEquivalent(
                new[] { "topic-one", "topic-two" },
                viewModel.VisibleArticles.Select(item => item.ArticleId).ToArray());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public void BackCommandReturnsToThePreviouslyOpenedArticle()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var firstArticle = viewModel.VisibleArticles[0];
        viewModel.SelectArticleCommand.Execute(firstArticle);

        viewModel.NextArticleCommand.Execute(null);

        var nextArticle = viewModel.SelectedArticle;
        Assert.IsNotNull(nextArticle);
        Assert.AreNotSame(firstArticle, nextArticle);
        viewModel.BackCommand.Execute(null);

        Assert.AreSame(firstArticle, viewModel.SelectedArticle);
    }

    [TestMethod]
    public void SelectedArticleReadAndSavedCommandsToggleState()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var article = viewModel.VisibleArticles[0];
        viewModel.SelectArticleCommand.Execute(article);

        Assert.IsTrue(article.IsRead);
        viewModel.ToggleReadCommand.Execute(null);
        Assert.IsFalse(article.IsRead);
        viewModel.ToggleReadCommand.Execute(null);
        Assert.IsTrue(article.IsRead);

        viewModel.ToggleSavedCommand.Execute(null);
        Assert.IsTrue(article.IsSaved);
        viewModel.ToggleSavedCommand.Execute(null);
        Assert.IsFalse(article.IsSaved);
    }

    [TestMethod]
    public void PreviousAndNextArticleFollowVisibleSortedSequenceAndBoundaries()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));
        viewModel.IsSortByDate = true;
        var articles = viewModel.VisibleArticles.OrderByDescending(article => article.PublishedAt).ToArray();

        Assert.IsFalse(viewModel.PreviousArticleCommand.CanExecute(null));
        Assert.IsFalse(viewModel.NextArticleCommand.CanExecute(null));
        viewModel.SelectArticleCommand.Execute(articles[0]);
        Assert.IsFalse(viewModel.PreviousArticleCommand.CanExecute(null));
        Assert.IsTrue(viewModel.NextArticleCommand.CanExecute(null));

        viewModel.NextArticleCommand.Execute(null);
        Assert.AreSame(articles[1], viewModel.SelectedArticle);
        viewModel.PreviousArticleCommand.Execute(null);
        Assert.AreSame(articles[0], viewModel.SelectedArticle);

        viewModel.SelectArticleCommand.Execute(articles[^1]);
        Assert.IsTrue(viewModel.PreviousArticleCommand.CanExecute(null));
        Assert.IsFalse(viewModel.NextArticleCommand.CanExecute(null));
    }

    [TestMethod]
    public void PreviousAndNextArticleFollowFolderSortedSequence()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "folder:Gaming"));
        viewModel.IsSortByFolder = true;
        var folderSortedArticles = viewModel.FolderSortedArticleListView
            .Cast<ArticleRowViewModel>()
            .ToArray();

        Assert.AreEqual(2, folderSortedArticles.Length);
        viewModel.SelectArticleCommand.Execute(folderSortedArticles[0]);

        viewModel.NextArticleCommand.Execute(null);

        Assert.AreSame(folderSortedArticles[1], viewModel.SelectedArticle);
    }

    [TestMethod]
    public void NextUnreadArticleFollowsTheVisibleSortedSequence()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));
        viewModel.IsSortByDate = true;
        var articles = viewModel.VisibleArticles
            .OrderByDescending(article => article.PublishedAt)
            .ToArray();
        viewModel.OpenArticleInReaderCommand.Execute(articles[0]);

        Assert.IsTrue(viewModel.NextUnreadArticleCommand.CanExecute(null));
        viewModel.NextUnreadArticleCommand.Execute(null);

        Assert.AreSame(articles[1], viewModel.SelectedArticle);
        Assert.AreEqual("2 of 5", viewModel.ArticlePositionLabel);
    }

    [TestMethod]
    public async Task PreloadNextArticleCachesOnlyTheAdjacentArticle()
    {
        var requestStarted = new TaskCompletionSource<Uri>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<ExtractedArticleContent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubArticleContentService((uri, _) =>
        {
            requestStarted.TrySetResult(uri);
            return response.Task;
        });
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            profilePreferences: new ProfilePreferences(PreloadNextArticle: true),
            articleContentService: service);
        viewModel.VisibleArticles.Clear();
        var currentArticle = new ArticleRowViewModel(
            "Current story",
            "Example feed",
            DateTimeOffset.UtcNow.AddHours(-1),
            "News",
            [],
            "Current preview.",
            articleId: "current-story",
            link: "https://example.test/current");
        var nextArticle = new ArticleRowViewModel(
            "Next story",
            "Example feed",
            DateTimeOffset.UtcNow.AddHours(-2),
            "News",
            [],
            "Next preview.",
            articleId: "next-story",
            link: "https://example.test/next");
        viewModel.VisibleArticles.Add(currentArticle);
        viewModel.VisibleArticles.Add(nextArticle);
        var cacheWritten = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        nextArticle.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ArticleRowViewModel.ExtractedContentHtml))
            {
                cacheWritten.TrySetResult();
            }
        };

        viewModel.OpenArticleInReaderCommand.Execute(currentArticle);
        var requestedUri = await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(nextArticle.Link, requestedUri.AbsoluteUri);
        Assert.AreEqual(1, service.RequestCount);
        response.SetResult(new ExtractedArticleContent(
            "<article><p>Prefetched body.</p></article>",
            "Next story",
            null,
            DateTimeOffset.UtcNow));
        await cacheWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(nextArticle.HasExtractedContent);
        Assert.AreEqual(1, service.RequestCount);
    }

    [TestMethod]
    public void ArticleNavigationDisablesWhenSelectionLeavesFilteredResults()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));
        var removedByFilter = viewModel.VisibleArticles[^1];
        viewModel.SelectArticleCommand.Execute(removedByFilter);

        viewModel.SearchQuery = "handheld";

        Assert.AreEqual(1, viewModel.VisibleArticles.Count);
        StringAssert.Contains(viewModel.VisibleArticles.Single().Title, "handheld");
        Assert.IsFalse(viewModel.PreviousArticleCommand.CanExecute(null));
        Assert.IsFalse(viewModel.NextArticleCommand.CanExecute(null));
    }

    [TestMethod]
    public void SearchNavigationHasDedicatedSearchSurfaceWithoutGoToRoute()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        CollectionAssert.AreEqual(
            new[] { "Today", "Follow sources", "Search" },
            viewModel.PrimaryLinks.Select(link => link.Route).ToArray());
        Assert.IsTrue(viewModel.IsArticleSearchBoxVisible);

        viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Search"));

        Assert.IsTrue(viewModel.IsSearchRoute);
        Assert.IsFalse(viewModel.IsArticleSearchBoxVisible);
    }

    [TestMethod]
    public void ReaderActionsHaveAccessibleNamesAndKeyboardBindings()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            try
            {
                var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                var window = CreateTestMainWindow(viewModel);
                var articleSearchBox = (TextBox)window.FindName("ArticleSearchBox");
                var clearArticleSearchButton = (Button)window.FindName("ClearArticleSearchButton");
                var searchViewLabel = FindArticleListElement<TextBlock>(window, "SearchViewLabel");
                var searchViewSearchBox = FindArticleListElement<TextBox>(window, "SearchViewSearchBox");
                var clearSearchViewButton = FindArticleListElement<Button>(window, "ClearSearchViewButton");
                var feedWarningsView = (FeedWarningsView)window.FindName("FeedWarningsView")!;
                var feedWarningsButton = (Button)feedWarningsView.FindName("FeedWarningsButton");
                var feedWarningsPopup = (System.Windows.Controls.Primitives.Popup)feedWarningsView.FindName("FeedWarningsPopup");
                var previousFeedWarningButton = (Button)feedWarningsView.FindName("PreviousFeedWarningButton");
                var nextFeedWarningButton = (Button)feedWarningsView.FindName("NextFeedWarningButton");
                var feedWarningMessage = (TextBox)feedWarningsView.FindName("FeedWarningMessage");
                var dismissFeedWarningButton = (Button)feedWarningsView.FindName("DismissFeedWarningButton");
                var dismissAllFeedWarningsButton = (Button)feedWarningsView.FindName("DismissAllFeedWarningsButton");
                var catalogAdminView = (UserControl)window.FindName("CatalogAdminView")!;
                var catalogFeedCheckErrorText = (TextBlock)catalogAdminView.FindName("CatalogFeedCheckErrorText");
                var backButton = FindReaderElement<Button>(window, "BackButton");
                var workspaceBackButton = (Button)window.FindName("WorkspaceBackButton");
                var toggleReadButton = FindReaderElement<Button>(window, "ToggleReadButton");
                var toggleSavedButton = FindReaderElement<Button>(window, "ToggleSavedButton");
                var previousButton = FindReaderElement<Button>(window, "PreviousArticleButton");
                var nextButton = FindReaderElement<Button>(window, "NextArticleButton");

                Assert.AreEqual("Search articles", AutomationProperties.GetName(articleSearchBox));
                Assert.AreEqual("Clear search expression", AutomationProperties.GetName(clearArticleSearchButton));
                Assert.AreEqual("Search label", AutomationProperties.GetName(searchViewLabel));
                Assert.AreEqual("Search", searchViewLabel.Text);
                Assert.AreEqual("Search articles", AutomationProperties.GetName(searchViewSearchBox));
                Assert.AreEqual("Clear search expression", AutomationProperties.GetName(clearSearchViewButton));
                var articleSearchBinding = BindingOperations.GetBinding(articleSearchBox, TextBox.TextProperty);
                var searchViewBinding = BindingOperations.GetBinding(searchViewSearchBox, TextBox.TextProperty);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.SearchQuery),
                    articleSearchBinding?.Path.Path);
                Assert.AreEqual(nameof(MainWindowViewModel.SearchQuery), searchViewBinding?.Path.Path);
                Assert.AreEqual(300, articleSearchBinding?.Delay);
                Assert.AreEqual(300, searchViewBinding?.Delay);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.ClearSearchQueryCommand),
                    BindingOperations.GetBinding(clearArticleSearchButton, Button.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.ClearSearchQueryCommand),
                    BindingOperations.GetBinding(clearSearchViewButton, Button.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.HasFeedWarnings),
                    BindingOperations.GetBinding(feedWarningsButton, UIElement.VisibilityProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.IsFeedWarningsPopupOpen),
                    BindingOperations.GetBinding(feedWarningsPopup, System.Windows.Controls.Primitives.Popup.IsOpenProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.PreviousFeedWarningCommand),
                    BindingOperations.GetBinding(previousFeedWarningButton, Button.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.NextFeedWarningCommand),
                    BindingOperations.GetBinding(nextFeedWarningButton, Button.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    "SelectedFeedWarning.Message",
                    BindingOperations.GetBinding(feedWarningMessage, TextBox.TextProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.DismissCurrentFeedWarningCommand),
                    BindingOperations.GetBinding(dismissFeedWarningButton, Button.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.DismissAllFeedWarningsCommand),
                    BindingOperations.GetBinding(dismissAllFeedWarningsButton, Button.CommandProperty)?.Path.Path);
                Assert.IsTrue(feedWarningMessage.IsReadOnly);
                Assert.IsTrue(feedWarningMessage.IsReadOnlyCaretVisible);
                viewModel.SearchQuery = "trump";
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, clearArticleSearchButton.Visibility);
                Assert.AreEqual(Visibility.Visible, clearSearchViewButton.Visibility);
                viewModel.ClearSearchQueryCommand.Execute(null);
                window.Dispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));
                window.UpdateLayout();
                Assert.AreEqual(string.Empty, viewModel.SearchQuery);
                Assert.AreEqual(Visibility.Collapsed, clearArticleSearchButton.Visibility);
                Assert.AreEqual(Visibility.Collapsed, clearSearchViewButton.Visibility);
                Assert.IsNull(window.FindName("GoToSearchBox"));
                Assert.IsFalse(viewModel.PrimaryLinks.Any(link => link.Route == "Go to..."));
                Assert.AreEqual("Go back to previous view", AutomationProperties.GetName(backButton));
                Assert.IsInstanceOfType(backButton.Content, typeof(System.Windows.Shapes.Path));
                Assert.AreEqual("Back to previous view (Esc)", backButton.ToolTip);
                Assert.AreEqual("Go back to previous view", AutomationProperties.GetName(workspaceBackButton));
                Assert.AreEqual(Visibility.Collapsed, workspaceBackButton.Visibility);
                Assert.IsNotNull(catalogFeedCheckErrorText.ContextMenu);
                Assert.AreEqual("Copy", ((MenuItem)catalogFeedCheckErrorText.ContextMenu.Items[0]).Header);
                Assert.AreEqual(
                    "PlacementTarget",
                    BindingOperations.GetBinding(
                        catalogFeedCheckErrorText.ContextMenu,
                        FrameworkElement.DataContextProperty)?.Path.Path);
                Assert.AreEqual("Toggle article read status", AutomationProperties.GetName(toggleReadButton));
                Assert.AreEqual("Toggle article saved status", AutomationProperties.GetName(toggleSavedButton));
                Assert.AreEqual("Previous article", AutomationProperties.GetName(previousButton));
                Assert.AreEqual("Next article", AutomationProperties.GetName(nextButton));
                Assert.AreEqual(
                    nameof(MainWindowViewModel.BackCommand),
                    BindingOperations.GetBinding(backButton, Button.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.BackCommand),
                    BindingOperations.GetBinding(workspaceBackButton, Button.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.ToggleReadCommand),
                    BindingOperations.GetBinding(toggleReadButton, Button.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.ToggleSavedCommand),
                    BindingOperations.GetBinding(toggleSavedButton, Button.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.PreviousArticleCommand),
                    BindingOperations.GetBinding(previousButton, Button.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.NextArticleCommand),
                    BindingOperations.GetBinding(nextButton, Button.CommandProperty)?.Path.Path);

                var backBinding = window.InputBindings.OfType<KeyBinding>()
                    .Single(binding => binding.Key == Key.Escape);
                var toggleReadBinding = window.InputBindings.OfType<KeyBinding>()
                    .Single(binding => binding.Key == Key.R);
                var toggleSavedBinding = window.InputBindings.OfType<KeyBinding>()
                    .Single(binding => binding.Key == Key.S);
                var previousBinding = window.InputBindings.OfType<KeyBinding>()
                    .Single(binding => binding.Key == Key.Left);
                var nextBinding = window.InputBindings.OfType<KeyBinding>()
                    .Single(binding => binding.Key == Key.Right);
                Assert.AreEqual(ModifierKeys.None, backBinding.Modifiers);
                Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Shift, toggleReadBinding.Modifiers);
                Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Shift, toggleSavedBinding.Modifiers);
                Assert.AreEqual(ModifierKeys.Alt, previousBinding.Modifiers);
                Assert.AreEqual(ModifierKeys.Alt, nextBinding.Modifiers);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.BackCommand),
                    BindingOperations.GetBinding(backBinding, InputBinding.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.ToggleReadCommand),
                    BindingOperations.GetBinding(toggleReadBinding, InputBinding.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.ToggleSavedCommand),
                    BindingOperations.GetBinding(toggleSavedBinding, InputBinding.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.PreviousArticleCommand),
                    BindingOperations.GetBinding(previousBinding, InputBinding.CommandProperty)?.Path.Path);
                Assert.AreEqual(
                    nameof(MainWindowViewModel.NextArticleCommand),
                    BindingOperations.GetBinding(nextBinding, InputBinding.CommandProperty)?.Path.Path);

                viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Search"));
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, workspaceBackButton.Visibility);
                viewModel.BackCommand.Execute(null);
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, workspaceBackButton.Visibility);
                window.Close();
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void BackToListRestoresKeyboardFocusToOpenedArticle()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            try
            {
                var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"))
                {
                    IsListView = true
                };
                var article = viewModel.VisibleArticles[0];
                var window = CreateTestMainWindow(viewModel);
                WpfTestHost.Application.MainWindow = window;
                window.Show();
                window.Activate();
                window.UpdateLayout();
                var articleList = FindArticleListElement<ListBox>(window, "ArticleRowsList");
                articleList.ScrollIntoView(article);
                articleList.UpdateLayout();

                viewModel.SelectArticleCommand.Execute(article);
                window.UpdateLayout();
                viewModel.BackCommand.Execute(null);
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                window.UpdateLayout();

                var articleContainer = (ListBoxItem?)articleList.ItemContainerGenerator.ContainerFromItem(article);
                Assert.IsNotNull(articleContainer);
                var focusedAction = Keyboard.FocusedElement as Button;
                Assert.IsNotNull(focusedAction);
                Assert.AreEqual(article.Title, AutomationProperties.GetName(focusedAction));
                Assert.IsTrue(
                    articleContainer.IsKeyboardFocusWithin,
                    $"Expected focus within the article row, but focus was {Keyboard.FocusedElement?.GetType().FullName ?? "null"}.");

                var articleActionSource = PresentationSource.FromVisual(focusedAction);
                Assert.IsNotNull(articleActionSource);
                focusedAction.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, articleActionSource, Environment.TickCount, Key.Space)
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                });
                focusedAction.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, articleActionSource, Environment.TickCount, Key.Space)
                {
                    RoutedEvent = Keyboard.KeyUpEvent
                });
                Assert.AreSame(article, viewModel.SelectedArticle);

                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void ReadLaterRouteShowsSavedArticles()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var readLaterLink = viewModel.ReadingLinks.Single(link => link.Route == "Read later");

        viewModel.NavigateCommand.Execute(readLaterLink);

        Assert.AreEqual(2, viewModel.VisibleArticles.Count);
        Assert.IsTrue(viewModel.VisibleArticles.All(article => article.IsSaved));
    }

    [TestMethod]
    public void GroupedArticlesAreSortedByFolderAndNewestFirst()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        Assert.IsTrue(viewModel.IsSortByFolder);
        var groups = viewModel.ArticleListView.Groups;

        Assert.IsNotNull(groups);
        Assert.AreEqual(2, groups.Count);
        var gamingGroup = (CollectionViewGroup)groups[0];
        var techGroup = (CollectionViewGroup)groups[1];
        Assert.AreEqual("Gaming", gamingGroup.Name);
        Assert.AreEqual("tech", techGroup.Name);

        var gamingArticles = gamingGroup.Items.Cast<ArticleRowViewModel>().ToArray();
        Assert.AreEqual("The next generation of handheld gaming is here", gamingArticles[0].Title);
        Assert.AreEqual("A new chapter for the world of Hyrule", gamingArticles[1].Title);
    }

    [TestMethod]
    public void DateSortShowsAFlatGlobalNewestFirstList()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        viewModel.NavigateCommand.Execute(viewModel.FeedLinks.Single(link => link.Route == "All"));

        viewModel.IsSortByDate = true;

        Assert.IsTrue(viewModel.IsSortByDate);
        Assert.IsFalse(viewModel.IsSortByFolder);
        Assert.IsNull(viewModel.ArticleListView.Groups);
        Assert.AreEqual(1, viewModel.ArticleListView.SortDescriptions.Count);
        Assert.AreEqual(
            nameof(ArticleRowViewModel.PublishedAt),
            viewModel.ArticleListView.SortDescriptions[0].PropertyName);
        var articles = viewModel.ArticleListView.Cast<ArticleRowViewModel>().ToArray();
        Assert.AreEqual(5, articles.Length);
        for (var index = 1; index < articles.Length; index++)
        {
            Assert.IsTrue(articles[index - 1].PublishedAt >= articles[index].PublishedAt);
        }
    }

    [TestMethod]
    public void UnreadAndSavedFiltersCombineWithFolderAndSearchRoutes()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        viewModel.NavigateCommand.Execute(viewModel.FeedLinks[1]);
        viewModel.SavedOnly = true;
        Assert.AreEqual(0, viewModel.VisibleArticles.Count);

        viewModel.SavedOnly = false;
        viewModel.UnreadOnly = true;
        Assert.AreEqual(2, viewModel.VisibleArticles.Count);

        viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Search"));
        viewModel.SearchQuery = "handheld";
        Assert.AreEqual(1, viewModel.VisibleArticles.Count);
        viewModel.SavedOnly = true;
        Assert.AreEqual(0, viewModel.VisibleArticles.Count);
    }

    [TestMethod]
    public void CatalogMasterStartsInCatalogManagementMode()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateCatalogMaster());

        Assert.IsTrue(viewModel.IsCatalogAdminVisible);
        Assert.AreEqual(1, viewModel.AdminLinks.Count);
        Assert.AreEqual("Manage catalog", viewModel.WorkspaceTitle);
    }

    [TestMethod]
    public async Task CatalogMasterCanFollowAndRefreshFeedsWithoutLeavingTheProfile()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"rss-reader-app-{Guid.NewGuid():N}.db");
        try
        {
            var profileStore = new SqliteProfileStore(databasePath);
            var catalogStore = new SqliteCatalogStore(databasePath);
            var readerStore = new SqliteReaderStore(databasePath);
            await profileStore.InitializeAsync();
            await catalogStore.InitializeAsync();
            await readerStore.InitializeAsync();

            var master = Profile.CreateCatalogMaster();
            var feed = new CatalogFeed(
                "catalog-master-test-feed",
                "Test feed",
                "https://example.com/test.xml",
                null,
                null);
            await catalogStore.AddFeedAsync(feed);
            await readerStore.AddFolderAsync(master.Id, "Testing");

            var downloader = new RecordingFeedDownloader();
            var readingService = new ReadingService(readerStore, catalogStore);
            var viewModel = new MainWindowViewModel(
                master,
                new CatalogService(catalogStore),
                readingService,
                new FeedRefreshService(readerStore, catalogStore, downloader),
                new ProfilePreferences(RefreshFeedsWhenOpened: false),
                profileFeedService: new ProfileFeedService(catalogStore, readerStore));
            await viewModel.InitializeAsync();

            Assert.IsTrue(viewModel.IsCatalogAdminVisible);
            Assert.IsTrue(viewModel.IsPersonalFeedManagementVisible);
            Assert.IsTrue(viewModel.RefreshCommand.CanExecute(null));
            viewModel.FolderSelectionRequested = (_, _, _) => Task.FromResult<string?>("Testing");
            await viewModel.ToggleSubscriptionCommand.ExecuteAsync(
                viewModel.CatalogFeeds.Single(candidate => candidate.Id == feed.Id));

            viewModel.NavigateCommand.Execute(
                viewModel.FeedLinks.Single(link => link.Route == $"feed:{feed.Id}"));
            await WaitForRefreshCompletionAsync(viewModel);

            Assert.AreEqual("Test feed", viewModel.WorkspaceTitle);
            Assert.AreEqual(1, downloader.RequestedFeedIds.Count);
            Assert.AreEqual(feed.Id, downloader.RequestedFeedIds.Single());
            Assert.AreEqual("New headline", viewModel.VisibleArticles.Single().Title);
            Assert.IsTrue(viewModel.RefreshCommand.CanExecute(null));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public void SidebarTogglePinsAtTheLeftOrCollapsesToAnOverlayTrigger()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        Assert.IsTrue(viewModel.IsSidebarPinned);
        Assert.AreEqual(286, viewModel.SidebarColumnWidth.Value);
        Assert.AreEqual(1, viewModel.SidebarColumnSpan);

        viewModel.ToggleSidebarCommand.Execute(null);

        Assert.IsFalse(viewModel.IsSidebarPinned);
        Assert.AreEqual(0, viewModel.SidebarColumnWidth.Value);
        Assert.AreEqual(2, viewModel.SidebarColumnSpan);

        viewModel.ToggleSidebarCommand.Execute(null);

        Assert.IsTrue(viewModel.IsSidebarPinned);
        Assert.AreEqual(286, viewModel.SidebarColumnWidth.Value);
        Assert.AreEqual(1, viewModel.SidebarColumnSpan);
    }

    [TestMethod]
    public void SidebarWidthIsConstrainedAndRetainedWhenSidebarIsUnpinned()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        viewModel.SetSidebarPanelWidth(350);
        Assert.AreEqual(350, viewModel.SidebarPanelWidth);
        Assert.AreEqual(350, viewModel.SidebarColumnWidth.Value);

        viewModel.SetSidebarPanelWidth(100);
        Assert.AreEqual(MainWindowViewModel.MinimumSidebarWidth, viewModel.SidebarPanelWidth);
        viewModel.SetSidebarPanelWidth(600);
        Assert.AreEqual(MainWindowViewModel.MaximumSidebarWidth, viewModel.SidebarPanelWidth);
        viewModel.SetSidebarPanelWidth(double.NaN);
        Assert.AreEqual(MainWindowViewModel.MaximumSidebarWidth, viewModel.SidebarPanelWidth);

        viewModel.ToggleSidebarCommand.Execute(null);
        Assert.AreEqual(0, viewModel.SidebarColumnWidth.Value);
        Assert.AreEqual(0, viewModel.SidebarColumnMinWidth);
        Assert.AreEqual(MainWindowViewModel.MaximumSidebarWidth, viewModel.SidebarPanelWidth);

        viewModel.ToggleSidebarCommand.Execute(null);
        Assert.AreEqual(MainWindowViewModel.MaximumSidebarWidth, viewModel.SidebarColumnWidth.Value);
        Assert.AreEqual(MainWindowViewModel.MinimumSidebarWidth, viewModel.SidebarColumnMinWidth);
    }

    [TestMethod]
    public void FormatAgeUsesRelativeTimeForRecentItems()
    {
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        var age = ArticleRowViewModel.FormatAge(now.AddHours(-3), now);

        Assert.AreEqual("3h ago", age);
    }

    [TestMethod]
    public void SmoothScrollOffsetUsesEaseOutInterpolation()
    {
        Assert.AreEqual(20, RssReader.App.SmoothScrollBehavior.InterpolateOffset(20, 100, 0));
        var middleOffset = RssReader.App.SmoothScrollBehavior.InterpolateOffset(20, 100, 0.5);
        Assert.IsTrue(middleOffset > 60);
        Assert.IsTrue(middleOffset < 100);
        Assert.AreEqual(100, RssReader.App.SmoothScrollBehavior.InterpolateOffset(20, 100, 1));
    }

    [TestMethod]
    public void SmoothScrollAccelerationOnlyAppliesToFastSameDirectionInput()
    {
        Assert.AreEqual(1, RssReader.App.SmoothScrollBehavior.GetWheelAcceleration(200, 1, 1));
        Assert.AreEqual(1, RssReader.App.SmoothScrollBehavior.GetWheelAcceleration(20, 1, -1));

        var fastAcceleration = RssReader.App.SmoothScrollBehavior.GetWheelAcceleration(40, 1, 1);

        Assert.IsTrue(fastAcceleration > 1);
        Assert.IsTrue(fastAcceleration <= 2);
    }

    [TestMethod]
    public void OptionalUriConverterIgnoresMissingLinksAndConvertsWebLinks()
    {
        var converter = new RssReader.App.OptionalUriConverter();

        Assert.AreSame(
            DependencyProperty.UnsetValue,
            converter.Convert(null!, typeof(Uri), null!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual(
            new Uri("https://example.com"),
            converter.Convert("https://example.com", typeof(Uri), null!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ArticleRowMakesSourceSummaryClickableAndExposesEmbeddedImage()
    {
        var article = new ArticleRowViewModel(
            "9 Chickweed Lane",
            "Arcamax",
            DateTimeOffset.UtcNow,
            "Comics",
            [],
            "Source",
            articleId: "article-1",
            feedId: "feed-1",
            link: "https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031",
            imageUrl: "https://resources.arcamax.com/newspics/396/39604/3960483.gif",
            websiteUrl: "https://www.arcamax.com");

        Assert.IsFalse(article.HasReadableSummary);
        Assert.IsTrue(article.IsSourceLinkVisible);
        Assert.IsTrue(article.IsWebsiteLinkVisible);
        Assert.AreEqual("https://www.arcamax.com", article.WebsiteUrl);
        Assert.IsTrue(article.IsImageVisible);
    }

    [TestMethod]
    public void ArticleCardUsesFirstEmbeddedImageWhenThumbnailIsMissing()
    {
        const string content = """
            <p>Story text.</p>
            <img src="../images/hero.jpg" alt="Hero">
            <img src="https://cdn.example.com/second.jpg">
            """;
        const string firstImageUrl = "https://news.example.com/images/hero.jpg";
        const string thumbnailUrl = "https://cdn.example.com/thumbnail.jpg";

        var articleWithoutThumbnail = new ArticleRowViewModel(
            "Example story",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "Story summary",
            link: "https://news.example.com/articles/story",
            content: content,
            feedUrl: "https://news.example.com/feed.xml");
        var articleWithThumbnail = new ArticleRowViewModel(
            "Another story",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "Story summary",
            link: "https://news.example.com/articles/story",
            content: content,
            imageUrl: thumbnailUrl);

        Assert.IsNull(articleWithoutThumbnail.ImageUrl);
        Assert.AreEqual(firstImageUrl, articleWithoutThumbnail.CardImageUrl);
        Assert.AreEqual(thumbnailUrl, articleWithThumbnail.CardImageUrl);
    }

    [TestMethod]
    public void ArticleCardDoesNotUseNonWebEmbeddedImages()
    {
        var article = new ArticleRowViewModel(
            "Example story",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "Story summary",
            link: "https://news.example.com/articles/story",
            content: """
                <img src="javascript:alert(1)">
                <img src="data:image/png;base64,AAAA">
                """);

        Assert.IsNull(article.CardImageUrl);
    }

    [TestMethod]
    public void ArticleRowBuildsSourceInitialsForImageFallback()
    {
        var article = new ArticleRowViewModel(
            "Esports headline",
            "Esports Insider RSS Feed",
            DateTimeOffset.UtcNow,
            "Esports",
            [],
            "Article summary");

        Assert.AreEqual("EI", article.SourceInitials);
    }

    [TestMethod]
    public void ArticleRowHidesExcerptWhenFullArticleContentIsAvailable()
    {
        var article = new ArticleRowViewModel(
            "Article",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "Short excerpt [...]",
            content: "Complete article body.");

        Assert.IsFalse(article.HasReadableSummary);
    }

    [TestMethod]
    public void ArticleHtmlSanitizerKeepsCommonTagsAndRemovesActiveContent()
    {
        var sanitized = RssReader.App.ArticleHtmlSanitizer.SanitizeFragment(
            "<p>Read <strong>this</strong><img src='/images/panel.jpg' onerror='alert(1)' /></p>" +
            "<img src='file:///private/image.jpg'><img src='data:image/png;base64,AAAA'>" +
            "<script>alert(2)</script><a href='javascript:alert(3)'>unsafe link</a>" +
            "<a href='file:///private/page.html'>local link</a>",
            "https://example.test/story");

        StringAssert.Contains(sanitized, "<p>");
        StringAssert.Contains(sanitized, "<strong>");
        StringAssert.Contains(sanitized, "<img");
        StringAssert.Contains(sanitized, "https://example.test/images/panel.jpg");
        Assert.IsFalse(sanitized.Contains("script", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("onerror", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("javascript:", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("file:", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("data:", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderAllowsOnlyItsHashedReaderProgressScript()
    {
        var document = ArticleHtmlDocumentBuilder.Build(
            "<script>alert('feed script')</script><p>Safe article text.</p>",
            null,
            "https://example.test/story",
            null);

        StringAssert.Contains(document, "script-src 'sha256-");
        StringAssert.Contains(document, "window.chrome.webview.postMessage");
        Assert.AreEqual(1, document.Split("<script", StringSplitOptions.None).Length - 1);
        Assert.IsFalse(document.Contains("alert('feed script')", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderAppliesReaderThemeAndTypographyAfterSanitizingFeedContent()
    {
        var document = ArticleHtmlDocumentBuilder.Build(
            "<p style=\"color: red\"><a href=\"https://example.com\">Article link</a></p>",
            null,
            null,
            null,
            readerTheme: ProfileReaderTheme.Dark,
            textSize: ProfileReaderTextSize.Large,
            lineSpacing: ProfileReaderLineSpacing.Relaxed,
            fontFamily: ProfileReaderFontFamily.Serif);

        StringAssert.Contains(document, "color-scheme: dark");
        StringAssert.Contains(document, "--reader-background: #202326");
        StringAssert.Contains(document, "font: 18px/1.85");
        StringAssert.Contains(document, "\"Georgia\", \"Times New Roman\", serif");
        StringAssert.Contains(document, "color: var(--reader-link) !important");
        Assert.IsFalse(document.Contains("color: red", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderAppliesXlargeTextAndExtraCompactLineSpacing()
    {
        var document = ArticleHtmlDocumentBuilder.Build(
            "<p>Article content.</p>",
            null,
            null,
            null,
            textSize: ProfileReaderTextSize.XLarge,
            lineSpacing: ProfileReaderLineSpacing.ExtraCompact);

        StringAssert.Contains(document, "font: 20px/1.3");
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderLimitsArticleWidthByDefaultAndCanUseAvailableWidth()
    {
        var limitedDocument = ArticleHtmlDocumentBuilder.Build("<p>Article content.</p>", null, null, null);
        StringAssert.Contains(
            limitedDocument,
            "main { max-width: 900px; margin: 0 auto; }");

        var fullWidthDocument = ArticleHtmlDocumentBuilder.Build(
            "<p>Wide article content.</p>",
            null,
            null,
            null,
            limitArticleWidth: false);
        StringAssert.Contains(fullWidthDocument, "main { box-sizing: border-box; width: 100%; padding: 12px 12px 24px; }");
        StringAssert.Contains(fullWidthDocument, "main { max-width: none; margin: 0; }");
        Assert.IsFalse(fullWidthDocument.Contains("max-width: 900px", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderRetainsAllImagesFromArmaghEquinoxArticle()
    {
        const string sunImage = "https://armaghplanet.com/wp-content/uploads/2011/03/Image-of-the-Sun.jpg";
        const string earthImage = "https://science.nasa.gov/wp-content/uploads/2023/05/goes16-vernalequinox-flickr50209599563-99acbeb180-b.jpg?w=1920";
        const string sunriseImage = "https://www.nasa.gov/wp-content/uploads/2023/03/582752main_sunrise_from_iss-full_full.jpg";
        var content = $"<img src=\"{sunImage}\" srcset=\"{sunImage} 580w\" />" +
            $"<div><img src=\"{earthImage}\" /></div><img src=\"{sunriseImage}\" />";

        var document = ArticleHtmlDocumentBuilder.Build(
            content,
            "Article summary",
            "https://armaghplanet.com/the-equinox-is-coming-what-on-earth-is-going-on.html",
            null);

        StringAssert.Contains(document, sunImage);
        StringAssert.Contains(document, earthImage);
        StringAssert.Contains(document, sunriseImage);
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderRestoresLazyAndResponsiveImageSourcesBeforeSanitizing()
    {
        const string articleUrl = "https://news.example.com/articles/story";
        var document = ArticleHtmlDocumentBuilder.Build(
            """
            <div><img src="data:image/gif;base64,AAAA" data-src="../images/lazy.jpg" alt="Lazy image"></div>
            <img srcset="/images/small.jpg 480w, /images/large.jpg 1200w" alt="Responsive image">
            """,
            "Article summary",
            articleUrl,
            null);

        StringAssert.Contains(document, "https://news.example.com/images/lazy.jpg");
        StringAssert.Contains(document, "https://news.example.com/images/large.jpg");
        Assert.IsFalse(document.Contains("data:image/", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(document.Contains("data-src=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(document.Contains("srcset=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderRestoresPolygonImageSourceBeforeSanitizing()
    {
        const string imageUrl =
            "https://static0.polygonimages.com/wordpress/wp-content/uploads/2026/09/vs-vimeo-itsthegreatpumpkincharliebrownvideosmoviesonvimeo-4-24.jpg?q=70&fit=crop&w=640&dpr=1";
        var document = ArticleHtmlDocumentBuilder.Build(
            """
            <figure><picture>
              <source media="(max-width: 767px)" srcset="https://static0.polygonimages.com/wordpress/wp-content/uploads/2026/09/vs-vimeo-itsthegreatpumpkincharliebrownvideosmoviesonvimeo-4-24.jpg?q=70&amp;fit=crop&amp;w=640&amp;dpr=1">
              <img width="640" height="480" loading="lazy" decoding="async" alt="Linus Great Pumpkin Charlie Brown" data-img-url="https://static0.polygonimages.com/wordpress/wp-content/uploads/2026/09/vs-vimeo-itsthegreatpumpkincharliebrownvideosmoviesonvimeo-4-24.jpg?q=70&amp;fit=crop&amp;w=640&amp;dpr=1" src="https://static0.polygonimages.com/wordpress/wp-content/uploads/2026/09/vs-vimeo-itsthegreatpumpkincharliebrownvideosmoviesonvimeo-4-24.jpg?q=70&amp;fit=crop&amp;w=640&amp;dpr=1" class="img-brightness-opt-out">
            </picture><figcaption>Image: Charles Schulz</figcaption></figure>
            """,
            null,
            "https://www.polygon.com/great-pumpkin-charlie-brown-best-quote-movie/",
            null);

        StringAssert.Contains(document, imageUrl.Replace("&", "&amp;", StringComparison.Ordinal));
        StringAssert.Contains(document, "<picture>");
        Assert.IsFalse(document.Contains("srcset=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(document.Contains("data-img-url=", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, document.Split("<img", StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderRemovesDuplicateFeaturedImageWhenInlineImageUsesResizedSource()
    {
        const string fullImage = "https://armaghplanet.com/wp-content/uploads/2026/09/Christmas-Card-Competition.jpeg";
        const string resizedImage = "https://armaghplanet.com/wp-content/uploads/2026/09/Christmas-Card-Competition-300x209.jpeg";
        var content = $"<img src=\"{fullImage}\" srcset=\"{resizedImage} 300w, {fullImage} 2048w\" />" +
            $"<p>Article text<a href=\"{fullImage}\"><img src=\"{resizedImage}\" srcset=\"{fullImage} 2048w\" /></a></p>";

        var document = ArticleHtmlDocumentBuilder.Build(
            content,
            null,
            "https://armaghplanet.com/article",
            null);

        Assert.AreEqual(1, document.Split("<img", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(document, resizedImage);
        Assert.IsFalse(document.Contains($"src=\"{fullImage}\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderCentersImagesMarkedAlignCenterInTheFeed()
    {
        const string imageUrl = "https://armaghplanet.com/wp-content/uploads/2026/09/Christmas-Card-Competition-300x209.jpeg";
        var content = $"<p>First sentence. <a href=\"https://armaghplanet.com/photo\"><img src=\"{imageUrl}\" class=\"size-medium wp-image-15550 aligncenter\" /></a>Second sentence.</p>";

        var document = ArticleHtmlDocumentBuilder.Build(
            content,
            null,
            "https://armaghplanet.com/article",
            null);

        StringAssert.Contains(document, "img.reader-centered-image { display: block; margin: 16px auto; }");
        StringAssert.Contains(document, "a.reader-centered-image-link { clear: both; display: block; text-align: center; }");
        StringAssert.Contains(document, "class=\"reader-centered-image-link\"");
        StringAssert.Contains(document, "class=\"reader-centered-image\"");
        Assert.IsFalse(document.Contains("aligncenter", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(document.Contains("wp-image-15550", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderUsesSummaryWithoutAddingFeedImageWhenBodyIsMissing()
    {
        var document = ArticleHtmlDocumentBuilder.Build(
            null,
            "A useful summary & detail.",
            null,
            null);

        StringAssert.Contains(document, "A useful summary &amp; detail.");
        Assert.IsFalse(document.Contains("<img", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(document.Contains("https://example.test/images/lead.jpg", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderPresentsSummaryOnlyContentAsFeedPreview()
    {
        const string summary =
            "AI should reduce the total amount of work, not transfer the burden of checking and correcting it to someone else.";
        const string articleUrl = "https://www.inc.com/scott-hutcheson/article/91415291";
        const string imageUrl = "https://img-cdn.inc.com/images/article.jpg";

        var document = ArticleHtmlDocumentBuilder.Build(
            $"<p>{summary}</p>",
            summary,
            articleUrl,
            null,
            imageUrl: imageUrl);

        StringAssert.Contains(document, "class=\"reader-feed-preview\"");
        StringAssert.Contains(document, "class=\"reader-feed-preview-image\"");
        StringAssert.Contains(document, imageUrl);
        Assert.IsTrue(
            document.IndexOf(imageUrl, StringComparison.Ordinal) <
            document.IndexOf(summary, StringComparison.Ordinal));
        StringAssert.Contains(document, "This feed provides a short preview.");
        StringAssert.Contains(document, $"href=\"{articleUrl}\"");
        StringAssert.Contains(document, "Open article on the publisher's site");
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderResolvesRelativeImagesAgainstFeedUrlWhenArticleLinkIsMissing()
    {
        var document = ArticleHtmlDocumentBuilder.Build(
            "<p><img src=\"/images/panel.jpg\" alt=\"Panel\"></p>",
            null,
            null,
            "https://example.test/rss/feed.xml");

        StringAssert.Contains(document, "https://example.test/images/panel.jpg");
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderPrependsThumbnailToFullContentOnlyWhenNoUsableInlineImageExists()
    {
        const string imageUrl = "https://example.test/images/thumbnail.jpg";
        var document = ArticleHtmlDocumentBuilder.Build(
            "<article><h2>Full article heading</h2><p>Full article body.</p></article>",
            "A short feed preview.",
            "https://example.test/story",
            null,
            imageUrl: "/images/thumbnail.jpg");

        var imageIndex = document.IndexOf(imageUrl, StringComparison.Ordinal);
        var textIndex = document.IndexOf("Full article heading", StringComparison.Ordinal);
        Assert.IsTrue(imageIndex >= 0 && imageIndex < textIndex);

        var inlineImageDocument = ArticleHtmlDocumentBuilder.Build(
            "<p>Full article body.</p><img src=\"https://example.test/images/body.jpg\" alt=\"Body\">",
            "A short feed preview.",
            "https://example.test/story",
            null,
            imageUrl: imageUrl);

        StringAssert.Contains(inlineImageDocument, "https://example.test/images/body.jpg");
        Assert.IsFalse(inlineImageDocument.Contains("<figure class=\"reader-leading-image\">", StringComparison.Ordinal));
        Assert.IsFalse(inlineImageDocument.Contains(imageUrl, StringComparison.Ordinal));

        var resizedImageDocument = ArticleHtmlDocumentBuilder.Build(
            "<p>Article body.</p><img src=\"https://example.test/images/thumbnail-300x200.jpg\" alt=\"Thumbnail\">",
            "A separate preview.",
            "https://example.test/story",
            null,
            imageUrl: imageUrl);
        Assert.AreEqual(1, resizedImageDocument.Split("<img", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(resizedImageDocument, "thumbnail-300x200.jpg");
        Assert.IsFalse(resizedImageDocument.Contains(imageUrl, StringComparison.Ordinal));

        var relativeThumbnailDocument = ArticleHtmlDocumentBuilder.Build(
            "<p>Another complete article body.</p>",
            "A separate preview.",
            null,
            "https://example.test/rss/feed.xml",
            imageUrl: "../images/relative-thumbnail.jpg");
        StringAssert.Contains(relativeThumbnailDocument, "https://example.test/images/relative-thumbnail.jpg");

        var unsupportedImageDocument = ArticleHtmlDocumentBuilder.Build(
            "<p>Text survives an unusable inline image.<img src=\"file:///private/picture.jpg\"></p>",
            "A separate preview.",
            "https://example.test/story",
            null,
            imageUrl: "file:///private/thumbnail.jpg");
        StringAssert.Contains(unsupportedImageDocument, "Text survives an unusable inline image.");
        Assert.IsFalse(unsupportedImageDocument.Contains("<img ", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task FullArticleActionLoadsExtractedContentAndCanReturnToCachedFeedPreview()
    {
        const string feedPreview = "A short feed preview.";
        const string extractedHtml = "<article><h2>Full story</h2><p>Publisher article body.</p></article>";
        var retrievedAt = DateTimeOffset.UtcNow;
        var service = new StubArticleContentService(
            (_, _) => Task.FromResult(new ExtractedArticleContent(
                extractedHtml,
                "Full story",
                "Publisher Writer",
                retrievedAt)));
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            articleContentService: service);
        var article = new ArticleRowViewModel(
            "Feed title",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            feedPreview,
            articleId: "article-1",
            link: "https://example.test/story",
            content: $"<p>{feedPreview}</p>");
        viewModel.VisibleArticles.Add(article);
        viewModel.OpenArticleInReaderCommand.Execute(article);

        Assert.IsTrue(viewModel.IsSelectedArticlePreviewCueVisible);
        Assert.IsTrue(viewModel.IsSelectedArticleFullArticleActionVisible);
        Assert.AreEqual(0, service.RequestCount);
        await viewModel.LoadFullArticleCommand.ExecuteAsync();

        Assert.AreEqual(1, service.RequestCount);
        Assert.AreEqual(extractedHtml, article.ReaderContent);
        Assert.AreEqual(retrievedAt, article.ExtractedContentRetrievedAt);
        Assert.IsTrue(viewModel.IsFeedPreviewActionVisible);
        Assert.IsFalse(viewModel.IsSelectedArticlePreviewCueVisible);

        viewModel.ShowFeedPreviewCommand.Execute(null);
        Assert.AreEqual($"<p>{feedPreview}</p>", article.ReaderContent);
        Assert.IsTrue(viewModel.IsSelectedArticlePreviewCueVisible);
        await viewModel.LoadFullArticleCommand.ExecuteAsync();
        Assert.AreEqual(1, service.RequestCount);
        Assert.AreEqual(extractedHtml, article.ReaderContent);
    }

    [TestMethod]
    public async Task FullArticleActionRefreshesAlreadyDisplayedExtractedContent()
    {
        const string oldHtml = "<article><p>Previously cached article content.</p></article>";
        const string refreshedHtml = "<article><p>Updated article content.</p></article>";
        var retrievedAt = DateTimeOffset.UtcNow;
        var service = new StubArticleContentService(
            (_, _) => Task.FromResult(new ExtractedArticleContent(
                refreshedHtml,
                "Updated story",
                null,
                retrievedAt)));
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            articleContentService: service);
        var article = new ArticleRowViewModel(
            "Feed title",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "A short feed preview.",
            link: "https://example.test/story",
            content: "<p>A short feed preview.</p>",
            extractedContentHtml: oldHtml,
            extractedContentRetrievedAt: DateTimeOffset.UtcNow.AddDays(-1));
        viewModel.VisibleArticles.Add(article);
        viewModel.OpenArticleInReaderCommand.Execute(article);

        Assert.IsTrue(viewModel.IsSelectedArticleFullArticleActionVisible);
        Assert.AreEqual("Refresh full article", viewModel.FullArticleActionLabel);
        Assert.AreEqual(oldHtml, article.ReaderContent);

        await viewModel.LoadFullArticleCommand.ExecuteAsync();

        Assert.AreEqual(1, service.RequestCount);
        Assert.AreEqual(refreshedHtml, article.ReaderContent);
        Assert.AreEqual(retrievedAt, article.ExtractedContentRetrievedAt);
    }

    [TestMethod]
    public void AutoLoadUsesFreshCachedArticleWithoutFetchingAgain()
    {
        var service = new StubArticleContentService((_, _) =>
            Task.FromResult(new ExtractedArticleContent(
                "<article><p>Unexpected refetch.</p></article>",
                "Fresh story",
                null,
                DateTimeOffset.UtcNow)));
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            profilePreferences: new ProfilePreferences(AutoLoadFullArticle: true),
            articleContentService: service);
        var cachedHtml = "<article><p>Fresh cached article.</p></article>";
        var article = new ArticleRowViewModel(
            "Cached story",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "Feed preview.",
            articleId: "cached-article",
            link: "https://example.test/cached-story",
            content: "<p>Feed preview.</p>",
            extractedContentHtml: cachedHtml,
            extractedContentRetrievedAt: DateTimeOffset.UtcNow.AddDays(-1));

        viewModel.OpenArticleInReaderCommand.Execute(article);

        Assert.AreEqual(0, service.RequestCount);
        Assert.AreEqual(cachedHtml, article.ReaderContent);
        Assert.IsFalse(viewModel.IsLoadingArticleContent);
    }

    [TestMethod]
    public async Task AutoLoadShowsStaleCacheWhileRefreshingIt()
    {
        const string staleHtml = "<article><p>Previously cached story.</p></article>";
        const string refreshedHtml = "<article><p>Freshly refreshed story.</p></article>";
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<ExtractedArticleContent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubArticleContentService((_, _) =>
        {
            requestStarted.TrySetResult();
            return response.Task;
        });
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            profilePreferences: new ProfilePreferences(AutoLoadFullArticle: true),
            articleContentService: service);
        var article = new ArticleRowViewModel(
            "Cached story",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "Feed preview.",
            articleId: "stale-article",
            link: "https://example.test/stale-story",
            content: "<p>Feed preview.</p>",
            extractedContentHtml: staleHtml,
            extractedContentRetrievedAt: DateTimeOffset.UtcNow.AddDays(-8));

        viewModel.OpenArticleInReaderCommand.Execute(article);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(staleHtml, article.ReaderContent);
        Assert.IsTrue(article.IsShowingStaleExtractedContent);
        Assert.IsFalse(article.IsWaitingForFullArticle);
        Assert.IsTrue(viewModel.IsLoadingArticleContent);

        response.SetResult(new ExtractedArticleContent(
            refreshedHtml,
            "Refreshed story",
            null,
            DateTimeOffset.UtcNow));
        await WaitForArticleContentLoadAsync(viewModel);

        Assert.AreEqual(refreshedHtml, article.ReaderContent);
        Assert.IsFalse(article.IsShowingStaleExtractedContent);
    }

    [TestMethod]
    public async Task AutoLoadFullArticleHidesPreviewUntilPublisherContentLoads()
    {
        const string extractedHtml = "<article><p>Automatically loaded publisher text.</p></article>";
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<ExtractedArticleContent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubArticleContentService((_, _) =>
        {
            requestStarted.TrySetResult();
            return response.Task;
        });
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            profilePreferences: new ProfilePreferences(AutoLoadFullArticle: true),
            articleContentService: service);
        var article = new ArticleRowViewModel(
            "Feed title",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "A short feed preview.",
            link: "https://example.test/story",
            content: "<p>A short feed preview.</p>");

        viewModel.OpenArticleInReaderCommand.Execute(article);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, service.RequestCount);
        Assert.IsTrue(viewModel.IsLoadingArticleContent);
        Assert.IsTrue(article.IsWaitingForFullArticle);
        Assert.IsNull(article.ReaderContent);

        response.SetResult(new ExtractedArticleContent(
            extractedHtml,
            "Full story",
            null,
            DateTimeOffset.UtcNow));
        await WaitForArticleContentLoadAsync(viewModel);

        Assert.AreEqual(extractedHtml, article.ReaderContent);
        Assert.IsFalse(article.IsWaitingForFullArticle);
        Assert.IsTrue(viewModel.IsFeedPreviewActionVisible);
    }

    [TestMethod]
    public async Task AutoLoadFullArticleShowsPreviewAfterPublisherContentFails()
    {
        const string feedPreview = "<p>A short feed preview.</p>";
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<ExtractedArticleContent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubArticleContentService((_, _) =>
        {
            requestStarted.TrySetResult();
            return response.Task;
        });
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            profilePreferences: new ProfilePreferences(AutoLoadFullArticle: true),
            articleContentService: service);
        var article = new ArticleRowViewModel(
            "Feed title",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "A short feed preview.",
            link: "https://example.test/story",
            content: feedPreview);

        viewModel.OpenArticleInReaderCommand.Execute(article);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(article.IsWaitingForFullArticle);
        Assert.IsNull(article.ReaderContent);

        response.SetException(new ArticleContentExtractionException("The publisher page could not be loaded."));
        await WaitForArticleContentLoadAsync(viewModel);

        Assert.IsFalse(article.IsWaitingForFullArticle);
        Assert.AreEqual(feedPreview, article.ReaderContent);
        Assert.AreEqual("The publisher page could not be loaded.", viewModel.ArticleContentError);
    }

    [TestMethod]
    public async Task FullArticleFailureRetainsPreviewAndDisplaysRecoverableError()
    {
        const string feedPreview = "The original feed content remains available.";
        var service = new StubArticleContentService(
            (_, _) => Task.FromException<ExtractedArticleContent>(
                new ArticleContentExtractionException("The publisher requires a browser challenge.")));
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            articleContentService: service);
        var article = new ArticleRowViewModel(
            "Feed title",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            feedPreview,
            link: "https://example.test/story",
            content: $"<p>{feedPreview}</p>");
        viewModel.VisibleArticles.Add(article);
        viewModel.OpenArticleInReaderCommand.Execute(article);

        await viewModel.LoadFullArticleCommand.ExecuteAsync();

        Assert.AreEqual($"<p>{feedPreview}</p>", article.ReaderContent);
        Assert.AreEqual("The publisher requires a browser challenge.", viewModel.ArticleContentError);
        Assert.AreEqual("Retry full article", viewModel.FullArticleActionLabel);
        Assert.IsTrue(viewModel.IsSelectedArticleFullArticleActionVisible);
        Assert.IsTrue(viewModel.IsOpenArticleInBrowserVisible);
        Assert.IsFalse(viewModel.IsOpenInAppPublisherBrowserVisible);

        viewModel.ShowFeedPreviewCommand.Execute(null);
        Assert.IsFalse(viewModel.IsOpenArticleInBrowserVisible);
    }

    [TestMethod]
    public async Task ForbiddenFullArticleFailureOffersInAppBrowserAndCanReturnToReader()
    {
        var service = new StubArticleContentService(
            (_, _) => Task.FromException<ExtractedArticleContent>(
                new ArticleContentExtractionException(
                    "The publisher denied the request (HTTP 403 Forbidden).",
                    statusCode: HttpStatusCode.Forbidden,
                    isCloudflareResponse: true)));
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            articleContentService: service);
        var article = new ArticleRowViewModel(
            "Feed title",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "A short feed preview.",
            link: "https://example.test/story",
            content: "<p>A short feed preview.</p>");
        viewModel.VisibleArticles.Add(article);
        viewModel.OpenArticleInReaderCommand.Execute(article);

        await viewModel.LoadFullArticleCommand.ExecuteAsync();

        Assert.IsTrue(viewModel.IsOpenArticleInBrowserVisible);
        Assert.IsTrue(viewModel.IsOpenInAppPublisherBrowserVisible);
        Assert.IsTrue(viewModel.OpenInAppPublisherBrowserCommand.CanExecute(null));
        Assert.IsFalse(viewModel.IsInAppPublisherBrowserVisible);

        viewModel.OpenInAppPublisherBrowserCommand.Execute(null);

        Assert.IsTrue(viewModel.IsInAppPublisherBrowserVisible);
        Assert.IsFalse(viewModel.IsOpenInAppPublisherBrowserVisible);
        Assert.IsTrue(viewModel.ReturnToArticleReaderCommand.CanExecute(null));

        viewModel.ReturnToArticleReaderCommand.Execute(null);

        Assert.IsFalse(viewModel.IsInAppPublisherBrowserVisible);
        Assert.IsTrue(viewModel.IsOpenInAppPublisherBrowserVisible);
    }

    [TestMethod]
    public async Task ChangingSelectionCancelsInFlightFullArticleLoad()
    {
        var requestStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubArticleContentService(
            async (_, cancellationToken) =>
            {
                requestStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Cancelled request unexpectedly completed.");
            });
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            articleContentService: service);
        var article = new ArticleRowViewModel(
            "First article",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "First preview.",
            link: "https://example.test/first",
            content: "<p>First preview.</p>");
        var nextArticle = new ArticleRowViewModel(
            "Next article",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "Next preview.",
            link: "https://example.test/next",
            content: "<p>Next preview.</p>");
        viewModel.VisibleArticles.Add(article);
        viewModel.VisibleArticles.Add(nextArticle);
        viewModel.OpenArticleInReaderCommand.Execute(article);

        var loadTask = viewModel.LoadFullArticleCommand.ExecuteAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.OpenArticleInReaderCommand.Execute(nextArticle);
        await loadTask;

        Assert.AreSame(nextArticle, viewModel.SelectedArticle);
        Assert.IsFalse(viewModel.IsLoadingArticleContent);
        Assert.IsNull(viewModel.ArticleContentError);
        Assert.AreEqual("<p>First preview.</p>", article.ReaderContent);
    }

    [TestMethod]
    public async Task AutoLoadStartsTheNewSelectionAfterCancellingThePreviousArticleLoad()
    {
        var firstRequestStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRequestStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubArticleContentService(
            async (articleUri, cancellationToken) =>
            {
                if (articleUri.AbsolutePath == "/first")
                {
                    firstRequestStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("Cancelled request unexpectedly completed.");
                }

                secondRequestStarted.TrySetResult();
                return new ExtractedArticleContent(
                    "<p>Second article full text.</p>",
                    "Second article",
                    null,
                    DateTimeOffset.UtcNow);
            });
        var viewModel = new MainWindowViewModel(
            Profile.CreateRegular("Reader"),
            catalogService: null,
            readingService: null,
            feedRefreshService: null,
            profilePreferences: new ProfilePreferences(AutoLoadFullArticle: true),
            articleContentService: service);
        var firstArticle = new ArticleRowViewModel(
            "First article",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "First preview.",
            link: "https://example.test/first",
            content: "<p>First preview.</p>");
        var secondArticle = new ArticleRowViewModel(
            "Second article",
            "Example feed",
            DateTimeOffset.UtcNow,
            "News",
            [],
            "Second preview.",
            link: "https://example.test/second",
            content: "<p>Second preview.</p>");

        viewModel.OpenArticleInReaderCommand.Execute(firstArticle);
        await firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.OpenArticleInReaderCommand.Execute(secondArticle);
        await secondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForArticleContentLoadAsync(viewModel);

        Assert.AreSame(secondArticle, viewModel.SelectedArticle);
        Assert.AreEqual(2, service.RequestCount);
        Assert.AreEqual("<p>Second article full text.</p>", secondArticle.ReaderContent);
        Assert.IsFalse(secondArticle.IsWaitingForFullArticle);
    }

    [TestMethod]
    public void MainWindowSelectionPassesBodyHtmlWithoutFeedImageToArticleViewer()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            try
            {
                var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                var window = CreateTestMainWindow(viewModel);
                var article = new ArticleRowViewModel(
                    "Article",
                    "Example feed",
                    DateTimeOffset.UtcNow,
                    "Comics",
                    [],
                    "Summary",
                    feedId: "comic-feed",
                    link: "https://example.test/story",
                    content: "<p>Body <img src=\"https://example.test/inline.png\" /></p>",
                    imageUrl: "https://example.test/feed-image.png");

                viewModel.SelectArticleCommand.Execute(article);

                var articleViewer = FindReaderElement<RssReader.App.ArticleHtmlViewer>(window, "SelectedArticleHtmlViewer");
                StringAssert.Contains(articleViewer.CurrentDocument, "https://example.test/inline.png");
                Assert.IsFalse(articleViewer.CurrentDocument.Contains("https://example.test/feed-image.png", StringComparison.Ordinal));
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void ArticleReaderRefreshesWhenFullArticleIsLoadedAndPreviewIsRestored()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            try
            {
                const string summary = "A short feed preview.";
                var requestCount = 0;
                var service = new StubArticleContentService(
                    (_, _) =>
                    {
                        requestCount++;
                        var html = requestCount == 1
                            ? "<article><script>alert('unsafe')</script><h2>Extracted story</h2><p>Full publisher text.</p></article>"
                            : "<article><h2>Updated story</h2><p>Refreshed publisher text.</p></article>";
                        return Task.FromResult(new ExtractedArticleContent(
                            html,
                            "Extracted story",
                            "Publisher Writer",
                            DateTimeOffset.UtcNow));
                    });
                var viewModel = new MainWindowViewModel(
                    Profile.CreateRegular("Reader"),
                    catalogService: null,
                    readingService: null,
                    feedRefreshService: null,
                    articleContentService: service);
                var window = CreateTestMainWindow(viewModel);
                var article = new ArticleRowViewModel(
                    "Feed title",
                    "Example feed",
                    DateTimeOffset.UtcNow,
                    "News",
                    [],
                    summary,
                    link: "https://example.test/story",
                    content: $"<p>{summary}</p>");
                viewModel.SelectArticleCommand.Execute(article);

                var loadButton = FindReaderElement<Button>(window, "LoadFullArticleButton");
                var previewButton = FindReaderElement<Button>(window, "ShowFeedPreviewButton");
                Assert.IsFalse(viewModel.IsFeedPreviewActionVisible);
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, loadButton.Visibility);
                Assert.AreEqual(
                    Visibility.Collapsed,
                    previewButton.Visibility,
                    $"Preview action visibility did not follow the selected article state: {viewModel.SelectedArticle?.IsShowingExtractedContent}.");
                var articleViewer = FindReaderElement<RssReader.App.ArticleHtmlViewer>(
                    window,
                    "SelectedArticleHtmlViewer");
                StringAssert.Contains(articleViewer.CurrentDocument, summary);

                var loadTask = viewModel.LoadFullArticleCommand.ExecuteAsync();
                PumpDispatcherUntil(loadTask);
                StringAssert.Contains(articleViewer.CurrentDocument, "Full publisher text.");
                Assert.IsTrue(articleViewer.CurrentDocument.Contains("Extracted story", StringComparison.Ordinal));
                Assert.AreEqual(
                    1,
                    articleViewer.CurrentDocument.Split("<script", StringSplitOptions.None).Length - 1);
                Assert.IsFalse(articleViewer.CurrentDocument.Contains("alert('unsafe')", StringComparison.Ordinal));
                StringAssert.Contains(articleViewer.CurrentDocument, "script-src 'sha256-");
                Assert.AreEqual(Visibility.Visible, loadButton.Visibility);
                Assert.AreEqual("Refresh full article", viewModel.FullArticleActionLabel);
                Assert.AreEqual(Visibility.Visible, previewButton.Visibility);

                var refreshTask = viewModel.LoadFullArticleCommand.ExecuteAsync();
                PumpDispatcherUntil(refreshTask);
                StringAssert.Contains(articleViewer.CurrentDocument, "Refreshed publisher text.");
                Assert.IsFalse(articleViewer.CurrentDocument.Contains("Full publisher text.", StringComparison.Ordinal));
                Assert.AreEqual(2, requestCount);

                viewModel.ShowFeedPreviewCommand.Execute(null);
                StringAssert.Contains(articleViewer.CurrentDocument, summary);
                Assert.IsFalse(articleViewer.CurrentDocument.Contains("Full publisher text.", StringComparison.Ordinal));
                Assert.AreEqual(Visibility.Visible, loadButton.Visibility);
                Assert.AreEqual(Visibility.Collapsed, previewButton.Visibility);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void ArticleReaderOffersBrowserLinkAfterFullArticleRequestFails()
    {
        Exception? failure = null;
        WpfTestHost.Run(() =>
        {
            try
            {
                const string articleUrl = "https://example.test/story";
                var viewModel = new MainWindowViewModel(
                    Profile.CreateRegular("Reader"),
                    catalogService: null,
                    readingService: null,
                    feedRefreshService: null,
                    articleContentService: new StubArticleContentService(
                        (_, _) => Task.FromException<ExtractedArticleContent>(
                            new ArticleContentExtractionException(
                                "The publisher blocked the app's request (HTTP 403 Forbidden).",
                                statusCode: HttpStatusCode.Forbidden))));
                var window = CreateTestMainWindow(viewModel);
                var article = new ArticleRowViewModel(
                    "Feed title",
                    "Example feed",
                    DateTimeOffset.UtcNow,
                    "News",
                    [],
                    "A short feed preview.",
                    link: articleUrl,
                    content: "<p>A short feed preview.</p>");
                viewModel.SelectArticleCommand.Execute(article);

                var loadTask = viewModel.LoadFullArticleCommand.ExecuteAsync();
                PumpDispatcherUntil(loadTask);
                window.UpdateLayout();

                var browserLinkText = FindReaderElement<TextBlock>(
                    window,
                    "OpenArticleInBrowserLink");
                Assert.AreEqual(Visibility.Visible, browserLinkText.Visibility);
                var browserLink = browserLinkText.Inlines.OfType<Hyperlink>().Single();
                Assert.AreEqual(new Uri(articleUrl), browserLink.NavigateUri);
                var inAppBrowserLink = FindReaderElement<TextBlock>(
                    window,
                    "OpenArticleInAppBrowserLink");
                Assert.AreEqual(Visibility.Visible, inAppBrowserLink.Visibility);

                viewModel.ShowFeedPreviewCommand.Execute(null);
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, browserLinkText.Visibility);
                Assert.AreEqual(Visibility.Collapsed, inAppBrowserLink.Visibility);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void FolderDeleteConfirmationOpensAtTheFeedsSectionLocation()
    {
        WpfTestHost.Run(() =>
        {
            var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
            var owner = CreateTestMainWindow(viewModel);
            owner.Left = 40;
            owner.Top = 40;
            owner.Width = 1000;
            owner.Height = 700;
            owner.Show();
            owner.UpdateLayout();

            var sidebar = (SidebarView)owner.FindName("SidebarView")!;
            var folderName = viewModel.FeedLinks.First(link => link.IsFolder).Label;
            var feedsLabelPosition = sidebar.GetFeedsSectionLabelPosition(owner);
            var sidebarRightEdge = sidebar.GetSidebarPanelRightEdge(owner);
            var dialogLocation = viewModel.FolderDeleteDialogLocationProvider!.Invoke();
            Assert.AreEqual(sidebarRightEdge.X + 12, dialogLocation.X, 1);
            Assert.AreEqual(feedsLabelPosition.Y, dialogLocation.Y, 1);

            double? dialogLeft = null;
            double? dialogTop = null;
            var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var dialog = System.Windows.Application.Current.Windows.OfType<MessageDialogWindow>().Single();
                dialogLeft = dialog.Left;
                dialogTop = dialog.Top;
                ((Button)dialog.FindName("NoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();

            try
            {
                var result = viewModel.ConfirmDeleteFolderRequested!(
                    folderName,
                    13,
                    null).GetAwaiter().GetResult();

                Assert.IsFalse(result);
                var compositionTarget = PresentationSource.FromVisual(owner)!.CompositionTarget;
                var expectedScreenLocation = compositionTarget.TransformFromDevice.Transform(
                    owner.PointToScreen(dialogLocation));
                Assert.AreEqual(expectedScreenLocation.X, dialogLeft!.Value, 1);
                Assert.AreEqual(expectedScreenLocation.Y, dialogTop!.Value, 1);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [TestMethod]
    public void SingleFeedUnfollowConfirmationOpensAtTheFeedsSectionLocation()
    {
        WpfTestHost.Run(() =>
        {
            var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
            var owner = CreateTestMainWindow(viewModel);
            owner.Left = 40;
            owner.Top = 40;
            owner.Width = 1000;
            owner.Height = 700;
            owner.Show();
            owner.UpdateLayout();

            var dialogLocation = viewModel.FolderDeleteDialogLocationProvider!.Invoke();
            double? dialogLeft = null;
            double? dialogTop = null;
            var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var dialog = System.Windows.Application.Current.Windows.OfType<MessageDialogWindow>().Single();
                dialogLeft = dialog.Left;
                dialogTop = dialog.Top;
                ((Button)dialog.FindName("NoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();

            try
            {
                var result = viewModel.ConfirmUnfollowRequested!("Example feed").GetAwaiter().GetResult();

                Assert.IsFalse(result);
                var compositionTarget = PresentationSource.FromVisual(owner)!.CompositionTarget;
                var expectedScreenLocation = compositionTarget.TransformFromDevice.Transform(
                    owner.PointToScreen(dialogLocation));
                Assert.AreEqual(expectedScreenLocation.X, dialogLeft!.Value, 1);
                Assert.AreEqual(expectedScreenLocation.Y, dialogTop!.Value, 1);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    private static async Task WaitForRefreshCompletionAsync(MainWindowViewModel viewModel)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (viewModel.IsRefreshing && DateTimeOffset.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.IsFalse(viewModel.IsRefreshing, "Feed refresh did not complete in time.");
    }

    private static async Task WaitForArticleContentLoadAsync(MainWindowViewModel viewModel)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (viewModel.IsLoadingArticleContent && DateTimeOffset.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.IsFalse(viewModel.IsLoadingArticleContent, "Article content load did not complete in time.");
    }

    private static void PumpDispatcherUntil(Task task)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            frame.Continue = false;
        };
        timeout.Start();
        _ = task.ContinueWith(
            _ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
            TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        timeout.Stop();
        Assert.IsTrue(task.IsCompleted, "WebView2 initialization did not finish within 15 seconds.");
    }

    private sealed class RecordingFeedDownloader : IFeedDownloader, IRawFeedContentDownloader, IRawFeedXmlDownloader
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> RequestedFeedIds { get; } = new();

        public System.Collections.Concurrent.ConcurrentQueue<string> RequestedRawXmlFeedIds { get; } = new();

        public HashSet<string> FailingFeedIds { get; } = [];

        public string RawFeedXmlToReturn { get; set; } = "<rss />";

        public IReadOnlyList<DownloadedFeedItem>? ItemsToReturn { get; set; }

        public IReadOnlyDictionary<string, IReadOnlyList<DownloadedFeedItem>>? ItemsByFeed { get; set; }

        public Func<CancellationToken, Task<IReadOnlyList<DownloadedFeedItem>>>? DownloadHandler { get; set; }

        public async Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default)
        {
            RequestedFeedIds.Enqueue(feed.Id);
            if (FailingFeedIds.Contains(feed.Id))
            {
                throw new InvalidOperationException("The feed is unavailable.");
            }

            if (DownloadHandler is not null)
            {
                return await DownloadHandler(cancellationToken);
            }

            if (ItemsByFeed?.TryGetValue(feed.Id, out var feedItems) == true)
            {
                return feedItems;
            }

            return ItemsToReturn ??
            [
                new DownloadedFeedItem($"{feed.Id}-item", "New headline", null, null, "Summary", "Content")
            ];
        }

        public Task<string> DownloadRawArticleContentAsync(
            CatalogFeed feed,
            string? externalId,
            string? link,
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"<item><guid>{externalId}</guid><title>{title}</title></item>");

        public Task<string> DownloadRawFeedXmlAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default)
        {
            RequestedRawXmlFeedIds.Enqueue(feed.Id);
            return Task.FromResult(RawFeedXmlToReturn);
        }
    }

    private sealed class StubArticleContentService(
        Func<Uri, CancellationToken, Task<ExtractedArticleContent>> extract) : IArticleContentService
    {
        public int RequestCount { get; private set; }

        public Task<ExtractedArticleContent> ExtractAsync(
            Uri articleUri,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return extract(articleUri, cancellationToken);
        }
    }
}

[TestClass]
public sealed class ProfileChooserViewModelTests
{
    [TestMethod]
    public async Task CatalogMasterIsOnlyListedWhenRevealed()
    {
        var profileService = new ProfileService(new EmptyProfileStore(), new FakeHasher());
        await profileService.InitializeAsync();
        var viewModel = new ProfileChooserViewModel(profileService);
        await viewModel.InitializeAsync();

        Assert.IsTrue(viewModel.IsEmptyStateVisible);
        Assert.AreEqual(0, viewModel.Profiles.Count);

        await viewModel.SetCatalogMasterRevealedAsync(true);

        Assert.IsTrue(viewModel.IsProfileListVisible);
        Assert.AreEqual(ProfileNameValidator.CatalogMasterName, viewModel.Profiles.Single().Name);

        await viewModel.SetCatalogMasterRevealedAsync(false);

        Assert.IsTrue(viewModel.IsEmptyStateVisible);
        Assert.AreEqual(0, viewModel.Profiles.Count);
    }

    [TestMethod]
    public async Task DeleteProfileRemovesItFromTheChooser()
    {
        var store = new EmptyProfileStore();
        var profileService = new ProfileService(store, new FakeHasher());
        await profileService.InitializeAsync();
        var profile = await profileService.CreateProfileAsync("Reader", null, null);
        var viewModel = new ProfileChooserViewModel(profileService);
        await viewModel.InitializeAsync();
        viewModel.IsDeleteModeActive = true;

        await viewModel.DeleteProfileAsync(profile);

        Assert.IsTrue(viewModel.IsEmptyStateVisible);
        Assert.AreEqual(0, viewModel.Profiles.Count);
    }

    private sealed class EmptyProfileStore : IProfileStore
    {
        private readonly List<Profile> _profiles = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _profiles.Add(Profile.CreateCatalogMaster());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Profile>>(_profiles.ToArray());

        public Task<ProfilePreferences> GetPreferencesAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfilePreferences());

        public Task SavePreferencesAsync(string profileId, ProfilePreferences preferences, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Profile?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_profiles.SingleOrDefault(profile => profile.Id == id));

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_profiles.Any(profile => string.Equals(
                profile.Name,
                name,
                StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(Profile profile, CancellationToken cancellationToken = default)
        {
            _profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            _profiles.RemoveAll(profile => profile.Id == id && !profile.IsCatalogMaster);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => password;

        public bool Verify(string encodedHash, string password) => encodedHash == password;
    }
}