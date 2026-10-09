using RssReader.Domain;

namespace RssReader.App.ViewModels;

internal sealed record ArticleListFilterResult(
    IReadOnlyList<ArticleRowViewModel> VisibleArticles,
    IReadOnlyList<ArticleTopicOption> TopicOptions,
    ArticleTopicOption SelectedTopic);

internal static class ArticleListFilter
{
    public static ArticleListFilterResult Apply(
        IEnumerable<ArticleRowViewModel> allArticles,
        string activeRoute,
        string searchQuery,
        bool unreadOnly,
        bool savedOnly,
        ArticleTopicOption? selectedTopic,
        bool sortByFolder,
        int folderArticlesPerFeedLimit)
    {
        IEnumerable<ArticleRowViewModel> articles = allArticles;
        if (activeRoute == "Read later")
        {
            articles = articles.Where(article => article.IsSaved);
        }
        else if (activeRoute == "Recently read")
        {
            articles = articles.Where(article => article.IsRead);
        }
        else if (activeRoute.StartsWith("folder:", StringComparison.Ordinal))
        {
            var folder = activeRoute["folder:".Length..];
            articles = articles.Where(article => string.Equals(article.Folder, folder, StringComparison.OrdinalIgnoreCase));
        }
        else if (activeRoute.StartsWith("tag:", StringComparison.Ordinal))
        {
            var tag = activeRoute["tag:".Length..];
            articles = articles.Where(article => article.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        }
        else if (activeRoute.StartsWith("feed:", StringComparison.Ordinal))
        {
            var feedId = activeRoute["feed:".Length..];
            articles = articles.Where(article => article.FeedId == feedId);
        }

        if (activeRoute == "Today")
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now.AddHours(-24);
            articles = articles.Where(article => article.PublishedAt > cutoff && article.PublishedAt <= now);
        }

        var query = searchQuery.Trim();
        if (activeRoute == "Search" && string.IsNullOrEmpty(query))
        {
            articles = [];
        }
        else if (!string.IsNullOrEmpty(query))
        {
            articles = articles.Where(article =>
                article.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                article.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                article.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (article.Content?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (article.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                article.Topics.Any(topic =>
                    topic.Term.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (topic.Label?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        if (unreadOnly)
        {
            articles = articles.Where(article => !article.IsRead);
        }
        if (savedOnly)
        {
            articles = articles.Where(article => article.IsSaved);
        }

        var topicScope = articles.ToArray();
        var topicOptions = BuildTopicOptions(topicScope);
        var resolvedTopic = ResolveSelectedTopic(selectedTopic, topicOptions);
        if (resolvedTopic.Term is { } topicTerm)
        {
            articles = topicScope.Where(article => article.Topics.Any(topic =>
                string.Equals(topic.Term, topicTerm, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(topic.Scheme, resolvedTopic.Scheme, StringComparison.OrdinalIgnoreCase)));
        }

        if (sortByFolder && activeRoute.StartsWith("folder:", StringComparison.Ordinal))
        {
            articles = articles
                .GroupBy(article => article.FeedId, StringComparer.Ordinal)
                .SelectMany(feedArticles => feedArticles
                    .OrderByDescending(article => article.PublishedAt)
                    .Take(folderArticlesPerFeedLimit));
        }

        return new ArticleListFilterResult(articles.ToArray(), topicOptions, resolvedTopic);
    }

    private static List<ArticleTopicOption> BuildTopicOptions(IReadOnlyList<ArticleRowViewModel> articles)
    {
        var topicCounts = new Dictionary<string, (string Term, string? Scheme, string Name, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var article in articles)
        {
            var seenOnArticle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var topic in article.Topics)
            {
                var term = topic.Term.Trim();
                if (term.Length == 0)
                {
                    continue;
                }

                var scheme = string.IsNullOrWhiteSpace(topic.Scheme) ? null : topic.Scheme.Trim();
                var identity = $"{term}\0{scheme}";
                if (!seenOnArticle.Add(identity))
                {
                    continue;
                }

                if (topicCounts.TryGetValue(identity, out var existing))
                {
                    topicCounts[identity] = (existing.Term, existing.Scheme, existing.Name, existing.Count + 1);
                }
                else
                {
                    var name = string.IsNullOrWhiteSpace(topic.Label) ? term : topic.Label.Trim();
                    topicCounts.Add(identity, (term, scheme, name, 1));
                }
            }
        }

        var options = topicCounts.Values
            .OrderBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(topic => topic.Term, StringComparer.OrdinalIgnoreCase)
            .Select(topic => new ArticleTopicOption(topic.Term, topic.Scheme, topic.Name, topic.Count))
            .ToList();
        options.Insert(0, new ArticleTopicOption(null, null, "All topics", articles.Count));
        return options;
    }

    private static ArticleTopicOption ResolveSelectedTopic(
        ArticleTopicOption? selectedTopic,
        IReadOnlyList<ArticleTopicOption> topicOptions)
    {
        if (selectedTopic?.Term is not { } term)
        {
            return topicOptions[0];
        }

        return topicOptions.FirstOrDefault(option =>
                   string.Equals(option.Term, term, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(option.Scheme, selectedTopic.Scheme, StringComparison.OrdinalIgnoreCase))
               ?? topicOptions[0];
    }
}
