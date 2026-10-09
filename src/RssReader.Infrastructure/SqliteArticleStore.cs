using System.Globalization;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

internal sealed class SqliteArticleStore(SqliteConnectionFactory connections)
{
    public async Task<IReadOnlyList<ArticleForProfile>> GetArticlesAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        var categoriesByArticle = new Dictionary<string, List<ArticleCategory>>(StringComparer.Ordinal);
        await using (var categoriesCommand = connection.CreateCommand())
        {
            categoriesCommand.CommandText = """
                SELECT category.ArticleId, category.Term, category.Scheme, category.Label
                FROM ArticleCategories AS category
                INNER JOIN Articles AS article ON article.Id = category.ArticleId
                INNER JOIN ProfileSubscriptions AS subscription ON subscription.FeedId = article.FeedId
                INNER JOIN CatalogFeeds AS feed ON feed.Id = article.FeedId
                WHERE subscription.ProfileId = $profileId
                  AND (
                      feed.IsSharedCatalog = 1
                      OR EXISTS (
                          SELECT 1 FROM ProfileFeedOwners AS owner
                          WHERE owner.FeedId = feed.Id AND owner.ProfileId = subscription.ProfileId
                      )
                  )
                ORDER BY category.Term COLLATE NOCASE;
                """;
            categoriesCommand.Parameters.AddWithValue("$profileId", profileId);
            await using var categoriesReader = await categoriesCommand.ExecuteReaderAsync(cancellationToken);
            while (await categoriesReader.ReadAsync(cancellationToken))
            {
                var articleId = categoriesReader.GetString(0);
                if (!categoriesByArticle.TryGetValue(articleId, out var categories))
                {
                    categories = [];
                    categoriesByArticle.Add(articleId, categories);
                }

                var scheme = categoriesReader.GetString(2);
                categories.Add(new ArticleCategory(
                    categoriesReader.GetString(1),
                    scheme.Length == 0 ? null : scheme,
                    categoriesReader.IsDBNull(3) ? null : categoriesReader.GetString(3)));
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT article.Id, article.FeedId, article.ExternalId, article.Title, article.Link,
                     article.PublishedAt, article.Summary, article.Content, article.ImageUrl, feed.Name,
                   subscription.FolderName, state.IsRead, state.IsSaved
                   , article.Author
            FROM Articles AS article
            INNER JOIN ProfileSubscriptions AS subscription ON subscription.FeedId = article.FeedId
            INNER JOIN CatalogFeeds AS feed ON feed.Id = article.FeedId
            LEFT JOIN ProfileArticleStates AS state
                ON state.ArticleId = article.Id AND state.ProfileId = subscription.ProfileId
            WHERE subscription.ProfileId = $profileId
              AND (
                  feed.IsSharedCatalog = 1
                  OR EXISTS (
                      SELECT 1 FROM ProfileFeedOwners AS owner
                      WHERE owner.FeedId = feed.Id AND owner.ProfileId = subscription.ProfileId
                  )
              )
            ORDER BY article.PublishedAt DESC;
            """;
        command.Parameters.AddWithValue("$profileId", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var articles = new List<ArticleForProfile>();
        while (await reader.ReadAsync(cancellationToken))
        {
            DateTimeOffset? publishedAt = reader.IsDBNull(5)
                ? null
                : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var article = new FeedArticle(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                publishedAt,
                reader.IsDBNull(6) ? null : HtmlTextParser.ToPlainText(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8))
            {
                Categories = categoriesByArticle.GetValueOrDefault(reader.GetString(0)) ?? [],
                Author = reader.IsDBNull(13) ? null : reader.GetString(13)
            };
            articles.Add(new ArticleForProfile(
                article,
                reader.GetString(9),
                reader.GetString(10),
                !reader.IsDBNull(11) && reader.GetInt64(11) == 1,
                !reader.IsDBNull(12) && reader.GetInt64(12) == 1));
        }

        return articles;
    }

    public async Task<int> SaveArticlesAsync(
        string feedId,
        IReadOnlyList<FeedArticle> articles,
        CancellationToken cancellationToken)
    {
        if (articles.Any(article => article.FeedId != feedId))
        {
            throw new ArgumentException("All articles must belong to the specified feed.", nameof(articles));
        }

        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var addedCount = 0;
        foreach (var article in articles)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Articles (Id, FeedId, ExternalId, Title, Link, PublishedAt, Summary, Content, ImageUrl, Author)
                VALUES ($id, $feedId, $externalId, $title, $link, $publishedAt, $summary, $content, $imageUrl, $author)
                ON CONFLICT(Id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$id", article.Id);
            command.Parameters.AddWithValue("$feedId", feedId);
            command.Parameters.AddWithValue("$externalId", article.ExternalId);
            command.Parameters.AddWithValue("$title", article.Title);
            command.Parameters.AddWithValue("$link", (object?)article.Link ?? DBNull.Value);
            command.Parameters.AddWithValue("$publishedAt", article.PublishedAt?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$summary", (object?)article.Summary ?? DBNull.Value);
            command.Parameters.AddWithValue("$content", (object?)article.Content ?? DBNull.Value);
            command.Parameters.AddWithValue("$imageUrl", (object?)article.ImageUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$author", (object?)article.Author ?? DBNull.Value);
            var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
            if (inserted > 0)
            {
                addedCount += inserted;
            }
            else
            {
                command.CommandText = """
                    UPDATE Articles SET
                        Title = $title,
                        Link = $link,
                        PublishedAt = $publishedAt,
                        Summary = $summary,
                        Content = $content,
                        ImageUrl = $imageUrl,
                        Author = $author
                    WHERE Id = $id;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteCategoriesCommand = connection.CreateCommand())
            {
                deleteCategoriesCommand.Transaction = transaction;
                deleteCategoriesCommand.CommandText = "DELETE FROM ArticleCategories WHERE ArticleId = $articleId;";
                deleteCategoriesCommand.Parameters.AddWithValue("$articleId", article.Id);
                await deleteCategoriesCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var category in article.Categories)
            {
                if (string.IsNullOrWhiteSpace(category.Term))
                {
                    continue;
                }

                await using var categoryCommand = connection.CreateCommand();
                categoryCommand.Transaction = transaction;
                categoryCommand.CommandText = """
                    INSERT OR IGNORE INTO ArticleCategories (ArticleId, Term, Scheme, Label)
                    VALUES ($articleId, $term, $scheme, $label);
                    """;
                categoryCommand.Parameters.AddWithValue("$articleId", article.Id);
                categoryCommand.Parameters.AddWithValue("$term", category.Term.Trim());
                categoryCommand.Parameters.AddWithValue("$scheme", category.Scheme?.Trim() ?? string.Empty);
                categoryCommand.Parameters.AddWithValue("$label", (object?)category.Label?.Trim() ?? DBNull.Value);
                await categoryCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return addedCount;
    }

    public Task SetArticleReadAsync(
        string profileId,
        string articleId,
        bool isRead,
        CancellationToken cancellationToken) => connections.ExecuteAsync(
        """
        INSERT INTO ProfileArticleStates (ProfileId, ArticleId, IsRead, IsSaved)
        VALUES ($profileId, $articleId, $isRead, 0)
        ON CONFLICT(ProfileId, ArticleId) DO UPDATE SET IsRead = excluded.IsRead;
        """,
        cancellationToken,
        ("$profileId", profileId),
        ("$articleId", articleId),
        ("$isRead", isRead ? 1 : 0));

    public Task SetArticleSavedAsync(
        string profileId,
        string articleId,
        bool isSaved,
        CancellationToken cancellationToken) => connections.ExecuteAsync(
        """
        INSERT INTO ProfileArticleStates (ProfileId, ArticleId, IsRead, IsSaved)
        VALUES ($profileId, $articleId, 0, $isSaved)
        ON CONFLICT(ProfileId, ArticleId) DO UPDATE SET IsSaved = excluded.IsSaved;
        """,
        cancellationToken,
        ("$profileId", profileId),
        ("$articleId", articleId),
        ("$isSaved", isSaved ? 1 : 0));
}
