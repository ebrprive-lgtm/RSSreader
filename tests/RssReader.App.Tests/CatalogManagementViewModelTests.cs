using System.IO;
using System.Text;
using RssReader.App.ViewModels;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.Tests;

[TestClass]
public sealed class CatalogManagementViewModelTests
{
    [TestMethod]
    public void CatalogFeedListItemRepairsCommonDoubleEncodedTitleText()
    {
        var feed = new CatalogFeedListItem(
            "energy-feed",
            "Tech Xplore Ã¢â‚¬â€œ Energy & Green Tech RSS Feed",
            "https://example.com/feed.xml",
            null,
            null);

        Assert.AreEqual("Tech Xplore – Energy & Green Tech RSS Feed", feed.Name);

        var doubleEncodedFeed = new CatalogFeedListItem(
            "business-feed",
            "Forbes Ã‚Â» Business RSS Feed",
            "https://example.com/business.xml",
            null,
            "Business Ã‚Â»");

        Assert.AreEqual("Forbes » Business RSS Feed", doubleEncodedFeed.Name);
        Assert.AreEqual("Business »", doubleEncodedFeed.CategoryName);

        var management = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(new MemoryCatalogStore()));
        var childNode = new CatalogTaxonomyTreeNode(
            CatalogTaxonomyTreeNodeKind.Feed,
            doubleEncodedFeed.Id,
            doubleEncodedFeed.Name,
            management,
            feed: doubleEncodedFeed);

        Assert.AreEqual("Forbes » Business RSS Feed", childNode.Name);
    }

    [TestMethod]
    public async Task OpmlImportAddsFeedsAndMapsFoldersToCategories()
    {
        const string opml = "<opml version=\"2.0\"><body><outline text=\"Science\"><outline text=\"NASA\" xmlUrl=\"https://example.com/nasa.xml\" /></outline></body></opml>";
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(new MemoryCatalogStore()));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(opml));

        await viewModel.ImportOpmlAsync(stream);

        Assert.AreEqual(1, viewModel.Feeds.Count);
        Assert.AreEqual("NASA", viewModel.Feeds.Single().Name);
        Assert.AreEqual("Science", viewModel.Feeds.Single().CategoryName);
        Assert.AreEqual("Added 1 feed(s); skipped 0.", viewModel.ImportMessage);
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task OpmlImportKeepsSameNameFeedsDistinctAndFlagsThemForReview()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);

        var result = await service.ImportFeedsAsync(Profile.CreateCatalogMaster(),
        [
            new OpmlFeed("Example Journal", "https://publisher-a.example/feed.xml", null, null, "https://publisher-a.example"),
            new OpmlFeed("example journal", "https://publisher-b.example/feed.xml", null, null, "https://publisher-b.example")
        ]);
        var viewModel = new CatalogManagementViewModel(Profile.CreateCatalogMaster(), service);
        await viewModel.InitializeAsync();

        Assert.AreEqual(2, result.AddedCount);
        Assert.AreEqual(2, viewModel.Feeds.Count);
        Assert.IsTrue(viewModel.Feeds.All(feed => feed.SameNameDisplay == "2 feeds share this name"));
        CollectionAssert.AreEquivalent(
            new[] { "https://publisher-a.example/", "https://publisher-b.example/" },
            viewModel.Feeds.Select(feed => feed.WebsiteUrl).ToArray());
    }

    [TestMethod]
    public async Task OpmlImportReportsExactUrlDuplicatesSeparatelyFromSameNameFeeds()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);
        await service.AddFeedAsync(
            Profile.CreateCatalogMaster(),
            "Example Journal",
            "https://example.com/feed.xml",
            null,
            null);
        var viewModel = new CatalogManagementViewModel(Profile.CreateCatalogMaster(), service);
        const string opml = "<opml version=\"2.0\"><body>" +
            "<outline text=\"Exact duplicate\" xmlUrl=\"https://EXAMPLE.com:443/feed.xml\" />" +
            "<outline text=\"Example Journal\" xmlUrl=\"https://different.example/feed.xml\" />" +
            "</body></opml>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(opml));

        await viewModel.ImportOpmlAsync(stream);

        StringAssert.Contains(viewModel.ImportMessage, "Exact URL duplicate (skipped): Exact duplicate matches Example Journal");
        StringAssert.Contains(viewModel.ImportMessage, "Same name, different URL (kept separately): Example Journal");
        Assert.AreEqual(2, viewModel.Feeds.Count);
        Assert.IsTrue(viewModel.Feeds.All(feed => feed.Name == "Example Journal"));
    }

    [TestMethod]
    public async Task ImportReportCanBeDismissedAndNotifiesVisibilityChange()
    {
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(new MemoryCatalogStore()));
        var hasImportMessageNotified = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.HasImportMessage))
            {
                hasImportMessageNotified = true;
            }
        };
        const string opml = "<opml version=\"2.0\"><body><outline text=\"Example\" xmlUrl=\"https://example.com/feed.xml\" /></body></opml>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(opml));

        await viewModel.ImportOpmlAsync(stream);

        Assert.IsTrue(viewModel.HasImportMessage);
        Assert.IsTrue(hasImportMessageNotified);
        viewModel.DismissImportMessageCommand.Execute(null);

        Assert.AreEqual(string.Empty, viewModel.ImportMessage);
        Assert.IsFalse(viewModel.HasImportMessage);
    }

    [TestMethod]
    public async Task StarterPackAddsCuratedFeedsAndSkipsThemWhenRepeated()
    {
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(new MemoryCatalogStore()));

        await viewModel.LoadStarterPackCommand.ExecuteAsync();
        Assert.AreEqual(6, viewModel.Feeds.Count);
        Assert.AreEqual("Added 6 feed(s); skipped 0.", viewModel.ImportMessage);

        await viewModel.LoadStarterPackCommand.ExecuteAsync();

        Assert.AreEqual(6, viewModel.Feeds.Count);
        StringAssert.StartsWith(viewModel.ImportMessage, "Added 0 feed(s); skipped 6.");
        StringAssert.Contains(viewModel.ImportMessage, "Exact URL duplicate (skipped):");
    }

    [TestMethod]
    public async Task CatalogMasterCommandsCreateAndOrganizeCatalogEntries()
    {
        var store = new MemoryCatalogStore();
        var viewModel = new CatalogManagementViewModel(Profile.CreateCatalogMaster(), new CatalogService(store));
        await viewModel.InitializeAsync();
        viewModel.CategoryName = "Technology";
        await viewModel.AddCategoryCommand.ExecuteAsync();
        viewModel.FeedName = "The Verge";
        viewModel.FeedUrl = "https://example.com/feed.xml";
        viewModel.SelectedCategory = viewModel.Categories.Single();
        await viewModel.AddFeedCommand.ExecuteAsync();
        viewModel.CollectionName = "Daily reads";
        await viewModel.AddCollectionCommand.ExecuteAsync();
        viewModel.SelectedFeed = viewModel.Feeds.Single();
        viewModel.SelectedCollection = viewModel.Collections.Single();

        await viewModel.AddFeedToCollectionCommand.ExecuteAsync();

        Assert.AreEqual("The Verge", viewModel.Feeds.Single().Name);
        Assert.AreEqual("Technology", viewModel.Feeds.Single().CategoryName);
        Assert.AreEqual(viewModel.Feeds.Single().Id, store.CollectionFeedIds.Single());

        await viewModel.RemoveFeedFromCollectionCommand.ExecuteAsync();

        Assert.AreEqual(0, store.CollectionFeedIds.Count);
    }

    [TestMethod]
    public async Task CollectionMembershipCanBeManagedFromFeedsOrCollectionsAndRefreshesCounts()
    {
        var actor = Profile.CreateCatalogMaster();
        var service = new CatalogService(new MemoryCatalogStore());
        var firstFeed = await service.AddFeedAsync(
            actor,
            "First source",
            "https://example.com/first.xml",
            null,
            null);
        var secondFeed = await service.AddFeedAsync(
            actor,
            "Second source",
            "https://example.com/second.xml",
            null,
            null);
        var firstCollection = await service.AddCollectionAsync(actor, "Daily briefing");
        var secondCollection = await service.AddCollectionAsync(actor, "PC gaming");
        var viewModel = new CatalogManagementViewModel(actor, service);
        await viewModel.InitializeAsync();

        Assert.IsTrue(await viewModel.UpdateFeedCollectionsAsync(
            firstFeed.Id,
            [firstCollection.Id, secondCollection.Id]));
        CollectionAssert.AreEquivalent(
            new[] { firstCollection.Id, secondCollection.Id },
            viewModel.GetCollectionIdsForFeed(firstFeed.Id).ToArray());
        Assert.AreEqual(1, viewModel.CollectionFilterOptions.Single(option =>
            option.CollectionId == firstCollection.Id).FeedIds.Count);

        Assert.IsTrue(await viewModel.UpdateCollectionFeedsAsync(firstCollection.Id, [secondFeed.Id]));

        CollectionAssert.AreEquivalent(
            new[] { secondCollection.Id },
            viewModel.GetCollectionIdsForFeed(firstFeed.Id).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { firstCollection.Id },
            viewModel.GetCollectionIdsForFeed(secondFeed.Id).ToArray());
        Assert.AreEqual(1, viewModel.CollectionFilterOptions.Single(option =>
            option.CollectionId == firstCollection.Id).FeedIds.Count);
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task TaxonomyTreesLoadChildrenOnDemandAndSupportCategoryCollectionActions()
    {
        var actor = Profile.CreateCatalogMaster();
        var service = new CatalogService(new MemoryCatalogStore());
        var category = await service.AddCategoryAsync(actor, "News");
        var categorizedFeed = await service.AddFeedAsync(
            actor,
            "Daily news",
            "https://example.com/news.xml",
            null,
            category.Id);
        var uncategorizedFeed = await service.AddFeedAsync(
            actor,
            "Other source",
            "https://example.com/other.xml",
            null,
            null);
        var collection = await service.AddCollectionAsync(actor, "Briefing");
        var viewModel = new CatalogManagementViewModel(actor, service);
        await viewModel.InitializeAsync();

        var categoryNode = viewModel.CategoryTreeNodes.Single(node => node.Id == category.Id);
        Assert.IsFalse(categoryNode.IsChildrenLoaded);
        Assert.AreEqual(CatalogTaxonomyTreeNodeKind.Placeholder, categoryNode.Children.Single().Kind);
        viewModel.LoadTaxonomyChildren(categoryNode);
        CollectionAssert.AreEqual(
            new[] { categorizedFeed.Id },
            categoryNode.Children.Select(node => node.Feed!.Id).ToArray());

        var uncategorizedNode = viewModel.CategoryTreeNodes.Single(node => node.Name == "Uncategorized");
        viewModel.LoadTaxonomyChildren(uncategorizedNode);
        CollectionAssert.AreEqual(
            new[] { uncategorizedFeed.Id },
            uncategorizedNode.Children.Select(node => node.Feed!.Id).ToArray());

        var collectionNode = viewModel.CollectionTreeNodes.Single(node => node.Id == collection.Id);
        collectionNode.IsExpanded = true;
        await viewModel.AddCategoryFeedsToCollectionAsync(collection.Id, category.Id);

        collectionNode = viewModel.CollectionTreeNodes.Single(node => node.Id == collection.Id);
        Assert.IsTrue(collectionNode.IsExpanded);
        viewModel.LoadTaxonomyChildren(collectionNode);
        CollectionAssert.AreEqual(
            new[] { categorizedFeed.Id },
            collectionNode.Children.Select(node => node.Feed!.Id).ToArray());

        categoryNode = viewModel.CategoryTreeNodes.Single(node => node.Id == category.Id);
        viewModel.LoadTaxonomyChildren(categoryNode);
        await viewModel.SetCategoryFeedAvailabilityCommand.ExecuteAsync(categoryNode.Children.Single());
        var feedAfterCategoryDisable = (await service.GetFeedsAsync()).Single(feed => feed.Id == categorizedFeed.Id);
        Assert.IsTrue(feedAfterCategoryDisable.IsEnabled);
        Assert.IsFalse(feedAfterCategoryDisable.IsEnabledInCategory);

        var browserState = new CatalogBrowserState(service, canFollowFeeds: true, hideFollowedCatalogFeeds: false);
        await browserState.LoadAsync(
            CancellationToken.None,
            new HashSet<string>(StringComparer.Ordinal),
            initializeCatalogManagement: null);
        Assert.IsTrue(browserState.CatalogFeedListView
            .Cast<CatalogFeedListItem>()
            .Any(feed => feed.Id == categorizedFeed.Id));
        browserState.SelectedCatalogCategory = browserState.CatalogCategoryOptions
            .Single(option => option.CategoryId == category.Id);
        Assert.IsFalse(browserState.CatalogFeedListView
            .Cast<CatalogFeedListItem>()
            .Any(feed => feed.Id == categorizedFeed.Id));

        collectionNode = viewModel.CollectionTreeNodes.Single(node => node.Id == collection.Id);
        viewModel.LoadTaxonomyChildren(collectionNode);
        var collectionFeedNode = collectionNode.Children.Single(node => node.Feed?.Id == categorizedFeed.Id);
        Assert.IsTrue(collectionFeedNode.IsCollectionFeed);
        await viewModel.SetGlobalFeedAvailabilityCommand.ExecuteAsync(collectionFeedNode.Feed!);
        var feedAfterGlobalDisable = (await service.GetFeedsAsync()).Single(feed => feed.Id == categorizedFeed.Id);
        Assert.IsFalse(feedAfterGlobalDisable.IsEnabled);
        Assert.IsFalse(feedAfterGlobalDisable.IsEnabledInCategory);

        var browserStateAfterCollectionDisable = new CatalogBrowserState(service, canFollowFeeds: true, hideFollowedCatalogFeeds: false);
        await browserStateAfterCollectionDisable.LoadAsync(
            CancellationToken.None,
            new HashSet<string>(StringComparer.Ordinal),
            initializeCatalogManagement: null);
        Assert.IsFalse(browserStateAfterCollectionDisable.CatalogFeedListView
            .Cast<CatalogFeedListItem>()
            .Any(feed => feed.Id == categorizedFeed.Id));

        collectionNode = viewModel.CollectionTreeNodes.Single(node => node.Id == collection.Id);
        viewModel.LoadTaxonomyChildren(collectionNode);
        collectionFeedNode = collectionNode.Children.Single(node => node.Feed?.Id == categorizedFeed.Id);
        await viewModel.SetGlobalFeedAvailabilityCommand.ExecuteAsync(collectionFeedNode.Feed!);
        var feedAfterGlobalEnable = (await service.GetFeedsAsync()).Single(feed => feed.Id == categorizedFeed.Id);
        Assert.IsTrue(feedAfterGlobalEnable.IsEnabled);
        Assert.IsFalse(feedAfterGlobalEnable.IsEnabledInCategory);

        var browserStateAfterGlobalEnable = new CatalogBrowserState(service, canFollowFeeds: true, hideFollowedCatalogFeeds: false);
        await browserStateAfterGlobalEnable.LoadAsync(
            CancellationToken.None,
            new HashSet<string>(StringComparer.Ordinal),
            initializeCatalogManagement: null);
        Assert.IsTrue(browserStateAfterGlobalEnable.CatalogFeedListView
            .Cast<CatalogFeedListItem>()
            .Any(feed => feed.Id == categorizedFeed.Id));

        collectionNode = viewModel.CollectionTreeNodes.Single(node => node.Id == collection.Id);
        viewModel.LoadTaxonomyChildren(collectionNode);
        await viewModel.RemoveCollectionTreeFeedCommand.ExecuteAsync(collectionNode.Children.Single());
        Assert.AreEqual(0, viewModel.GetFeedIdsForCollection(collection.Id).Count);
    }

    [TestMethod]
    public async Task CatalogDeletionRequiresConfirmationAndExplainsItsImpact()
    {
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);
        var actor = Profile.CreateCatalogMaster();
        var category = await service.AddCategoryAsync(actor, "Technology");
        var firstFeed = await service.AddFeedAsync(
            actor,
            "Example One",
            "https://example.com/one.xml",
            null,
            category.Id);
        var secondFeed = await service.AddFeedAsync(
            actor,
            "Example Two",
            "https://example.com/two.xml",
            null,
            category.Id);
        var collection = await service.AddCollectionAsync(actor, "Daily reads");
        await service.AddFeedToCollectionAsync(actor, collection.Id, firstFeed.Id);
        await service.AddFeedToCollectionAsync(actor, collection.Id, secondFeed.Id);
        var viewModel = new CatalogManagementViewModel(actor, service);
        await viewModel.InitializeAsync();
        string? confirmationMessage = null;
        viewModel.ConfirmDeleteRequested = message =>
        {
            confirmationMessage = message;
            return Task.FromResult(false);
        };

        await viewModel.DeleteFeedCommand.ExecuteAsync(viewModel.Feeds.Single(feed => feed.Id == firstFeed.Id));

        Assert.AreEqual(2, viewModel.Feeds.Count);
        StringAssert.Contains(confirmationMessage!, "every profile");
        StringAssert.Contains(confirmationMessage, "cached articles");
        StringAssert.Contains(confirmationMessage, "read/saved state");
        StringAssert.Contains(confirmationMessage, "collection memberships");

        viewModel.ConfirmDeleteRequested = _ => Task.FromResult(true);
        await viewModel.DeleteFeedCommand.ExecuteAsync(viewModel.Feeds.Single(feed => feed.Id == firstFeed.Id));
        Assert.AreEqual(1, viewModel.Feeds.Count);
        Assert.AreEqual(1, store.CollectionFeedIds.Count);

        viewModel.ConfirmDeleteRequested = message =>
        {
            confirmationMessage = message;
            return Task.FromResult(false);
        };
        await viewModel.DeleteCategoryCommand.ExecuteAsync(viewModel.Categories.Single());
        Assert.AreEqual(1, viewModel.Categories.Count);
        StringAssert.Contains(confirmationMessage!, "become uncategorized");

        viewModel.ConfirmDeleteRequested = message =>
        {
            confirmationMessage = message;
            return Task.FromResult(false);
        };
        await viewModel.DeleteCollectionCommand.ExecuteAsync(viewModel.Collections.Single());
        Assert.AreEqual(1, viewModel.Collections.Count);
        StringAssert.Contains(confirmationMessage!, "feed memberships are removed");
        StringAssert.Contains(confirmationMessage, "feeds remain in the catalog");
    }

    [TestMethod]
    public async Task CatalogMasterFeedAppearsInFollowSourcesImmediatelyAfterCreation()
    {
        var catalogService = new CatalogService(new MemoryCatalogStore());
        var viewModel = new MainWindowViewModel(Profile.CreateCatalogMaster(), catalogService);
        await viewModel.InitializeAsync();
        var catalogManagement = viewModel.CatalogManagement!;
        catalogManagement.FeedName = "New publication";
        catalogManagement.FeedUrl = "https://example.com/feed.xml";

        await catalogManagement.AddFeedCommand.ExecuteAsync();

        viewModel.NavigateCommand.Execute(viewModel.PrimaryLinks.Single(link => link.Route == "Follow sources"));
        viewModel.CatalogSearchQuery = "New publication";

        var feed = viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Single();
        Assert.AreEqual("New publication", feed.Name);
        Assert.AreEqual(1, viewModel.CatalogFeeds.Count);

        catalogManagement.PrepareFeedEdit(catalogManagement.Feeds.Single());
        catalogManagement.FeedName = "New publication revised";
        await catalogManagement.AddFeedCommand.ExecuteAsync();
        viewModel.CatalogSearchQuery = "New publication revised";

        var revisedFeed = viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Single();
        Assert.AreEqual(feed.Id, revisedFeed.Id);
        Assert.AreEqual("New publication revised", revisedFeed.Name);

        catalogManagement.ConfirmDeleteRequested = _ => Task.FromResult(true);
        await catalogManagement.DeleteFeedCommand.ExecuteAsync(catalogManagement.Feeds.Single());
        Assert.AreEqual(0, viewModel.CatalogFeedListView.Cast<CatalogFeedListItem>().Count());
        Assert.AreEqual(0, viewModel.CatalogFeeds.Count);
    }

    [TestMethod]
    public async Task CatalogMasterCanEditExistingFeedMetadata()
    {
        var store = new MemoryCatalogStore();
        var catalogService = new CatalogService(store);
        var actor = Profile.CreateCatalogMaster();
        var original = await catalogService.AddFeedAsync(
            actor,
            "Example",
            "https://example.com/feed.xml",
            null,
            null);
        var viewModel = new CatalogManagementViewModel(actor, catalogService);
        await viewModel.InitializeAsync();
        viewModel.PrepareFeedEdit(viewModel.Feeds.Single());
        viewModel.FeedName = "Example Journal";
        viewModel.FeedDescription = "Edited description";
        viewModel.FeedWebsiteUrl = "https://example.com/journal";

        await viewModel.AddFeedCommand.ExecuteAsync();

        var edited = viewModel.Feeds.Single();
        Assert.AreEqual(original.Id, edited.Id);
        Assert.AreEqual("Example Journal", edited.Name);
        Assert.AreEqual("Edited description", edited.Description);
        Assert.AreEqual("https://example.com/journal", edited.WebsiteUrl);
    }

    [TestMethod]
    public async Task DisablingCategoryHidesItsFeedsOutsideCategoryManagement()
    {
        var actor = Profile.CreateCatalogMaster();
        var store = new MemoryCatalogStore();
        var service = new CatalogService(store);
        var category = await service.AddCategoryAsync(actor, "Technology");
        var categorizedFeed = await service.AddFeedAsync(
            actor,
            "Technology news",
            "https://example.com/technology.xml",
            null,
            category.Id);
        var uncategorizedFeed = await service.AddFeedAsync(
            actor,
            "Other news",
            "https://example.com/other.xml",
            null,
            null);
        var collection = await service.AddCollectionAsync(actor, "Briefing");
        await service.AddFeedToCollectionAsync(actor, collection.Id, categorizedFeed.Id);

        var viewModel = new CatalogManagementViewModel(actor, service);
        await viewModel.InitializeAsync();
        Assert.AreEqual(2, viewModel.VisibleFeedCount);

        var categoryNode = viewModel.CategoryTreeNodes.Single(node => node.Id == category.Id);
        await viewModel.SetCategoryAvailabilityCommand.ExecuteAsync(categoryNode);

        Assert.IsFalse(viewModel.Categories.Single(item => item.Id == category.Id).IsEnabled);
        Assert.AreEqual(1, viewModel.VisibleFeedCount);
        Assert.AreEqual(uncategorizedFeed.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
        Assert.IsFalse(viewModel.CategoryFilterOptions.Any(option => option.CategoryId == category.Id));

        categoryNode = viewModel.CategoryTreeNodes.Single(node => node.Id == category.Id);
        Assert.AreEqual("Enable", categoryNode.CategoryAvailabilityActionLabel);
        viewModel.LoadTaxonomyChildren(categoryNode);
        Assert.IsTrue(categoryNode.Children.Any(node => node.Feed?.Id == categorizedFeed.Id));

        var collectionNode = viewModel.CollectionTreeNodes.Single(node => node.Id == collection.Id);
        viewModel.LoadTaxonomyChildren(collectionNode);
        Assert.IsFalse(collectionNode.Children.Any(node => node.Feed?.Id == categorizedFeed.Id));

        var browserState = new CatalogBrowserState(service, canFollowFeeds: true, hideFollowedCatalogFeeds: false);
        await browserState.LoadAsync(
            CancellationToken.None,
            new HashSet<string>(StringComparer.Ordinal),
            initializeCatalogManagement: null);
        Assert.IsFalse(browserState.CatalogCategoryOptions.Any(option => option.CategoryId == category.Id));
        Assert.IsFalse(browserState.CatalogFeedListView
            .Cast<CatalogFeedListItem>()
            .Any(feed => feed.Id == categorizedFeed.Id));
        Assert.IsTrue(browserState.CatalogFeedListView
            .Cast<CatalogFeedListItem>()
            .Any(feed => feed.Id == uncategorizedFeed.Id));

        await viewModel.SetCategoryAvailabilityCommand.ExecuteAsync(categoryNode);
        Assert.IsTrue(viewModel.Categories.Single(item => item.Id == category.Id).IsEnabled);
        Assert.AreEqual(2, viewModel.VisibleFeedCount);

        var reenabledBrowserState = new CatalogBrowserState(service, canFollowFeeds: true, hideFollowedCatalogFeeds: false);
        await reenabledBrowserState.LoadAsync(
            CancellationToken.None,
            new HashSet<string>(StringComparer.Ordinal),
            initializeCatalogManagement: null);
        Assert.IsTrue(reenabledBrowserState.CatalogCategoryOptions.Any(option => option.CategoryId == category.Id));
        Assert.IsTrue(reenabledBrowserState.CatalogFeedListView
            .Cast<CatalogFeedListItem>()
            .Any(feed => feed.Id == categorizedFeed.Id));
    }

    [TestMethod]
    public async Task CatalogMasterCanRenameCategoryWithoutChangingFeedAssignment()
    {
        var store = new MemoryCatalogStore();
        var category = new CatalogCategory("category-comics", "Comics");
        await store.AddCategoryAsync(category);
        var feed = new CatalogFeed("comic-feed", "Comic", "https://example.com/feed.xml", null, category.Id);
        await store.AddFeedAsync(feed);
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store));
        await viewModel.InitializeAsync();

        viewModel.PrepareCategoryEdit(viewModel.Categories.Single());
        viewModel.CategoryName = "Comics and cartoons";
        await viewModel.AddCategoryCommand.ExecuteAsync();

        Assert.AreEqual(category.Id, viewModel.Categories.Single().Id);
        Assert.AreEqual("Comics and cartoons", viewModel.Categories.Single().Name);
        Assert.AreEqual("Comics and cartoons", viewModel.Feeds.Single().CategoryName);
    }

    [TestMethod]
    public async Task CatalogMasterCanRenameCollectionWithoutLosingFeedMemberships()
    {
        var store = new MemoryCatalogStore();
        var actor = Profile.CreateCatalogMaster();
        var collection = new CatalogCollection("collection-games", "Games");
        var feed = new CatalogFeed("pc-game-feed", "PC games", "https://example.com/games.xml", null, null);
        await store.AddCollectionAsync(collection);
        await store.AddFeedAsync(feed);
        await store.AddFeedToCollectionAsync(collection.Id, feed.Id);
        var viewModel = new CatalogManagementViewModel(actor, new CatalogService(store));
        await viewModel.InitializeAsync();

        viewModel.PrepareCollectionEdit(viewModel.Collections.Single());
        viewModel.CollectionName = "PC Gaming";
        await viewModel.AddCollectionCommand.ExecuteAsync();

        Assert.AreEqual(collection.Id, viewModel.Collections.Single().Id);
        Assert.AreEqual("PC Gaming", viewModel.Collections.Single().Name);
        CollectionAssert.AreEqual(new[] { feed.Id }, store.CollectionFeedIds.ToArray());
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task CatalogMasterCanMergeEditedCategoryIntoExistingCategory()
    {
        var store = new MemoryCatalogStore();
        var sourceCategory = new CatalogCategory("category-science", "Science");
        var targetCategory = new CatalogCategory("category-research", "Research");
        await store.AddCategoryAsync(sourceCategory);
        await store.AddCategoryAsync(targetCategory);
        await store.AddFeedAsync(new CatalogFeed(
            "science-feed",
            "Science feed",
            "https://example.com/science.xml",
            null,
            sourceCategory.Id));
        await store.AddFeedAsync(new CatalogFeed(
            "research-feed",
            "Research feed",
            "https://example.com/research.xml",
            null,
            targetCategory.Id));
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store));
        await viewModel.InitializeAsync();

        viewModel.PrepareCategoryEdit(viewModel.Categories.Single(category => category.Id == sourceCategory.Id));
        Assert.IsFalse(viewModel.CanMergeCategory);
        viewModel.CategoryName = " research ";

        Assert.IsTrue(viewModel.CanMergeCategory);
        Assert.AreEqual(targetCategory.Id, viewModel.MergeTargetCategory!.Id);
        Assert.AreEqual("Merge into Research", viewModel.MergeCategoryButtonLabel);
        await viewModel.MergeCategoryCommand.ExecuteAsync();

        Assert.AreEqual(1, viewModel.Categories.Count);
        Assert.AreEqual(targetCategory.Id, viewModel.Categories.Single().Id);
        Assert.IsTrue(viewModel.Feeds.All(feed => feed.CategoryId == targetCategory.Id));
        Assert.IsTrue(viewModel.Feeds.All(feed => feed.CategoryName == targetCategory.Name));
        Assert.IsFalse(viewModel.CanMergeCategory);
    }

    [TestMethod]
    public async Task CatalogMasterPreviewReturnsOnlySanitizedSampleItems()
    {
        var store = new MemoryCatalogStore();
        var feed = new CatalogFeed("preview-feed", "Preview feed", "https://example.com/feed.xml", "Description", null);
        await store.AddFeedAsync(feed);
        var previewService = new CatalogFeedPreviewService(new PreviewFeedDownloader());
        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store),
            previewService);
        await viewModel.InitializeAsync();

        var preview = await viewModel.PreviewFeedAsync(viewModel.Feeds.Single());

        Assert.AreEqual("Preview feed", preview.Name);
        Assert.AreEqual(5, preview.Items.Count);
        Assert.IsTrue(preview.Items.All(item =>
            item.Content is null && item.ImageUrl == "https://example.com/image.jpg"));
    }

    [TestMethod]
    public async Task CatalogMasterSearchAndFiltersComposeAndReportMatchingCounts()
    {
        var store = new MemoryCatalogStore();
        var science = new CatalogCategory("category-science", "Science");
        var technology = new CatalogCategory("category-technology", "Technology");
        var spaceCollection = new CatalogCollection("collection-space", "Space sources");
        await store.AddCategoryAsync(science);
        await store.AddCategoryAsync(technology);
        await store.AddCollectionAsync(spaceCollection);

        var nasaSpace = new CatalogFeed(
            "feed-nasa-space",
            "NASA Space News",
            "https://feeds.example/nasa.xml",
            "Space agency reports",
            science.Id,
            "https://nasa.example");
        var nasaMissions = new CatalogFeed(
            "feed-nasa-missions",
            "NASA Mission Updates",
            "https://publisher-b.example/rss",
            null,
            science.Id);
        var technologyNews = new CatalogFeed(
            "feed-tech-news",
            "Tech Desk",
            "https://tech.example/rss",
            "Hardware news",
            technology.Id,
            "https://tech.example");
        await store.AddFeedAsync(nasaSpace);
        await store.AddFeedAsync(nasaMissions);
        await store.AddFeedAsync(technologyNews);
        await store.AddFeedToCollectionAsync(spaceCollection.Id, nasaSpace.Id);
        await store.AddFeedToCollectionAsync(spaceCollection.Id, nasaMissions.Id);

        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store, new TestFeedDownloader(feed => feed.Id == nasaMissions.Id)));
        await viewModel.InitializeAsync();

        Assert.AreEqual("Showing 3 of 3 feeds", viewModel.FeedResultsSummary);
        CollectionAssert.AreEqual(
            new[] { "NASA Mission Updates", "NASA Space News", "Tech Desk" },
            viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Select(feed => feed.Name).ToArray());

        viewModel.SearchQuery = "  nasa   space ";
        Assert.AreEqual(nasaSpace.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
        Assert.AreEqual("Showing 1 of 3 feeds", viewModel.FeedResultsSummary);

        viewModel.SearchQuery = "no such feed";
        Assert.IsTrue(viewModel.IsFeedResultsEmpty);
        Assert.AreEqual("No feeds match these filters.", viewModel.FeedResultsEmptyMessage);

        viewModel.ClearFiltersCommand.Execute(null);
        viewModel.SelectedCategoryFilter = viewModel.CategoryFilterOptions.Single(option => option.CategoryId == science.Id);
        Assert.AreEqual(2, viewModel.VisibleFeedCount);

        viewModel.SelectedCollectionFilter = viewModel.CollectionFilterOptions.Single(option => option.CollectionId == spaceCollection.Id);
        Assert.AreEqual(2, viewModel.VisibleFeedCount);

        viewModel.ClearFiltersCommand.Execute(null);
        await viewModel.CheckFeedHealthCommand.ExecuteAsync(viewModel.Feeds.Single(feed => feed.Id == nasaSpace.Id));
        await viewModel.CheckFeedHealthCommand.ExecuteAsync(viewModel.Feeds.Single(feed => feed.Id == nasaMissions.Id));

        viewModel.SelectedHealthFilter = viewModel.HealthFilterOptions.Single(option => option.Filter == CatalogFeedHealthFilter.Succeeded);
        Assert.AreEqual(nasaSpace.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
        viewModel.SelectedHealthFilter = viewModel.HealthFilterOptions.Single(option => option.Filter == CatalogFeedHealthFilter.Failed);
        Assert.AreEqual(nasaMissions.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
        viewModel.SelectedHealthFilter = viewModel.HealthFilterOptions.Single(option => option.Filter == CatalogFeedHealthFilter.NotChecked);
        Assert.AreEqual(technologyNews.Id, viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
    }

    [TestMethod]
    public async Task CatalogMasterCanFilterFiveThousandFeeds()
    {
        var store = new MemoryCatalogStore();
        for (var index = 0; index < 5_000; index++)
        {
            var feedId = $"feed-{index:D5}";
            await store.AddFeedAsync(new CatalogFeed(
                feedId,
                $"Feed {index:D5}",
                $"https://feeds.example/{index:D5}.xml",
                null,
                null));
        }

        var viewModel = new CatalogManagementViewModel(
            Profile.CreateCatalogMaster(),
            new CatalogService(store));
        await viewModel.InitializeAsync();

        Assert.AreEqual(5_000, viewModel.VisibleFeedCount);
        viewModel.SearchQuery = "04999";

        Assert.AreEqual(1, viewModel.VisibleFeedCount);
        Assert.AreEqual("feed-04999", viewModel.VisibleFeeds.Cast<CatalogFeedListItem>().Single().Id);
        Assert.AreEqual("Showing 1 of 5000 feeds", viewModel.FeedResultsSummary);
    }

    [TestMethod]
    public async Task CatalogMasterCanCheckFeedAndDisplayTimestampedHealth()
    {
        var store = new MemoryCatalogStore();
        var actor = Profile.CreateCatalogMaster();
        var catalogService = new CatalogService(store, new TestFeedDownloader());
        var feed = await catalogService.AddFeedAsync(
            actor,
            "Checkable",
            "https://example.com/feed.xml",
            null,
            null);
        var viewModel = new CatalogManagementViewModel(actor, catalogService);
        await viewModel.InitializeAsync();
        var item = viewModel.Feeds.Single();

        Assert.AreEqual("Not checked", item.HealthCheckDisplay);

        await viewModel.CheckFeedHealthCommand.ExecuteAsync(item);

        var checkedItem = viewModel.Feeds.Single();
        Assert.IsTrue(checkedItem.LastHealthCheckSucceeded);
        Assert.IsNotNull(checkedItem.LastHealthCheckedAt);
        StringAssert.StartsWith(checkedItem.HealthCheckDisplay, "Feed valid - checked ");
    }

    [TestMethod]
    public async Task CheckAllVisibleFeedsDisablesFailedAndNearlyEmptySourcesGlobally()
    {
        var store = new MemoryCatalogStore();
        var actor = Profile.CreateCatalogMaster();
        var downloader = new TestFeedDownloader(
            shouldFail: feed => feed.Name == "Scan error",
            items: feed => Enumerable.Range(1, feed.Name switch
            {
                "Scan healthy" => 3,
                "Scan two entries" => 2,
                "Scan one entry" => 1,
                _ => 4
            })
            .Select(index => new DownloadedFeedItem(
                $"article-{feed.Id}-{index}",
                $"Entry {index}",
                null,
                null,
                null,
                null))
            .ToArray());
        var catalogService = new CatalogService(store, downloader);
        foreach (var name in new[]
        {
            "Scan healthy",
            "Scan two entries",
            "Scan one entry",
            "Scan error",
            "Hidden source"
        })
        {
            await catalogService.AddFeedAsync(
                actor,
                name,
                $"https://example.com/{Uri.EscapeDataString(name)}.xml",
                null,
                null);
        }

        var viewModel = new CatalogManagementViewModel(actor, catalogService);
        await viewModel.InitializeAsync();
        viewModel.SearchQuery = "Scan";
        var progressReports = new List<CatalogFeedScanProgress>();

        var disabledCount = await viewModel.CheckAllVisibleFeedsAsync(
            new DelegateProgress<CatalogFeedScanProgress>(progressReports.Add));

        Assert.AreEqual(3, disabledCount);
        var feeds = (await catalogService.GetFeedsAsync()).ToDictionary(feed => feed.Name, StringComparer.Ordinal);
        Assert.IsTrue(feeds["Scan healthy"].IsEnabled);
        Assert.AreEqual(true, feeds["Scan healthy"].LastHealthCheckSucceeded);
        Assert.IsFalse(feeds["Scan two entries"].IsEnabled);
        Assert.AreEqual(true, feeds["Scan two entries"].LastHealthCheckSucceeded);
        Assert.IsFalse(feeds["Scan one entry"].IsEnabled);
        Assert.IsFalse(feeds["Scan error"].IsEnabled);
        Assert.AreEqual(false, feeds["Scan error"].LastHealthCheckSucceeded);
        Assert.IsTrue(feeds["Hidden source"].IsEnabled);
        Assert.IsNull(feeds["Hidden source"].LastHealthCheckedAt);
        Assert.AreEqual(4, progressReports.Where(report => report.CompletedCount == report.TotalCount).Last().TotalCount);
        Assert.AreEqual(3, progressReports.Last().DisabledCount);
    }

    [TestMethod]
    public async Task CheckCategoryFeedsOnlyChecksAndDisablesFeedsInSelectedCategory()
    {
        var store = new MemoryCatalogStore();
        var actor = Profile.CreateCatalogMaster();
        var downloader = new TestFeedDownloader(
            shouldFail: feed => feed.Name == "Selected error",
            items: feed => Enumerable.Range(1, feed.Name switch
            {
                "Selected healthy" => 3,
                "Selected sparse" => 2,
                _ => 4
            })
            .Select(index => new DownloadedFeedItem(
                $"article-{feed.Id}-{index}",
                $"Entry {index}",
                null,
                null,
                null,
                null))
            .ToArray());
        var catalogService = new CatalogService(store, downloader);
        await catalogService.ImportFeedsAsync(actor,
        [
            new OpmlFeed("Selected healthy", "https://example.com/selected-healthy.xml", null, "Selected"),
            new OpmlFeed("Selected sparse", "https://example.com/selected-sparse.xml", null, "Selected"),
            new OpmlFeed("Selected error", "https://example.com/selected-error.xml", null, "Selected"),
            new OpmlFeed("Other category", "https://example.com/other.xml", null, "Other")
        ]);
        var viewModel = new CatalogManagementViewModel(actor, catalogService);
        await viewModel.InitializeAsync();
        var category = viewModel.Categories.Single(candidate => candidate.Name == "Selected");
        var progressReports = new List<CatalogFeedScanProgress>();

        var disabledCount = await viewModel.CheckCategoryFeedsAsync(
            category.Id,
            new DelegateProgress<CatalogFeedScanProgress>(progressReports.Add));

        Assert.AreEqual(2, disabledCount);
        var feeds = (await catalogService.GetFeedsAsync()).ToDictionary(feed => feed.Name, StringComparer.Ordinal);
        Assert.IsTrue(feeds["Selected healthy"].IsEnabled);
        Assert.AreEqual(true, feeds["Selected healthy"].LastHealthCheckSucceeded);
        Assert.IsFalse(feeds["Selected sparse"].IsEnabled);
        Assert.IsFalse(feeds["Selected error"].IsEnabled);
        Assert.AreEqual(false, feeds["Selected error"].LastHealthCheckSucceeded);
        Assert.IsTrue(feeds["Other category"].IsEnabled);
        Assert.IsNull(feeds["Other category"].LastHealthCheckedAt);
        Assert.AreEqual(3, progressReports.Where(report => report.CompletedCount == report.TotalCount).Last().TotalCount);
        Assert.AreEqual(2, progressReports.Last().DisabledCount);
    }

    [TestMethod]
    public async Task CheckAllVisibleFeedsStopsOnCancellation()
    {
        var store = new MemoryCatalogStore();
        var actor = Profile.CreateCatalogMaster();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalogService = new CatalogService(store, new BlockingFeedDownloader(started));
        var first = await catalogService.AddFeedAsync(
            actor,
            "First feed",
            "https://example.com/first.xml",
            null,
            null);
        var second = await catalogService.AddFeedAsync(
            actor,
            "Second feed",
            "https://example.com/second.xml",
            null,
            null);
        var viewModel = new CatalogManagementViewModel(actor, catalogService);
        await viewModel.InitializeAsync();
        using var cancellation = new CancellationTokenSource();

        var scan = viewModel.CheckAllVisibleFeedsAsync(
            new DelegateProgress<CatalogFeedScanProgress>(_ => { }),
            cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => scan);
        var feeds = (await catalogService.GetFeedsAsync()).ToDictionary(feed => feed.Id, StringComparer.Ordinal);
        Assert.IsTrue(feeds[first.Id].IsEnabled);
        Assert.IsTrue(feeds[second.Id].IsEnabled);
        Assert.IsNull(feeds[first.Id].LastHealthCheckedAt);
        Assert.IsNull(feeds[second.Id].LastHealthCheckedAt);
    }

    [TestMethod]
    public async Task FailedFeedHealthCheckDisplaysDiagnosticDetails()
    {
        var store = new MemoryCatalogStore();
        var actor = Profile.CreateCatalogMaster();
        var catalogService = new CatalogService(store, new TestFeedDownloader(_ => true));
        await catalogService.AddFeedAsync(actor, "Unavailable", "https://example.com/feed.xml", null, null);
        var viewModel = new CatalogManagementViewModel(actor, catalogService);
        await viewModel.InitializeAsync();

        await viewModel.CheckFeedHealthCommand.ExecuteAsync(viewModel.Feeds.Single());

        StringAssert.Contains(viewModel.ErrorMessage, "Could not check feed 'Unavailable'");
        StringAssert.Contains(viewModel.ErrorMessage, "Feed check failed.");
    }

    private sealed class MemoryCatalogStore : ICatalogStore
    {
        private readonly List<CatalogFeed> _feeds = [];
        private readonly List<CatalogCategory> _categories = [];
        private readonly List<CatalogCollection> _collections = [];
        private readonly HashSet<(string CollectionId, string FeedId)> _memberships = [];

        public IReadOnlyList<string> CollectionFeedIds => _memberships.Select(item => item.FeedId).ToArray();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CatalogFeed>> GetFeedsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogFeed>>(_feeds.ToArray());
        public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogCategory>>(_categories.ToArray());
        public Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogCollection>>(_collections.ToArray());
        public Task<IReadOnlyList<string>> GetCollectionFeedIdsAsync(string collectionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(_memberships.Where(item => item.CollectionId == collectionId).Select(item => item.FeedId).ToArray());
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetCollectionFeedIdsByCollectionAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(_memberships.GroupBy(item => item.CollectionId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(item => item.FeedId).ToArray(), StringComparer.Ordinal));

        public Task AddFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default)
        {
            _feeds.Add(feed);
            return Task.CompletedTask;
        }

        public Task UpdateFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default)
        {
            var index = _feeds.FindIndex(item => item.Id == feed.Id);
            if (index >= 0)
            {
                _feeds[index] = feed;
            }

            return Task.CompletedTask;
        }

        public Task DeleteFeedAsync(string feedId, CancellationToken cancellationToken = default)
        {
            _feeds.RemoveAll(item => item.Id == feedId);
            _memberships.RemoveWhere(item => item.FeedId == feedId);
            return Task.CompletedTask;
        }

        public Task AddCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default)
        {
            _categories.Add(category);
            return Task.CompletedTask;
        }

        public Task UpdateCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default)
        {
            var index = _categories.FindIndex(item => item.Id == category.Id);
            if (index >= 0)
            {
                _categories[index] = category;
            }

            return Task.CompletedTask;
        }

        public Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
        {
            _categories.RemoveAll(item => item.Id == categoryId);
            return Task.CompletedTask;
        }

        public Task MergeCategoriesAsync(string sourceCategoryId, string targetCategoryId, CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < _feeds.Count; index++)
            {
                if (_feeds[index].CategoryId == sourceCategoryId)
                {
                    _feeds[index] = _feeds[index] with { CategoryId = targetCategoryId };
                }
            }

            _categories.RemoveAll(category => category.Id == sourceCategoryId);
            return Task.CompletedTask;
        }

        public Task AddCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default)
        {
            _collections.Add(collection);
            return Task.CompletedTask;
        }

        public Task UpdateCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default)
        {
            var index = _collections.FindIndex(item => item.Id == collection.Id);
            if (index >= 0)
            {
                _collections[index] = collection;
            }

            return Task.CompletedTask;
        }

        public Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken = default)
        {
            _collections.RemoveAll(item => item.Id == collectionId);
            _memberships.RemoveWhere(item => item.CollectionId == collectionId);
            return Task.CompletedTask;
        }

        public Task AddFeedToCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default)
        {
            _memberships.Add((collectionId, feedId));
            return Task.CompletedTask;
        }

        public Task RemoveFeedFromCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default)
        {
            _memberships.Remove((collectionId, feedId));
            return Task.CompletedTask;
        }
    }

    private sealed class TestFeedDownloader(
        Func<CatalogFeed, bool>? shouldFail = null,
        Func<CatalogFeed, IReadOnlyList<DownloadedFeedItem>>? items = null) : IFeedDownloader
    {
        public Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default) => shouldFail?.Invoke(feed) == true
                ? Task.FromException<IReadOnlyList<DownloadedFeedItem>>(new InvalidOperationException("Feed check failed."))
                : Task.FromResult(items?.Invoke(feed) ?? (IReadOnlyList<DownloadedFeedItem>)[]);
    }

    private sealed class BlockingFeedDownloader(TaskCompletionSource started) : IFeedDownloader
    {
        public async Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }

    private sealed class DelegateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class PreviewFeedDownloader : IFeedDownloader
    {
        public Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadedFeedItem>>(Enumerable.Range(1, 6)
                .Select(index => new DownloadedFeedItem(
                    $"article-{index}",
                    $"Article {index}",
                    $"https://example.com/{index}",
                    DateTimeOffset.UtcNow,
                    "Sample summary",
                    "Full content that should not be previewed",
                    "https://example.com/image.jpg"))
                .ToArray());
    }
}