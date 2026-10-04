using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RssReader.App.ViewModels;
using RssReader.Application;
using RssReader.Domain;
using RssReader.Infrastructure;
using Microsoft.Data.Sqlite;
using System.Threading;

namespace RssReader.App.Tests;

[TestClass]
public sealed class MainWindowViewModelTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ManagedFeedPreviewCheckButtonClosesAndChecksFeed()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new RssReader.App.App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
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

                var checkFeedButton = (Button)preview.FindName("CheckFeedButton");
                var closeButton = (Button)preview.FindName("ClosePreviewButton");
                Assert.AreEqual(Visibility.Visible, checkFeedButton.Visibility);
                Assert.AreEqual(Visibility.Collapsed, closeButton.Visibility);
                Assert.AreEqual("Check Feed", checkFeedButton.Content);
                Assert.AreEqual("Check feed", AutomationProperties.GetName(checkFeedButton));
                Assert.AreEqual("#FF218739", checkFeedButton.Foreground.ToString());

                checkFeedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, checkFeedButton));

                Assert.IsTrue(checkFeedRequested);
                Assert.IsTrue(previewWasClosedBeforeCheck);
                Assert.IsFalse(preview.IsVisible);
                app.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void MainWindowCanBeConstructedForARegularProfile()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new RssReader.App.App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                var accessibilityFeed = new CatalogFeedListItem(
                    "feed-accessibility",
                    "Example comic feed",
                    "https://example.com/comics.xml",
                    "A sample comic feed",
                    "Comics",
                    "category-comics");
                var feedWithoutDescription = new CatalogFeedListItem(
                    "feed-no-description",
                    "Example feed without description",
                    "https://example.com/short.xml",
                    null,
                    "Comics",
                    "category-comics");
                viewModel.CatalogFeeds.Add(accessibilityFeed);
                viewModel.CatalogFeeds.Add(feedWithoutDescription);
                var window = new RssReader.App.MainWindow(viewModel);
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
                var articleCardsList = (ListBox)window.FindName("ArticleCardsList");
                var folderArticleCardsList = (ListBox)window.FindName("ArticleFolderCardsList");
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
                Assert.AreEqual(Visibility.Collapsed, managedPreviewCloseButton.Visibility);
                var previewActions = (StackPanel)VisualTreeHelper.GetParent(managedPreviewDeleteButton);
                Assert.AreSame(managedPreviewDeleteButton, previewActions.Children[0]);
                Assert.AreSame(managedPreviewCheckFeedButton, previewActions.Children[1]);
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
                Assert.AreEqual(Visibility.Collapsed, managedCheckCloseButton.Visibility);
                Assert.AreEqual("Check Feed", checkFeedButton.Content);
                Assert.AreEqual("Check feed", AutomationProperties.GetName(checkFeedButton));
                Assert.AreEqual("#FF218739", checkFeedButton.Foreground.ToString());
                checkFeedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, checkFeedButton));
                Assert.IsTrue(managedPreviewCheckRequested);
                Assert.IsFalse(managedCheckPreviewWindow.IsVisible);
                var catalogManagementList = (ListBox)window.FindName("CatalogManagementFeedList");
                Assert.AreEqual("Catalog feed management results", AutomationProperties.GetName(catalogManagementList));
                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(catalogManagementList));
                Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(catalogManagementList));
                var catalogActionsButton = (Button)window.FindName("CatalogActionsButton");
                Assert.AreEqual("Catalog actions", AutomationProperties.GetName(catalogActionsButton));
                CollectionAssert.AreEqual(
                    new[] { "Import OPML...", "Load starter pack" },
                    catalogActionsButton.ContextMenu!.Items.OfType<MenuItem>().Select(item => item.Header.ToString()).ToArray());
                catalogActionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, catalogActionsButton));
                Assert.IsTrue(catalogActionsButton.ContextMenu.IsOpen);
                catalogActionsButton.ContextMenu.IsOpen = false;
                Assert.AreEqual(2, ((TabControl)window.FindName("CatalogManagementTabs")).Items.Count);
                Assert.AreEqual(Visibility.Collapsed, ((ProgressBar)window.FindName("CatalogManagementLoadProgressBar")).Visibility);
                Assert.IsNull(window.FindName("CatalogFeedPreviewPanel"));
                viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));
                window.UpdateLayout();
                var catalogSearchBox = (TextBox)window.FindName("CatalogFeedSearchBox");
                catalogSearchBox.Text = "comic";
                Assert.AreEqual("comic", viewModel.CatalogSearchQuery);
                var categoryFilter = (ComboBox)window.FindName("CatalogCategoryFilter");
                Assert.AreEqual("All categories (0)", ((CatalogCategoryOption)categoryFilter.SelectedItem).Name);
                var collectionFilter = (ComboBox)window.FindName("CatalogCollectionFilter");
                Assert.AreEqual("All collections (0)", ((CatalogCollectionOption)collectionFilter.SelectedItem).Name);
                var hideFollowedCheckBox = (CheckBox)window.FindName("HideFollowedCatalogCheckBox");
                hideFollowedCheckBox.IsChecked = true;
                Assert.IsTrue(viewModel.HideFollowedCatalogFeeds);
                var catalogResults = (ListBox)window.FindName("CatalogFeedResultsList");
                Assert.AreEqual("Catalog feed results", AutomationProperties.GetName(catalogResults));
                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(catalogResults));
                Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(catalogResults));
                catalogResults.UpdateLayout();
                var feedContainer = catalogResults.ItemContainerGenerator.ContainerFromItem(accessibilityFeed);
                Assert.IsNotNull(feedContainer);
                var descriptionText = FindVisualChildren<TextBlock>(feedContainer!)
                    .Single(text => AutomationProperties.GetName(text) == "Catalog feed description");
                Assert.AreEqual(Visibility.Visible, descriptionText.Visibility);
                var feedWithoutDescriptionContainer = catalogResults.ItemContainerGenerator.ContainerFromItem(feedWithoutDescription);
                Assert.IsNotNull(feedWithoutDescriptionContainer);
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
                Assert.AreEqual("Select Example comic feed for follow", AutomationProperties.GetName(feedCheckBox));
                Assert.IsTrue(feedCheckBox.IsTabStop);
                Assert.AreEqual(Visibility.Visible, feedCheckBox.Visibility);
                Assert.AreEqual(Visibility.Visible, selectableFeedCheckBox.Visibility);
                Assert.AreEqual("Preview Example comic feed", AutomationProperties.GetName(previewButton));
                Assert.AreEqual("\uE890", ((TextBlock)previewButton.Content).Text);
                Assert.IsTrue(previewButton.IsTabStop);
                Assert.AreEqual("Follow Example comic feed", AutomationProperties.GetName(subscriptionButton));
                Assert.AreEqual("\uE710", ((TextBlock)subscriptionButton.Content).Text);
                accessibilityFeed.IsSubscribed = true;
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, feedCheckBox.Visibility);
                var followedFeedText = FindVisualChildren<StackPanel>(feedContainer!)
                    .Single(panel => Grid.GetColumn(panel) == 1);
                var unfollowedFeedText = FindVisualChildren<StackPanel>(feedWithoutDescriptionContainer!)
                    .Single(panel => Grid.GetColumn(panel) == 1);
                var followedFeedTextLeft = followedFeedText.TranslatePoint(new Point(0, 0), catalogResults).X;
                var unfollowedFeedTextLeft = unfollowedFeedText.TranslatePoint(new Point(0, 0), catalogResults).X;
                Assert.AreEqual(followedFeedTextLeft, unfollowedFeedTextLeft, 0.1);
                Assert.AreEqual("Unfollow Example comic feed", AutomationProperties.GetName(subscriptionButton));
                Assert.AreEqual("\uE738", ((TextBlock)subscriptionButton.Content).Text);
                var selectAllCatalogFeedsCheckBox = (CheckBox)window.FindName("SelectAllCatalogFeedsCheckBox");
                Assert.IsTrue(selectAllCatalogFeedsCheckBox.IsThreeState);
                Assert.AreEqual(0, Grid.GetColumn(selectAllCatalogFeedsCheckBox));
                var batchFollowButton = (Button)window.FindName("BatchFollowSelectedButton");
                Assert.AreEqual("\uE710", batchFollowButton.Content);
                Assert.AreEqual("Follow selected feeds", AutomationProperties.GetName(batchFollowButton));
                Assert.AreEqual("Follow selected (0)", AutomationProperties.GetHelpText(batchFollowButton));
                Assert.IsFalse(batchFollowButton.IsEnabled);
                Assert.IsFalse(((Button)window.FindName("ClearSelectedCatalogFeedsButton")).IsEnabled);
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
                Assert.IsTrue(((CheckBox)preferencesWindow.FindName("ShowRawFeedButtonCheckBox")).IsChecked);
                preferencesWindow.Close();
                var rawWindow = new RssReader.App.RawFeedWindow("Test feed", "<?xml version=\"1.0\"?><rss />") { Owner = window };
                rawWindow.Show();
                rawWindow.UpdateLayout();
                Assert.AreEqual("<?xml version=\"1.0\"?><rss />", ((TextBox)rawWindow.FindName("RawContentTextBox")).Text);
                rawWindow.Close();

                var profileButton = (Button)window.FindName("ProfileMenuButton");
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

                var sidebarPanel = (Border)window.FindName("SidebarPanel");
                var sidebarPeekButton = (Button)window.FindName("SidebarPeekButton");
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
                    feedId: "comic-feed",
                    link: "https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031",
                    content: "<p>Comic panel <img src=\"https://example.com/inline-panel.png\" alt=\"Inline panel\" /> follows.</p>",
                    imageUrl: "https://resources.arcamax.com/newspics/396/39604/3960483.gif");
                viewModel.SelectArticleCommand.Execute(comicArticle);
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, ((DockPanel)window.FindName("WorkspaceHeader")).Visibility);
                var articleViewer = (RssReader.App.ArticleHtmlViewer)window.FindName("SelectedArticleHtmlViewer");
                Assert.AreEqual(Visibility.Visible, articleViewer.Visibility);
                var initialViewerWidth = articleViewer.ActualWidth;
                var initialViewerHeight = articleViewer.ActualHeight;
                StringAssert.Contains(articleViewer.CurrentDocument, "https://example.com/inline-panel.png");
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
                window.Height += 120;
                window.UpdateLayout();
                Assert.IsTrue(articleViewer.ActualWidth > initialViewerWidth);
                Assert.IsTrue(articleViewer.ActualHeight > initialViewerHeight);
                Assert.AreEqual(articleViewer.ActualWidth, browser.ActualWidth, 1);
                Assert.AreEqual(articleViewer.ActualHeight, browser.ActualHeight, 1);
                var articleTitle = (TextBlock)window.FindName("SelectedArticleTitle");
                Assert.AreEqual(
                    new Uri("https://www.arcamax.com/thefunnies/ninechickweedlane/s-4307031"),
                    ((Hyperlink)articleTitle.Inlines.Single()).NavigateUri);
                viewModel.BackToListCommand.Execute(null);
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
                    lastHealthCheckSucceeded: false);
                catalogManagement.Feeds.Add(managedFeed);
                catalogManagement.Categories.Add(new CatalogCategory("comics", "Comics RSS"));
                catalogManagement.Collections.Add(new CatalogCollection("editor-picks", "Editor's picks"));

                var catalogMasterWindow = new RssReader.App.MainWindow(catalogMasterViewModel);
                catalogMasterWindow.Show();
                catalogMasterWindow.UpdateLayout();
                Assert.AreEqual(1, catalogManagement.VisibleFeedCount);
                ((TabControl)catalogMasterWindow.FindName("CatalogManagementTabs")).SelectedIndex = 0;
                catalogMasterWindow.UpdateLayout();
                var managementList = (ListBox)catalogMasterWindow.FindName("CatalogManagementFeedList");
                Assert.AreSame(
                    catalogMasterViewModel,
                    managementList.DataContext,
                    $"Unexpected feed list context: {managementList.DataContext?.GetType().FullName ?? "null"}");
                Assert.AreEqual(1, managementList.Items.Count);
                Assert.IsTrue(catalogMasterViewModel.IsCatalogAdminVisible);
                Assert.IsTrue(managementList.IsLoaded, $"Feed list loaded: {managementList.IsLoaded}; height: {managementList.ActualHeight}");
                Assert.IsTrue(managementList.ActualHeight > 0, $"Feed list height: {managementList.ActualHeight}");
                managementList.ScrollIntoView(managedFeed);
                catalogMasterWindow.UpdateLayout();
                managementList.UpdateLayout();
                var managedFeedContainer = managementList.ItemContainerGenerator.ContainerFromItem(managedFeed)
                    ?? throw new AssertFailedException("The Catalog Master feed row was not created.");
                var managementActions = FindVisualChildren<Button>(managedFeedContainer).ToArray();
                CollectionAssert.AreEquivalent(
                    new[] { "Preview feed", "Edit feed", "Check feed", "Remove feed" },
                    managementActions.Select(button => AutomationProperties.GetName(button)).ToArray());
                var managedTitle = FindVisualChildren<TextBlock>(managedFeedContainer).Single(text => text.Text == managedFeed.Name);
                Assert.AreEqual(14, managedTitle.FontSize);
                var managedDescription = FindVisualChildren<TextBlock>(managedFeedContainer)
                    .Single(text => AutomationProperties.GetName(text) == "Catalog feed description");
                Assert.AreEqual(Visibility.Collapsed, managedDescription.Visibility);
                var metadataIcon = FindVisualChildren<TextBlock>(managedFeedContainer)
                    .Single(text => AutomationProperties.GetName(text).StartsWith("Metadata gaps:", StringComparison.Ordinal));
                Assert.AreEqual(managedFeed.MetadataReviewDisplay, metadataIcon.ToolTip);
                var healthIcon = FindVisualChildren<TextBlock>(managedFeedContainer)
                    .Single(text => AutomationProperties.GetName(text).StartsWith("Check failed - checked", StringComparison.Ordinal));
                Assert.AreEqual(managedFeed.HealthCheckDisplay, healthIcon.ToolTip);
                Assert.AreEqual("\uE711", healthIcon.Text);

                var managementTabs = (TabControl)catalogMasterWindow.FindName("CatalogManagementTabs");
                managementTabs.SelectedIndex = 1;
                catalogMasterWindow.UpdateLayout();
                var renameButton = FindVisualChildren<Button>(catalogMasterWindow)
                    .Single(button => AutomationProperties.GetName(button) == "Rename category");
                var removeCategoryButton = FindVisualChildren<Button>(catalogMasterWindow)
                    .Single(button => AutomationProperties.GetName(button) == "Remove category");
                Assert.AreEqual(0, renameButton.Opacity);
                Assert.AreEqual(0, removeCategoryButton.Opacity);
                renameButton.Focus();
                catalogMasterWindow.UpdateLayout();
                Assert.AreEqual(1, renameButton.Opacity);
                catalogMasterWindow.Close();
                app.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void FeedTreeFolderCanExpandAndCollapseWithoutWpfAnimationErrors()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new RssReader.App.App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
                var folder = new SidebarLink("folder:Comics", "Comics", string.Empty, "(1)", indentLevel: 1);
                var feed = new SidebarLink("feed:comics", "Example comic", "\uE774", indentLevel: 2, parentFolder: folder);
                viewModel.FeedLinks.Add(folder);
                viewModel.FeedLinks.Add(feed);
                var window = new RssReader.App.MainWindow(viewModel);
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
                var deleteFolderButton = folderRow.Children.OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == "Delete folder");
                Assert.AreEqual(0, deleteFolderButton.Opacity);
                folderRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
                window.UpdateLayout();
                Assert.AreEqual(1, deleteFolderButton.Opacity);
                viewModel.ActivateSidebarLinkCommand.Execute(folder);
                window.UpdateLayout();
                Assert.IsTrue(folder.IsExpanded);
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
                Assert.AreEqual(1, deleteFolderButton.Opacity);
                Assert.AreEqual(40, folderButton.Padding.Right);

                viewModel.ActivateSidebarLinkCommand.Execute(folder);
                window.UpdateLayout();
                Assert.IsFalse(folder.IsExpanded);

                window.Close();
                app.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject =>
        FindVisualChildren<T>(parent).FirstOrDefault();

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
            var feed = new CatalogFeed("feed-1", "Gaming News", "https://example.com/feed.xml", "News", null);
            await catalogStore.AddFeedAsync(feed);
            await readerStore.AddFolderAsync(profile.Id, "Gaming");
            await readerStore.SubscribeAsync(profile.Id, feed.Id, "Gaming");
            await readerStore.AddFeedTagAsync(profile.Id, feed.Id, "Reviews");
            await readerStore.SaveArticlesAsync(feed.Id,
            [
                new FeedArticle("article-1", feed.Id, "item-1", "Handheld review", null, DateTimeOffset.UtcNow, "Portable hardware", "Full story")
            ]);

            var viewModel = new MainWindowViewModel(
                profile,
                new CatalogService(catalogStore),
                new ReadingService(readerStore, catalogStore),
                null,
                new ProfilePreferences(ShowRawFeedButton: true));
            await viewModel.InitializeAsync();

            Assert.AreEqual(1, viewModel.VisibleArticles.Count);
            Assert.IsFalse(viewModel.IsRawFeedButtonVisible);
            viewModel.SelectArticleCommand.Execute(viewModel.VisibleArticles[0]);
            Assert.IsTrue(viewModel.IsRawFeedButtonVisible);
            Assert.AreEqual("Handheld review", viewModel.VisibleArticles[0].Title);
            Assert.AreEqual("Full story", viewModel.VisibleArticles[0].Content);
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
            Assert.IsTrue(folderLink.IsExpanded);
            Assert.AreEqual("folder:Gaming", viewModel.ActiveRoute);
            viewModel.ActivateSidebarLinkCommand.Execute(folderLink);
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
            CollectionAssert.AreEqual(
                new[] { chickweed.Name, dilbert.Name, spaceNews.Name },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Name).ToArray());
            Assert.AreEqual("Editor's picks (2)", viewModel.CatalogCollectionOptions.Single(option => option.CollectionId == collection.Id).Name);

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
            Assert.AreEqual(2, viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Count());

            viewModel.SelectedCatalogCollection = viewModel.CatalogCollectionOptions.Single(option => option.CollectionId == collection.Id);
            Assert.AreEqual("Curated by Catalog Master", viewModel.SelectedCatalogCollectionCuratorLabel);
            CollectionAssert.AreEqual(
                new[] { chickweed.Id },
                viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Select(feed => feed.Id).ToArray());
            viewModel.SelectedCatalogCollection = viewModel.CatalogCollectionOptions.Single(option => option.CollectionId is null);
            Assert.AreEqual(string.Empty, viewModel.SelectedCatalogCollectionCuratorLabel);
            viewModel.SelectedCatalogCollection = viewModel.CatalogCollectionOptions.Single(option => option.CollectionId is null);

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
        Assert.IsTrue(preview.Items.All(item => item.Content is null && item.ImageUrl is null));
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
            viewModel.ConfirmDeleteFolderRequested = (folderName, feedCount) =>
            {
                confirmedFolder = folderName;
                confirmedFeedCount = feedCount;
                return Task.FromResult(false);
            };
            await viewModel.DeleteFolderCommand.ExecuteAsync(comicsFolder);
            Assert.AreEqual(2, (await readerStore.GetSubscriptionsAsync(profile.Id)).Count);
            Assert.IsTrue(viewModel.FolderNames.Contains("Comics", StringComparer.OrdinalIgnoreCase));

            viewModel.ConfirmDeleteFolderRequested = (folderName, feedCount) =>
            {
                confirmedFolder = folderName;
                confirmedFeedCount = feedCount;
                return Task.FromResult(true);
            };
            await viewModel.DeleteFolderCommand.ExecuteAsync(comicsFolder);

            Assert.AreEqual("Comics", confirmedFolder);
            Assert.AreEqual(1, confirmedFeedCount);
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
                await viewModel.GetRawArticleContentAsync(newFeedArticle));

            var visibleArticleChanges = 0;
            viewModel.VisibleArticles.CollectionChanged += (_, _) => visibleArticleChanges++;
            downloader.ItemsByFeed = new Dictionary<string, IReadOnlyList<DownloadedFeedItem>>
            {
                [existingFeed.Id] =
                [new DownloadedFeedItem($"{existingFeed.Id}-item", "Existing headline", null, null, "Summary", "Content")],
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
                [new DownloadedFeedItem($"{existingFeed.Id}-item", "Existing headline", null, null, "Summary", "Content")],
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
    }

    [TestMethod]
    public void SelectingArticleReplacesListAndBackReturnsToList()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));
        var article = viewModel.VisibleArticles[0];

        viewModel.SelectArticleCommand.Execute(article);

        Assert.IsFalse(viewModel.IsArticleListVisible);
        Assert.IsTrue(viewModel.IsReadingViewVisible);
        Assert.AreSame(article, viewModel.SelectedArticle);

        viewModel.BackToListCommand.Execute(null);

        Assert.IsTrue(viewModel.IsArticleListVisible);
        Assert.IsFalse(viewModel.IsReadingViewVisible);
        Assert.AreSame(article, viewModel.VisibleArticles[0]);
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
    public void DateSortShowsAFlatNewestFirstListWithFolderData()
    {
        var viewModel = new MainWindowViewModel(Profile.CreateRegular("Reader"));

        viewModel.IsSortByDate = true;

        Assert.IsTrue(viewModel.IsSortByDate);
        Assert.IsFalse(viewModel.IsSortByFolder);
        Assert.IsNull(viewModel.ArticleListView.Groups);
        var articles = viewModel.ArticleListView.Cast<ArticleRowViewModel>().ToArray();
        Assert.AreEqual(5, articles.Length);
        Assert.AreEqual("Gaming", articles[0].Folder);
        Assert.AreEqual("tech", articles[2].Folder);
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
            imageUrl: "https://resources.arcamax.com/newspics/396/39604/3960483.gif");

        Assert.IsFalse(article.HasReadableSummary);
        Assert.IsTrue(article.IsSourceLinkVisible);
        Assert.IsTrue(article.IsImageVisible);
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
            "<script>alert(2)</script><a href='javascript:alert(3)'>unsafe link</a>",
            "https://example.test/story");

        StringAssert.Contains(sanitized, "<p>");
        StringAssert.Contains(sanitized, "<strong>");
        StringAssert.Contains(sanitized, "<img");
        StringAssert.Contains(sanitized, "https://example.test/images/panel.jpg");
        Assert.IsFalse(sanitized.Contains("script", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("onerror", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("javascript:", StringComparison.OrdinalIgnoreCase));
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
            null,
            "https://armaghplanet.com/the-equinox-is-coming-what-on-earth-is-going-on.html",
            null);

        StringAssert.Contains(document, sunImage);
        StringAssert.Contains(document, earthImage);
        StringAssert.Contains(document, sunriseImage);
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderUsesSummaryAndLeadImageWhenBodyIsMissing()
    {
        var document = ArticleHtmlDocumentBuilder.Build(
            null,
            "A useful summary & detail.",
            "https://example.test/images/lead.jpg",
            null,
            null);

        StringAssert.Contains(document, "A useful summary &amp; detail.");
        StringAssert.Contains(document, "<img");
        StringAssert.Contains(document, "https://example.test/images/lead.jpg");
    }

    [TestMethod]
    public void ArticleHtmlDocumentBuilderResolvesRelativeImagesAgainstFeedUrlWhenArticleLinkIsMissing()
    {
        var document = ArticleHtmlDocumentBuilder.Build(
            "<p><img src=\"/images/panel.jpg\" alt=\"Panel\"></p>",
            null,
            null,
            null,
            "https://example.test/rss/feed.xml");

        StringAssert.Contains(document, "https://example.test/images/panel.jpg");
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

    private sealed class RecordingFeedDownloader : IFeedDownloader, IRawFeedContentDownloader
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> RequestedFeedIds { get; } = new();

        public IReadOnlyList<DownloadedFeedItem>? ItemsToReturn { get; set; }

        public IReadOnlyDictionary<string, IReadOnlyList<DownloadedFeedItem>>? ItemsByFeed { get; set; }

        public Func<CancellationToken, Task<IReadOnlyList<DownloadedFeedItem>>>? DownloadHandler { get; set; }

        public async Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default)
        {
            RequestedFeedIds.Enqueue(feed.Id);
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