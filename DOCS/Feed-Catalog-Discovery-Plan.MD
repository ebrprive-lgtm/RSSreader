# Feed Catalog Discovery and Selection Plan

## Purpose

Turn **Follow sources** from a long, flat catalog into a fast, trustworthy way to find and follow feeds. The experience should remain useful as the catalog grows beyond its current 575 entries, help users understand what they are choosing, and fit the existing Windows, WPF, MVVM, SQLite, and local-first product architecture.

This is a product and engineering roadmap, not a commitment to build every advanced capability. Each phase should be independently useful, testable, and releasable. Validate user value before taking on metadata, network, or personalization complexity.

## Current Baseline

- The browser loads catalog feeds into `MainWindowViewModel.CatalogFeeds` and provides token-based search, category and collection filters, followed-feed hiding, and a recycling virtualized list.
- Discovery rows show feed identity, description, feed URL, optional publisher website, preview action, and Follow/Unfollow state. Catalog Master rows show publisher websites and flag names shared by multiple feeds.
- Categories and collections are represented in the domain and storage; category and collection filters compose in the regular user's browser.
- Following a single feed uses the folder-selection flow; batch follow asks once for the selected feeds.
- Catalog feeds have a name, URL, optional description, category, publisher website, and optional timestamped health-check result. Creator metadata is not modeled as a catalog field.
- Duplicate display names can appear, so additional identifying context will matter as the catalog grows.

Relevant implementation surfaces include `src/RssReader.App/MainWindow.xaml`, `src/RssReader.App/ViewModels/MainWindowViewModel.cs`, `src/RssReader.App/ViewModels/CatalogFeedListItem.cs`, `src/RssReader.Domain/CatalogFeed.cs`, `src/RssReader.Application/CatalogService.cs`, and `src/RssReader.Infrastructure/SqliteCatalogStore.cs`.

## Product Outcomes

Users should be able to:

- Find a known feed quickly using its name, creator, publisher, URL, or category.
- Explore unfamiliar feeds by category and curated collection without scanning the entire catalog.
- Distinguish similarly named feeds and make an informed choice before following.
- Follow one or several feeds with clear feedback, without repeating avoidable setup steps.
- Understand which feeds they already follow and undo or change their choices easily.

## Guiding Principles

- **Local-first and private by default:** Do not require a hosted account or send browsing, search, reading, or subscription behavior to a recommendation service. Any optional product analytics or personalization must be transparent and opt-in.
- **Curated, honest discovery:** Clearly distinguish editorial collections, objective feed metadata, and personalized recommendations. Never present an unexplained ranking as popularity or quality.
- **Fast at every size:** Avoid rendering or filtering thousands of WPF rows on the UI thread. Keep search responsive and preserve the user's place while browsing.
- **Progressive disclosure:** Put search and useful categories first; show advanced filters and preview details when needed.
- **Accessible by construction:** Support keyboard operation, visible focus, screen readers, high contrast, display scaling, and clear text labels.
- **Respect publisher behavior:** Preview only content exposed by a public feed, make network work explicit and cancelable, and avoid frequent or parallel requests caused by browsing.

## Phased Roadmap

### Phase 0: Validate the Problem and Set Baselines

**Goal:** Confirm which discovery tasks matter most before expanding the catalog model or adding recommendation features.

**Baseline worksheet:** See [Feed Catalog Discovery Baseline](Feed-Catalog-Discovery-Baseline.MD) for the screenshot/seed inventory, moderator script, tasks, scorecard, taxonomy worksheet, and catalog quality checklist. The inventory is not user-study evidence; participant results remain pending.

**Work:**

- Run short usability sessions with representative catalog tasks: find a named feed, browse a category, distinguish two similarly named feeds, and follow multiple feeds into a folder.
- Record a baseline for time-to-find, successful selection, abandonment, and cases where users cannot distinguish results. Prefer moderated tests and local test notes; do not add silent telemetry.
- Agree on the initial category names, category ownership, and what a collection means to users. Decide whether categories stay flat for the first release or need a hierarchy.
- Define a lightweight catalog quality checklist for missing descriptions, ambiguous names, invalid URLs, and duplicate or stale entries.

**Exit criteria:** The first-release category model and core user tasks are agreed; usability test scenarios and success thresholds are written down.

### Phase 1: Searchable, Category-First Catalog

**Goal:** Remove the immediate “575 feeds in one list” problem with a compact, reliable browser.

**Work:**

- Add a persistent search box matching feed name, description, category, and URL/domain using case-insensitive, whitespace-tolerant matching. Keep the query while the user follows a feed or opens and closes a category.
- Add category navigation with feed counts and an **All feeds** option. Add curated collections as a distinct navigation option only when they have a clear user-facing purpose; do not treat a collection as a category.
- Add a **Hide followed** filter, a clear/reset action, and deterministic alphabetical sorting. Do not make a popularity sort until a defensible popularity signal exists.
- Make each result easy to scan: feed title, category, concise description where available, and publisher/domain context derived from reliable metadata or the URL. Show followed status consistently and make the primary action unambiguous.
- Replace the plain `ItemsControl` list with a WPF list control configured for UI virtualization and recycling. Ensure virtualization is not defeated by an outer `ScrollViewer`; validate keyboard selection and scrolling.
- Add loading, empty catalog, no results, and recoverable error states. Keep the total count clear about whether it means catalog entries or currently filtered entries.
- Keep initial behavior simple: following a single feed continues to use the existing folder assignment flow, with clear completion feedback.

**Exit criteria:** A user can find a named item, browse by category, hide followed feeds, and return to the same search/category position after following. The list remains responsive with at least the current catalog and a generated large-catalog test fixture.

**Delivery status (October 3, 2026):** Token-based search, category and curated-collection filtering with membership counts, hide-followed, clear filters, alphabetical ordering, result summaries, distinct empty states, recoverable catalog-load errors with retry, and a recycling virtualized results list are implemented. Category and collection filters compose. Generated filtering tests cover 575, 5,000, and 25,000 feeds. Participant validation and reference-machine performance targets remain outstanding.

### Phase 2: Preview and Batch Follow

**Goal:** Help users choose confidently and reduce repetitive subscription steps.

**Work:**

- Add an on-demand feed preview, preferably in a side panel or detail view that preserves the current result list. Show feed identity and a small number of recent feed-provided article titles/dates when available.
- Keep preview requests explicit, cancelable on navigation, bounded in concurrency, and protected against repeated refreshes. Show loading, unavailable, malformed-feed, and offline states. Do not imply that preview provides full article text or access beyond what the feed publishes.
- Support multi-select within the current result set, show the selected count, and offer one batch **Follow selected** action.
- Ask for folder assignment once per batch, then report successes and failures clearly. Define partial-failure behavior and avoid duplicate subscriptions. Make it easy to clear selection and undo the completed batch where practical.
- Keep individual Follow/Unfollow actions available; multi-select must not become a required workflow.

**Exit criteria:** Users can preview a feed without losing their browse position and follow several selected feeds with one folder decision. Tests cover cancellation, duplicate follow, partial failure, and clear success/error feedback.

**Delivery status (October 3, 2026):** Batch selection/follow and feed preview are implemented. Users can select eligible feeds, choose one folder for the batch, cancel without losing selection, and retry failed items after partial success. Per-feed preview shows up to five recent titles, dates, and summaries in an inline panel; it supports refresh and cancellation, caches at most 20 successful previews per session, and does not store article content or images. Tests cover batch cancellation, partial failure, preview caps/cache/refresh, offline recovery, and cancellation on navigation. A one-time smoke check confirmed that three selected public endpoints (BBC News, NPR News, and the GitHub Blog) returned RSS 2.0 with entries; this does not establish in-app preview compatibility or future availability. Manual preview verification and usability validation remain outstanding.

### Phase 3: Catalog Quality and Curation

**Goal:** Make results more distinguishable and keep the shared catalog trustworthy as it grows.

**Work:**

- Review the catalog schema with real data before adding fields. Candidate metadata includes creator/publisher, publisher website, language, descriptive tags, and last verification outcome/date. Add only fields that improve discovery or maintenance and can be maintained accurately.
- Add safe schema migrations and update Catalog Master editing/import flows to validate, display, and preserve new metadata. Keep existing OPML imports compatible; missing optional metadata must remain valid.
- Improve duplicate handling: retain URL-based duplicate prevention, surface same-name feeds to catalog editors, and show enough publisher/domain context to users. Do not merge feeds solely because their titles match.
- Add catalog maintenance workflows for incomplete metadata, invalid or repeatedly failing feed URLs, category cleanup, and collection membership. Any health status must include when it was checked and must not imply permanent availability.
- Use collections for intentional editorial groupings, such as “Independent comics” or “Science explainers.” Label who curates them and avoid confusing them with taxonomy categories.

**Exit criteria:** Catalog editors can identify and correct common quality issues; metadata changes migrate without data loss; regular users can distinguish known same-title entries; import and catalog-management tests pass.

**Delivery status (October 3, 2026):** OPML `htmlUrl` is preserved as an optional publisher website, validated for manual entry, stored through an additive SQLite migration, displayed separately from the feed endpoint, and included in catalog search. Catalog Masters can edit existing feed metadata, filter feeds with missing descriptions/categories/publisher websites, review same-name groups without merging them, and explicitly check individual feeds. Health results persist with UTC check times and do not imply ongoing availability; URL-based duplicate prevention remains unchanged. Collection filters and management identify Catalog Master as curator. Parser, import, update, migration, and catalog-quality regressions pass. Automated or bulk health scans, actual catalog-wide cleanup, and live/user validation remain outstanding.

### Phase 4: Personal and Explainable Discovery

**Goal:** Help users discover relevant sources without turning the reader into a black box or compromising local-first privacy.

**Work:**

- Start with user-controlled interests and transparent signals such as selected categories, followed feeds, or explicit “show me more like this” actions. Keep profile data local.
- Present recommendations with a brief reason, such as “In Comics, which you browse often” or “Similar to a feed you follow.” Provide controls to dismiss, adjust interests, and disable recommendations.
- Keep recommendations separate from alphabetical and curated views. Do not describe a feed as popular unless there is a trustworthy, privacy-reviewed aggregate source.
- Treat machine-learning or hosted recommendation services as a separate future proposal requiring explicit consent, threat/privacy review, and a clear offline fallback. They are not a dependency for catalog discovery.

**Exit criteria:** Recommendations are optional, explainable, adjustable, and local by default. Users can complete all discovery tasks with recommendations disabled.

**Status (October 3, 2026):** Deferred pending Phase 0 evidence that users want recommendations and agreement on transparent, locally maintainable signals. No ranking behavior has been added without that evidence.

### Phase 5: Scale, Accessibility, and Operational Polish

**Goal:** Keep the discovery experience dependable as data volume and feature depth increase.

**Work:**

- If the catalog outgrows in-memory loading, add application-level filtering/paging or indexed database queries with stable sort order. Measure before introducing server-style complexity to a local SQLite app.
- Debounce search, cancel superseded work, and verify that filtering and navigation never block the WPF dispatcher. Preserve category, query, sort, filter, selection, and scroll state for sensible back-navigation.
- Audit keyboard-only use, screen-reader names, focus order, high-contrast colors, 100%-200% display scaling, and narrow/resized windows. Provide non-color status cues and predictable focus after follow/unfollow.
- Add end-to-end coverage for the complete browse, search, preview, follow, and return workflow; retain deterministic fake feeds and temporary SQLite databases.
- Establish a small performance test fixture at several catalog sizes (for example, 575, 5,000, and 25,000 entries). Record initial display, search response, and scroll behavior on a documented reference machine.

**Exit criteria:** Performance and accessibility checks meet documented targets; UI remains responsive during search and preview; core catalog workflows pass app-level tests.

**Work status (October 3, 2026):** The deterministic catalog filter fixture now exercises 575, 5,000, and 25,000 generated entries and reports population and filter durations without imposing machine-dependent thresholds. A SQLite-backed integration test covers browse, search, preview, follow, and return. WPF virtualization wiring and catalog screen-reader names/keyboard focus are covered by control tests. Reference-machine measurements, visual scrolling, broader application accessibility review, and numeric targets remain outstanding pending Phase 0 validation and a documented test machine.

## Success Measures

Set numeric targets after Phase 0 rather than guessing. Track these through usability sessions and deterministic performance tests:

- Median time to find and follow a known feed.
- Task completion rate for known-feed search, category exploration, and multi-feed follow.
- Frequency of wrong-feed selection in same-title scenarios.
- Search response and first-result render time at agreed catalog sizes.
- Preview load/cancel/error rates in controlled tests, without collecting user browsing history.
- Keyboard and screen-reader completion of core workflows.

Do not add a remote analytics pipeline to measure these metrics by default. If opt-in diagnostics are ever proposed, specify the exact data, retention, consent, and off switch before implementation.

## Cross-Cutting Engineering Requirements

- Preserve MVVM boundaries: WPF views render state; application services own browse/preview/subscription workflows; storage and HTTP remain behind existing abstractions.
- Add focused unit tests for search normalization, category/collection filtering, followed-state filtering, sorting, selection, and command state. Add application tests for batch subscription and preview orchestration.
- Add infrastructure tests for any new catalog migration or preview HTTP behavior using temporary SQLite databases and fake HTTP responses. No live feed dependency in automated tests.
- Add WPF-level tests for important control wiring, focus behavior, empty/error states, virtualization configuration, and the complete user workflow. Do not test screenshots or individual pixels as a substitute for behavior.
- Keep cancellation, per-feed failures, duplicate subscription handling, and profile boundaries explicit. Browsing one unavailable feed must not break the rest of the catalog.
- Update `DOCS/Architecture.MD` when a product or data-boundary decision from this roadmap is accepted; do not silently widen the current local-first scope.

## Open Decisions

- Should the category taxonomy remain flat, or do real catalog examples justify parent/child categories?
- Are collections editorial sets maintained by Catalog Master, user-created personal lists, or both? Keep these concepts separate if ownership differs.
- Which source will maintain publisher/creator and feed health metadata, and how stale can that information be before it is hidden?
- Should feed preview fetch on demand from the publisher, or should a safe cached preview be introduced later?
- What are the minimum desktop accessibility and large-catalog performance targets for the supported machines?
- Is any recommendation behavior valuable enough to justify implementation after the non-personalized browser is complete?

## Recommended Delivery Order

Deliver Phase 1 as the first user-facing release: search, category navigation, hide-followed, better row identity, preserved browsing state, and virtualization. Follow with Phase 2 only after validating that preview and batch follow reduce user effort. Improve catalog data and editor workflows in Phase 3 before considering personalization. Phases 4 and 5 should be driven by demonstrated user value and measured catalog growth, not by novelty alone.
