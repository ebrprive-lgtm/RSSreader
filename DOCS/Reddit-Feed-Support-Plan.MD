# Reddit Feed Support Plan

## Purpose

Add Reddit as a first-class feed source so readers can follow Reddit content and filter loaded entries by their post **Flair**. This is an implementation roadmap; complete the source-access and scope decisions before changing production code.

## Current Baseline

- The catalog represents a feed with a name and generic `FeedUrl`; it has no source/provider discriminator or Reddit-specific configuration.
- `SyndicationFeedDownloader` fetches RSS/Atom XML and maps syndication items to `DownloadedFeedItem`. `FeedRefreshService` refreshes every subscribed catalog feed through the same `IFeedDownloader`.
- Articles already carry publisher categories/topics, which are persisted separately from article fields. Flair is not currently modeled.
- `ArticleFilter` and the reader UI support article filters including topics, feed, folder, search, read, and saved state. There is no flair filter.
- Catalog add/edit and OPML import operate on generic feed URLs. Existing article/topic and profile-assigned feed-tag concepts must remain unchanged.
- Relevant surfaces include `CatalogFeed`, `DownloadedFeedItem`, `FeedArticle`, `ArticleFilter`, `CatalogService`, `FeedRefreshService`, `SqliteCatalogStore`, `SqliteReaderStore`, `CatalogManagementViewModel`, `MainWindowViewModel`, and `MainWindow.xaml`.

## Product Outcomes

Users should be able to:

- Add and follow a supported Reddit source through the catalog workflow.
- Refresh and read Reddit entries alongside existing RSS/Atom entries.
- See a post's flair where article metadata is shown and filter the current article list by one flair.
- Combine the flair selection with the existing feed/folder, search, unread, and saved filters; clear flair without clearing other filters.
- Use existing feeds, saved/read state, and cached articles without loss or behavior changes.

## Product Decisions and Constraints

- Model source kind explicitly if Reddit needs behavior/configuration that cannot be represented reliably by a generic URL. Do not infer a provider from an arbitrary URL in multiple layers.
- Treat flair as publisher-provided **article metadata**, distinct from RSS/Atom **Topics** and profile-owned **Feed tags**. Do not store flair in `ArticleCategories` or `ProfileFeedTags`.
- A feed refresh updates the flair for each returned article, including clearing a previously stored flair when the source no longer provides one. Articles omitted from a refresh retain their cached data.
- Start with one selected flair per article-list view. Flair filtering composes with, and clearing it preserves, all other active filters.
- Trim flair values and ignore missing/blank values. Decide and test whether matching and option deduplication are case-insensitive; preserve the source's display casing.
- Do not commit to Reddit endpoint, authentication, source types (subreddit/listing/search), historical pagination, or request rate until Phase 0 verifies current access requirements, reliability, flair availability, and licensing/usage constraints.
- Automated tests use deterministic fixtures and fake HTTP responses. They must not require Reddit credentials or live network access.

## Phased Roadmap

### Phase 0: Verify Source Access and Lock Scope

**Goal:** Select a supportable Reddit ingestion contract before designing persistence or UI around it.

- [ ] Verify current Reddit access options for public read-only feeds and identify the method that reliably returns post IDs, links, timestamps, summaries/content, authors where available, and flair text.
- [ ] Confirm whether the selected method requires credentials, an application registration, a specific User-Agent, request throttling, or other operational configuration. Do not hard-code credentials.
- [ ] Exercise representative sources with flair present, absent, Unicode, and changed after refresh; record observed response and failure behavior without relying on live responses in tests.
- [ ] Define the initial supported source scope (for example, a subreddit and a specific listing). Explicitly defer unsupported Reddit source types rather than implying full Reddit coverage.
- [ ] Define input UX and canonical identity: accepted URL forms, normalization, duplicate detection, display name, and how invalid/private/restricted sources are reported.
- [ ] Confirm flair matching, casing, no-flair behavior, option labels/counts, and how flair filtering composes with current filters.

**Exit criteria:** The retrieval method, source scope, access/rate-limit requirements, catalog UX, and flair semantics are documented and feasible without weakening existing feed refresh behavior.

### Phase 1: Add Reddit Source Modeling and Ingestion

**Goal:** Download Reddit entries through the existing refresh pipeline without regressing RSS/Atom.

- [ ] Introduce the smallest explicit source-kind/configuration model needed to distinguish Reddit from generic RSS/Atom feeds; preserve existing constructor and database compatibility where practical.
- [ ] Add a Reddit downloader or source-specific downloader strategy behind `IFeedDownloader`, reusing shared HTTP and refresh services where appropriate instead of duplicating them.
- [ ] Parse the verified response format with bounded response size, cancellation support, stable external IDs, and explicit HTTP/format errors. Respect the Phase 0 request policy.
- [ ] Map flair into a dedicated optional article property in the downloaded/domain model. Keep topics/categories, author, title, link, date, summary, and content mappings independent.
- [ ] Apply clear, tested handling for deleted/removed/unavailable content and absent flair; do not turn failures into successful empty feeds.

**Tests:**

- [ ] Add fixture-based parsing tests for valid entries, flair values, missing flair, malformed responses, unavailable entries, and response limits.
- [ ] Add downloader tests for URL validation, status errors, cancellation, headers/throttling behavior where applicable, and deterministic HTTP responses.
- [ ] Add refresh mapping tests proving stable Reddit identities and that existing RSS/Atom mapping remains unchanged.

**Exit criteria:** Reddit and existing RSS/Atom feeds use the same refresh and error-reporting lifecycle with isolated, tested source-specific parsing.

### Phase 2: Persist Flair and Source Configuration

**Goal:** Keep Reddit source identity and article flair available offline.

- [ ] Extend catalog persistence and migration logic to store the source kind and only the Reddit configuration required by the chosen source contract.
- [ ] Add a migration-safe article flair field or dedicated metadata table, following existing SQLite conventions and query patterns. Preserve existing articles with null flair.
- [ ] Update article save transactions so returned entries atomically replace their flair value, including removal when the refreshed value is absent, without changing read/saved state.
- [ ] Read flair back with article rows and retain it through all existing article/profile projections.
- [ ] Ensure catalog updates, duplicate detection, deletion, and OPML import/export preserve or intentionally omit Reddit-specific configuration according to the Phase 0 contract.

**Tests:**

- [ ] Verify fresh database initialization and migration from a database with existing feeds, articles, categories, and profile states.
- [ ] Verify flair insert, update, clear, offline readback, and transaction rollback behavior.
- [ ] Verify existing RSS/Atom rows and OPML behavior remain backward compatible.

**Exit criteria:** Existing user data migrates without loss, and Reddit flair survives refresh/restart independently of topic and feed-tag metadata.

### Phase 3: Add Reddit to Catalog Workflows

**Goal:** Make the supported Reddit source discoverable and manageable.

- [ ] Add a clear Reddit source choice and input guidance to catalog add/edit flows; validate input through `CatalogService`, not only in the view.
- [ ] Show an understandable source name and distinguish Reddit from generic feeds in catalog and subscription surfaces where this aids management.
- [ ] Ensure preview, subscribe, refresh, edit, duplicate detection, and health/error reporting work for Reddit sources.
- [ ] Define OPML behavior for Reddit sources: retain standard OPML compatibility and avoid silently losing necessary source configuration on round trip.
- [ ] Follow existing shared control styles, keyboard interaction, and accessibility conventions.

**Tests:**

- [ ] Cover view-model/service validation for supported and unsupported Reddit URLs, duplicate sources, and edit behavior.
- [ ] Cover preview/subscription metadata and OPML round trips for the chosen representation.
- [ ] Verify existing generic feed add/edit/import workflows are unchanged.

**Exit criteria:** A user can add, preview, follow, edit, and remove a supported Reddit source with actionable validation errors.

### Phase 4: Add Flair Display and Filtering

**Goal:** Make flair useful without conflating it with topics or personal feed tags.

- [ ] Carry flair through article/profile view models and display it with an accessible label in article list/card/magazine and reading-detail surfaces where appropriate.
- [ ] Add a single-select flair filter to the article-list controls, using existing styles and clearly identifying the filter as Reddit flair.
- [ ] Build available flair options from the current article scope. Define whether option counts respect other active filters and exclude the selected flair in the same manner as the existing topic filter.
- [ ] Apply flair filtering together with feed/folder, search, unread, and saved filters; clearing flair changes only flair state.
- [ ] Define local search behavior for flair and implement consistently with existing topic/article search behavior.
- [ ] Keep articles without flair visible under the unfiltered view; if a “No flair” option is added, label and test it explicitly.

**Tests:**

- [ ] Verify flair labels and filtering for case, whitespace, duplicate values, missing values, and changed values after refresh.
- [ ] Verify filter composition and clearing does not reset feed/folder, topic, tag, search, read, or saved filters.
- [ ] Verify keyboard access, accessible names, narrow layouts, and that long flair text does not break article presentation.

**Exit criteria:** Readers can distinguish flair from topics/feed tags, select a flair to filter articles, and clear only that filter.

### Phase 5: End-to-End Verification and Release Readiness

- [ ] Run focused application, infrastructure, persistence, and view-model tests for all changed behavior.
- [ ] Run the relevant broader solution tests and build; document any pre-existing test-host limitations separately.
- [ ] Manually verify add/preview/follow/refresh/filter/clear/error workflows against deterministic test data and, if permitted, a limited real public source.
- [ ] Confirm rate-limit and access failures are visible and retryable, with no credential leakage in logs or UI.
- [ ] Review migration safety, accessibility, resize behavior, styling consistency, documentation, and `git diff --check`.
- [ ] Update architecture and user-facing help/documentation to describe supported Reddit source scope and any access limitations.

**Exit criteria:** Existing RSS/Atom behavior remains intact, migrations are safe, all automated checks pass, and supported Reddit scope and limitations are clear.
