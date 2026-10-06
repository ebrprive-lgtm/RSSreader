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

- Model source kind explicitly for Reddit; do not infer a provider from an arbitrary URL in multiple layers.
- Treat flair as publisher-provided **article metadata**, distinct from RSS/Atom **Topics** and profile-owned **Feed tags**. Do not store flair in `ArticleCategories` or `ProfileFeedTags`.
- A feed refresh updates the flair for each returned article, including clearing a previously stored flair when the source no longer provides one. Articles omitted from a refresh retain their cached data.
- Start with one selected flair per article-list view. Flair filtering composes with, and clearing it preserves, all other active filters.
- Trim flair values and ignore missing/blank values. Match and deduplicate case-insensitively while preserving source display casing.
- Unflairied posts remain visible when no flair is selected. Do not add a "No flair" option in the first release.
- Offer flair options/counts from the loaded article scope after the other active filters are applied; selecting a flair narrows that scope, and clearing it does not reset those other filters.
- Do not scrape Reddit HTML or rely on an unauthenticated `.json` endpoint. Any API use must follow Reddit's current access terms and must not ship a client secret in the desktop application.
- Automated tests use deterministic fixtures and fake HTTP responses. They must not require Reddit credentials or live network access.

## Phase 0 Findings (2026-10-05)

- A live, read-only compatibility probe of `https://www.reddit.com/r/dotnet/.rss` and `https://www.reddit.com/r/technology/.rss` returned Atom feeds (25 entries sampled per subreddit). The entries exposed subreddit-level categories but no distinct per-post flair metadata; the native Atom feed is therefore insufficient for a reliable flair filter.
- An unauthenticated request to `https://www.reddit.com/r/dotnet/.json` with a descriptive User-Agent returned HTTP 403 in the probe. This is an observation, not proof that every unauthenticated request is universally blocked.
- Reddit's [Data API Wiki](https://support.reddithelp.com/hc/en-us/articles/16160319875092-Reddit-Data-API-Wiki) and [API reference](https://www.reddit.com/dev/api/) are the primary references for confirming current OAuth, registration, User-Agent, quota, and endpoint requirements before implementation. The API is the likely first-party route for `link_flair_text`, but access is not yet demonstrated for this application.
- Reddit's [Data API Terms](https://redditinc.com/policies/data-api-terms) sections 2.4, 2.5, 2.8, and 2.9 limit copying/displaying user content to what is needed to run the app, govern removal requests, require use of Reddit-provided access information (including OAuth tokens), and reserve the right to impose API limits. Local offline caching and removal obligations must be checked against the complete current documentation before implementation.
- The Data API Wiki also discusses removing content that is removed on Reddit and recommends routinely deleting stored user data/content within 48 hours. The current reader keeps cached articles for offline use; a scan of [`SqliteReaderStore`](../src/RssReader.Infrastructure/SqliteReaderStore.cs) found refresh updates but no article-pruning/deletion path. The exact obligation, practical deletion mechanism, and compatibility with offline reading require explicit review; do not assume that a refresh of only the newest posts is enough.
- **Retention strategy to investigate, not adopt yet:** determine whether a currently supported API can revalidate stored submission IDs in batches, how deleted/removed content is represented, and whether the request cost fits Reddit's limits. A possible fail-safe is to purge deleted content promptly and stop displaying cached Reddit content once its verification age exceeds the policy window when revalidation is unavailable. That would weaken offline reading and needs policy and product approval.
- Follow-up requests to other subreddit feeds received HTTP 429 during the probe. This reinforces the need to define request pacing and visible retry/error behavior; it does not establish a stable quota.
- **V1 source scope:** a Reddit feed represents one public subreddit and its newest-post listing. Defer hot/top listings, subreddit search, multireddits, user feeds, comments, and historical backfill.
- **V1 catalog input:** add an explicit Reddit source choice. Accept a canonical Reddit subreddit URL (`reddit.com/r/<name>`, optional `www` and trailing slash); normalize host, case, and slash for duplicate detection; display it as `r/<name>`. Reject post/comment URLs and unrelated hosts. Keep the initial listing fixed to newest posts rather than exposing sort configuration.
- **OAuth candidate, not yet verified:** register a Reddit installed/native app and use a user-approved authorization-code flow with the minimum read-only scope. A native/installed client should not require a client secret in the desktop binary; keep refresh tokens out of the SQLite reader database and use OS-protected storage. Confirm Reddit currently permits this flow and the application before relying on it. If registration or approval is unavailable, defer API-backed support.
- **Safe working disposition:** use the V1 subreddit/newest scope above as the planning baseline. Do not substitute a third-party proxy or a flair-less native Atom feed for the requested flair-filter experience. Reddit access and cache compliance are mandatory stop/go checks before any production-code change.

## Phase 0 Decision

**Status: Complete — conditional GO to a bounded Phase 1 technical proof only. This is not approval to begin production implementation.**

- The proposed source is one public subreddit, newest-post listing only, entered as a canonical subreddit URL. Search, other listings, user feeds, multireddits, comments, and backfill are out of V1 scope.
- The native subreddit Atom feed is not sufficient for the required per-post flair filter. The first-party Data API is the only proposed source; do not scrape Reddit pages, use an unauthenticated API endpoint, or depend on a third-party relay.
- The access candidate is a registered installed/native OAuth client with user authorization and minimum read-only scope. Do not ship a client secret; keep any refresh token in OS-protected storage. Current approval, exact flow, and access remain unverified.
- Flair is article metadata, separate from Topics and Feed tags. The first UI uses one trimmed, case-insensitive flair selection, source-cased display values, current-scope counts, and independent clearing. Unflairied posts remain in the unfiltered view.
- Reddit article persistence is **not approved** yet. Data API terms/removal rules may conflict with the reader's indefinite offline archive, which currently has no article-pruning path. Phase 1 must verify an allowed retention/revalidation strategy before enabling persistence.
- The facts that cannot be verified without registered/authorized API access (OAuth, real flair fields, deletion status, and rate behavior) are moved to the Phase 1 stop/go gate. If that gate fails, stop the feature rather than weaken the source or retention requirements.

This conditional decision closes discovery and scope-setting. It keeps unresolved external access/policy checks explicit and prevents them from being mistaken for implementation approval.

## Phased Roadmap

### Phase 0: Verify Source Access and Lock Scope

**Goal:** Select a supportable Reddit source scope, UX, semantics, and implementation gate.

- [x] Verify current Reddit access options for public read-only feeds and identify that native subreddit Atom responses sampled do not expose a distinct post flair; an authenticated API remains the likely first-party option.
- [x] Confirm from the current [Reddit Data API Wiki](https://support.reddithelp.com/hc/en-us/articles/16160319875092-Reddit-Data-API-Wiki) that API use is OAuth-authenticated and requires a registered app and descriptive User-Agent; follow the documented rate-limit signals rather than hard-coding a presumed global quota. No credentials or application registration are present in this repository.
- [x] Record installed/native OAuth as the candidate access model, with an explicit Phase 1 stop/go check for current flow support, registration/approval, and least-privilege scope. Do not embed a client secret; store tokens outside SQLite.
- [x] Review the current Data API Terms and Wiki: the content license is limited to copying/displaying content as needed to run the app, removal requirements apply, and routine deletion within 48 hours is recommended. This is not blanket authorization for indefinite offline retention.
- [x] Record the cache/retention incompatibility risk and set the decision: do not persist Reddit articles until a compliant, rate-feasible removal/revalidation strategy is proven; if it cannot be proven, stop Reddit support.
- [x] Record authorized API samples for flair-present, no-flair, Unicode, changed, and removed posts as Phase 1 gate evidence; do not substitute guessed/live-dependent fixtures.
- [x] Define the initial supported source scope as one public subreddit newest-post listing. Defer other Reddit source types and historical backfill.
- [x] Define catalog input and identity: explicit Reddit source choice; accept a canonical subreddit URL with optional `www`/trailing slash; normalize for duplicate detection; display `r/<name>`; reject direct post/comment URLs and unrelated hosts.
- [x] Define initial flair semantics: one case-insensitive trimmed flair selection, preserve source casing for display, keep unflairied posts in the unfiltered view, offer counts in the other-filtered article scope, and clear flair independently of all other filters.

**Exit criteria:** Source limitations, V1 scope, catalog identity, flair semantics, terms risks, and an explicit implementation stop/go gate are documented. **Met.** OAuth authorization, removal/revalidation, and representative API samples are Phase 1 prerequisites, not claims of Phase 0 verification.

### Phase 1: Add Reddit Source Modeling and Ingestion

**Goal:** Download Reddit entries through the existing refresh pipeline without regressing RSS/Atom.

**Status (2026-10-05):** Gate started; blocked at approved OAuth access. No-credential requests to `oauth.reddit.com/r/dotnet/new?limit=1` and `www.reddit.com/r/dotnet/.json` returned HTTP 403. A further public `r/technology/.rss` probe returned 25 entries with only a subreddit category term and no flair marker, confirming native Atom is not a substitute. No Reddit app registration or user-authorized token has been supplied or is present in the workspace. Reddit's current terms make API access conditional on its access rules and limits. Do not proceed to production changes until authorized app access is available and the remaining gate checks can be performed.

#### Gate: Prove Access and Retention Before Production Changes

- [x] Probe public JSON/OAuth endpoints without credentials using a descriptive User-Agent; both returned HTTP 403. A separate 25-entry native Atom probe again exposed no flair metadata. No unauthenticated or flair-less fallback will be used.
- [x] Inspect local purge mechanics: `Articles` deletion cascades to `ArticleCategories` and `ProfileArticleStates` with foreign keys enabled. This can remove article metadata plus read/saved state once Reddit removal is detected; the actual deletion transaction still needs a focused test if implementation proceeds.
- [ ] Register/obtain an approved Reddit application and verify the installed/native OAuth flow with a user-authorized account and minimum read-only scope. Confirm the truthful User-Agent and observe actual rate-limit/429 behavior.
- [ ] Using authorized, deterministic captures, verify subreddit `/new` response shape, stable post IDs, links, dates, `link_flair_text`, absent/Unicode/changed flair, deleted/removed post representation, and error responses.
- [ ] Verify a supported, rate-feasible way to revalidate stored post IDs and purge removed content. Define what happens to cached Reddit content after verification becomes stale or the API is unavailable.
- [ ] Confirm the proposed Reddit cache/display/retention behavior complies with the current Data API Terms and removal guidance.
- [ ] **Stop condition:** if OAuth access, required metadata, removal detection, or a compliant retention strategy cannot be verified, do not add Reddit article persistence or continue to the implementation tasks below. Update the plan with the feasibility result.

#### Implementation (only after the gate passes)

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
