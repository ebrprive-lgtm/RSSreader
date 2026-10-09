# RSS Reader App Refactoring Plan

## Status

Structural roadmap; execution is complete for the identified high-cohesion boundaries. This plan targets cohesion and change safety across the app; it does not authorize a rewrite or a change in product behavior. Each phase was scoped, tested, and reviewed independently.

## Purpose

Reduce the amount of unrelated behavior and UI in individual files by moving cohesive responsibilities into focused views, view models, services, and persistence components. Make feature work easier to understand and safer to test while preserving the existing architecture and user experience.

The application already has presentation, application, domain, and infrastructure projects. Refactoring should strengthen those boundaries rather than introduce another framework or split files mechanically.

## Current Size Snapshot

Source line counts observed during the initial inventory; generated files under `obj` are excluded. Counts are a prioritization aid, not a quality metric, and may change as other work is merged.

| Area | Current file | Approx. lines | Initial direction |
|---|---|---:|---|
| Main reader UI | [`MainWindow.xaml`](../src/RssReader.App/MainWindow.xaml) | 2,581 | Extract focused feature views and local resource dictionaries; leave a small shell |
| Main reader state and commands | [`MainWindowViewModel.cs`](../src/RssReader.App/ViewModels/MainWindowViewModel.cs) | 2,058 | Separate reader, navigation, source-discovery, and refresh responsibilities |
| Main window WPF glue | [`MainWindow.xaml.cs`](../src/RssReader.App/MainWindow.xaml.cs) | 841 | Keep lifecycle and genuinely WPF-specific behavior; move feature orchestration to appropriate owners |
| Catalog management state and commands | [`CatalogManagementViewModel.cs`](../src/RssReader.App/ViewModels/CatalogManagementViewModel.cs) | 817 | Separate feed, category/collection, and bulk/health workflows where boundaries are cohesive |
| Shared application resources | [`App.xaml`](../src/RssReader.App/App.xaml) | 690 | Merge focused dictionaries for shared styles, templates, and resources |
| Feed retrieval and parsing | [`SyndicationFeedDownloader.cs`](../src/RssReader.Infrastructure/SyndicationFeedDownloader.cs) | 602 | Separate transport, syndication parsing, and raw-content retrieval if the current implementation confirms these seams |
| Reader persistence | [`SqliteReaderStore.cs`](../src/RssReader.Infrastructure/SqliteReaderStore.cs) | 577 | Separate subscription organization, article/state, and refresh-history persistence behind stable contracts |
| Catalog persistence | [`SqliteCatalogStore.cs`](../src/RssReader.Infrastructure/SqliteCatalogStore.cs) | 453 | Separate catalog feed and category/collection responsibilities if this reduces coupling |
| Catalog use cases | [`CatalogService.cs`](../src/RssReader.Application/CatalogService.cs) | 425 | Consider focused services for feed, category, and collection workflows while keeping authorization consistent |
| Raw feed dialog | [`RawFeedWindow.xaml.cs`](../src/RssReader.App/RawFeedWindow.xaml.cs) | 349 | Review after the main UI extraction; retain view-specific XML and clipboard behavior near its view |

Other files should be reviewed when modified, but should not be split solely because they cross a chosen line count.

## Execution Log

- **2026-10-08 — Main-window resource extraction:** Moved 20 self-contained resources from `MainWindow.xaml` into shared, sidebar, and article resource dictionaries. Kept `SidebarLinkTemplate` in the window because its event handlers are implemented by `MainWindow`. The app build and all 99 App tests pass.
- **2026-10-08 — Article resource split:** Separated article-list styles from article presentation templates beneath the article resource dictionary. The app build and all 99 App tests pass.
- **2026-10-08 — Reader/list view extraction:** Moved the article list and reader panels into focused `UserControl`s, moved article-list focus restoration with its view, and shared visual-tree lookup logic. Updated WPF tests for the new name scopes; the App project builds and all 99 App tests pass.
- **2026-10-08 — Catalog view extraction:** Moved Follow Sources browsing and Catalog Master administration into separate `UserControl`s, along with their dialog/action handlers. Kept external-link launch behavior coordinated by the window, updated test name-scope lookups, and changed former parent-window `ElementName` bindings to explicit ancestor-window bindings. The App project builds and all 99 App tests pass.
- **2026-10-08 — Sidebar view extraction:** Moved the sidebar, navigation template, profile menu, tag editor action, and pin/peek presentation behavior into `SidebarView`; the main window now delegates sidebar state updates and profile events. Updated test lookups for the sidebar's new name scope; the App project builds and all 99 App tests pass.
- **2026-10-08 — Feed downloader decomposition:** Moved RSS/Atom mapping to `SyndicationFeedParser`, raw article matching to `RawFeedArticleSelector`, and HTTP request policy/error diagnostics to `FeedRequestClient`. The Infrastructure project builds and all 52 Infrastructure tests pass.
- **2026-10-08 — Application resource split:** Separated global brushes/converters, menu, scrollbar, selection, tab, and button styles into six purpose-based dictionaries. `App.xaml` is now a compact merge point; the App project builds and all 99 App tests pass.
- **2026-10-08 — Article filtering extraction:** Moved route, search, read/saved, topic, and folder-limit filtering plus topic-option aggregation into `ArticleListFilter`; `MainWindowViewModel` retains observable state and UI notifications. All 99 App tests pass.
- **2026-10-08 — Catalog feed filtering extraction:** Moved catalog text, category, collection, health, and metadata-gap filter rules into `CatalogFeedFilter`; `CatalogManagementViewModel` remains responsible for WPF view refresh and binding notifications. All 19 CatalogManagementViewModel tests pass.
- **2026-10-08 — Catalog management projection extraction:** Moved conversion of catalog feed/category/collection data into display rows and filter options into `CatalogManagementSnapshot`; selection retention and observable collection updates remain in the view model. All 19 CatalogManagementViewModel tests pass.
- **2026-10-08 — Catalog application-service split:** Kept `CatalogService` as the compatibility facade and moved feed/import/health, category, and collection operations into focused services. Shared Catalog Master authorization and input validation remain centralized in `CatalogServiceRules`; all 11 CatalogService tests pass.
- **2026-10-08 — Reader article persistence extraction:** Kept `SqliteReaderStore` as the `IReaderStore` facade and moved article querying, ingestion, category mapping, and read/saved state writes into `SqliteArticleStore`. Shared SQLite connection setup is centralized, with transaction and schema behavior unchanged; all 52 Infrastructure tests pass.
- **2026-10-08 — Reader profile persistence split:** Moved subscription, folder, and tag operations into `SqliteProfileLibraryStore`, and refresh-state persistence into `SqliteFeedRefreshStore`. `SqliteReaderStore` now owns schema initialization and delegates through the stable `IReaderStore` contract; all 52 Infrastructure tests pass.
- **2026-10-08 — Catalog taxonomy persistence extraction:** Kept `SqliteCatalogStore` as the `ICatalogStore` facade and moved category, collection, and collection-membership queries/writes into `SqliteCatalogTaxonomyStore`, reusing the shared SQLite connection factory. All 52 Infrastructure tests pass.
- **2026-10-08 — Reader refresh-state extraction:** Moved per-feed refresh-state storage and selected-article status formatting into `FeedRefreshStatus`; the main view model retains refresh orchestration and visibility-aware notifications. All 99 App tests pass.
- **2026-10-08 — Catalog preview cache extraction:** Moved the bounded FIFO preview cache out of `MainWindowViewModel` into `CatalogFeedPreviewCache`, preserving the 20-preview capacity and refresh behavior. All 99 App tests pass.
- **2026-10-08 — Catalog membership workflow extraction:** Moved validation, membership diffing, persistence, and error recovery into `CatalogCollectionMembershipEditor`; the management view model retains selection, command, and refresh binding ownership. All 19 CatalogManagementViewModel tests pass.
- **2026-10-08 — Catalog taxonomy workflow extraction:** Moved category/collection form state and CRUD/merge commands into `CatalogTaxonomyManagement`; the existing management view-model binding surface and service behavior remain unchanged. All 99 App tests pass.
- **2026-10-08 — Navigation history extraction:** Moved bounded (100-entry) back-history storage and snapshots into `ReaderNavigationHistory`, leaving route restoration and UI synchronization with the main view model. All 99 App tests pass.
- **2026-10-08 — Sidebar feed navigation extraction:** Moved folder/feed link projection, folder-to-feed lookup, and expanded-folder state into `SidebarFeedNavigation`; the shell view model delegates updates while preserving sample links and profile navigation behavior. All 99 App tests pass.
- **2026-10-08 — Catalog browser state extraction:** Moved catalog loading, filtering, selection summaries, and subscription-state updates into `CatalogBrowserState`; `MainWindowViewModel` remains the binding and command coordinator. Shared `BulkObservableCollection` preserves reset notifications and reentrancy checks. All 99 App tests pass.
- **2026-10-08 — Final architecture review:** Retained the remaining route, refresh, and selection orchestration in `MainWindowViewModel` because it coordinates shared visible/selected state across extracted feature owners; kept view-specific WPF lifecycle and raw-feed behavior with their views. No remaining file was split mechanically. All 196 solution tests pass; `git diff --check` passes.

## Guiding Principles

- Split by one clear reason to change, not by arbitrary line ranges or class length.
- Prefer a focused class or WPF `UserControl` over `partial` files that conceal one oversized responsibility.
- Keep the existing project dependency direction: WPF in `RssReader.App`, use cases in `RssReader.Application`, domain rules in `RssReader.Domain`, and SQLite/HTTP details in `RssReader.Infrastructure`.
- Preserve routes, commands, keyboard behavior, focus, selection, article scroll position, accessibility names, resource lookup, and virtualization during view extraction.
- Preserve profile isolation, authorization, SQLite schema and transaction semantics, feed parsing, URL safety, cancellation, and failure handling during service/store extraction.
- Initially keep public contracts and dependency-injection behavior stable. Change an interface only when the extraction demonstrates a useful, independently testable boundary.
- Do not combine structural cleanup with feature changes, data migrations, broad renaming, or visual redesign.
- Keep a large view-specific component intact when splitting it would add indirection without making ownership clearer.

## Proposed Target Boundaries

### Presentation

Turn the main window into a composition shell hosting focused pieces such as navigation/sidebar, article list and filters, article reader, and Follow Sources/catalog browsing. The exact control boundaries should be chosen from the current XAML and data bindings during Phase 1; avoid creating one control per small panel.

Move large XAML resource groups into merged dictionaries organized by purpose, for example shared colors/typography, common controls, and article/catalog templates. Keep resources local when they are truly specific to one view.

Reduce `MainWindowViewModel` to state and coordination that genuinely spans the shell. Candidate feature owners are:

- Article list, selection, adjacent-article navigation, read/saved actions, and list presentation.
- Sidebar routes, folders/tags, and navigation history.
- Follow Sources search, selection, and subscription actions.
- Refresh progress, per-feed failure details, and retry actions.

Do not duplicate shared state or introduce a generic event bus just to decouple views. Establish how the extracted view models receive shared profile/services and communicate before moving commands.

Keep WPF-specific code such as focus restoration, window placement, and WebView2 hosting close to the relevant view or a small dedicated behavior/controller. The window code-behind should not remain a second home for product workflows.

### Application and infrastructure

Split only where responsibilities and tests separate cleanly:

- In `CatalogManagementViewModel` and `CatalogService`, distinguish feed maintenance from category/collection maintenance and bulk/health workflows, while retaining a single, consistent authorization boundary.
- In `SyndicationFeedDownloader`, distinguish HTTP request policy, RSS/Atom parsing, and raw article-content retrieval if those responsibilities can be isolated without weakening shared limits, cancellation, or URL validation.
- In `SqliteReaderStore`, identify cohesive persistence units for subscriptions/folders/tags, articles and profile article state, and refresh history. Keep connection, schema initialization, transaction, and profile-scoping behavior explicit.
- In `SqliteCatalogStore`, separate catalog feeds from category/collection membership only if doing so removes meaningful coupling. Reuse the existing database setup rather than duplicating schema and connection logic.

Where existing `IReaderStore` or `ICatalogStore` contracts are too broad for the extracted responsibilities, first consider an internal implementation facade that preserves the public contract. Introduce narrower interfaces only when consumers can depend on them meaningfully and tests can verify the boundary.

## Phased Execution

### Phase 0: Establish a safe baseline

- Confirm the working tree and isolate this work from unrelated in-progress changes.
- Build the solution and run the relevant Domain, Application, Infrastructure, and App tests before each extraction.
- Identify behavior already covered by tests and add focused characterization tests before moving an unprotected workflow.
- Record the current DI registrations, WPF resource lookup, important bindings, keyboard/focus behavior, and persistence contracts for the target area.
- Choose one file cluster per change; do not start with a repo-wide rename or namespace reorganization.

**Exit criteria:** The first target has a clear behavior baseline and a small, reviewable extraction sequence.

### Phase 1: Decompose the main-window UI

- Extract the highest-cohesion UI sections from `MainWindow.xaml` into a small number of focused `UserControl`s.
- Move view-specific styles/templates with their controls; move shared resources to purpose-based dictionaries.
- Keep bindings and data context ownership explicit. Preserve virtualization and avoid nested scrolling surfaces.
- Reduce `MainWindow.xaml.cs` to shell lifecycle and WPF-only integration as each view is extracted.

**Checks:** App build and focused WPF tests; manually verify navigation, search/filter composition, article selection/back, keyboard shortcuts, resizing, focus, and list virtualization.

### Phase 2: Separate main-window feature state

- Move one coherent command/property group at a time from `MainWindowViewModel` to the view model that owns its view or workflow.
- Start with boundaries made obvious by Phase 1; avoid speculative abstractions or moving properties merely to reduce the file count.
- Retain a thin shell model only for truly cross-view state and navigation coordination.

**Checks:** Focused view-model and WPF tests for route changes, selection/history, filter persistence, refresh/retry, and profile preferences.

### Phase 3: Decompose catalog management

- Extract feed editing/list state separately from category/collection management only where each can own its state and commands.
- Keep catalog authorization and destructive-operation semantics centralized and test them at the application boundary.
- Keep partial failures and cancellation behavior unchanged for bulk operations.

**Checks:** Catalog service and view-model tests; verify add/edit/delete, category/collection membership, and immediate Follow Sources synchronization.

### Phase 4: Decompose feed retrieval and persistence

- Characterize parser formats, request limits, redirects/safe URL behavior, cancellation, raw-content behavior, and refresh failures before changing downloader boundaries.
- Extract persistence responsibilities incrementally, retaining schema creation, transaction boundaries, cascades, and profile isolation.
- Keep the existing store contracts until the internal structure proves stable; do not make a database migration for a code-only refactor.

**Checks:** Infrastructure and Application tests, SQLite round trips/cascade tests, deterministic HTTP/parser tests, and relevant profile-isolation tests.

### Phase 5: Review remaining large components

- Reassess `RawFeedWindow.xaml.cs` and other files based on the boundaries established in earlier phases.
- Refactor only when it creates a more cohesive owner or improves independent testing.
- Update architecture documentation if component ownership or dependency wiring materially changes.

**Exit criteria:** No remaining high-change surface mixes unrelated workflows without a documented reason; no new file is merely a line-split of the old implementation.

## Completion Criteria

- Each extracted component has a concise responsibility and a deliberately small interaction surface.
- The main window and its view model primarily compose and coordinate features rather than implement every feature.
- WPF-only behavior remains explicit and testable without forcing UI framework details into Application or Domain.
- Application and Infrastructure ownership is clearer without duplicated policy, database setup, or authorization logic.
- Existing behavior is preserved, focused tests pass, and the app builds after every phase.
- There are no unrelated design, feature, schema, or generated-file changes in a refactoring change.

The product workflow and release-validation backlog remains in the [App Improvements Plan](./App-Improvements-Plan.MD); this document covers structural work only.
