# Catalog Master: Manage Catalog View Plan

## Purpose

Make the Catalog Master feed catalog practical to maintain as it grows from hundreds to thousands of feeds. The current view is a product-management workspace, not the regular user's **Follow Sources** browser; it should reuse that browser's effective search and filtering patterns while adding safe editing and bulk curation.

This document proposes a phased UI and workflow plan. It does not commit to database paging, new catalog metadata, automated health scanning, or changes to the regular user's subscription flow without evidence that those are needed.

## Current Baseline

Observed in `src/RssReader.App/MainWindow.xaml` and `src/RssReader.App/ViewModels/CatalogManagementViewModel.cs`:

- The Catalog Master view is two columns. The left column contains the feed list followed by category and collection lists; the right column contains OPML/starter-pack import and one-feed-to-one-collection assignment controls.
- Feed management has a **Show metadata gaps only** toggle but no text search or general category, collection, or health filters. Its summary reports metadata gaps, not how many feeds match the current view.
- Feeds are rendered in an `ItemsControl` within a `ScrollViewer`; unlike Follow Sources, this list has no WPF UI virtualization or recycling configuration.
- Each feed row offers Edit, Check feed, and Remove. The add/edit dialog supports name, feed URL, publisher website, description, and one category, but not collection membership.
- Collection membership changes require choosing one feed and one collection in the right column, then invoking a separate add/remove action.
- Import OPML and Load starter pack are always-visible controls in the right column.
- The catalog browser already provides token-based search, category and collection filters, hide-followed, clear filters, a result summary, and a recycling virtualized list. Its filtering behavior and accessibility conventions are useful local precedents.

Relevant implementation surfaces: `src/RssReader.App/MainWindow.xaml`, `src/RssReader.App/ViewModels/CatalogManagementViewModel.cs`, `src/RssReader.App/ViewModels/CatalogFeedListItem.cs`, `src/RssReader.App/CatalogEntryWindow.xaml`, and `src/RssReader.App/CatalogEntryWindow.xaml.cs`.

## Product Outcomes

Catalog Masters should be able to:

- Locate a known feed by name, description, category, feed URL, or publisher website without scrolling through the catalog.
- Narrow the inventory by category, collection membership, metadata completeness, and health-check state, and understand how many feeds match.
- Review a compact, consistently ordered feed list that stays responsive at several thousand entries.
- Assign or remove collection membership while editing a feed or by selecting multiple feeds, without leaving the feed workflow.
- Import OPML or the starter pack without giving those infrequent actions permanent toolbar space.
- Edit and remove shared catalog entries deliberately, with clear validation, confirmation, and outcomes.

## Proposed View

### 1. One focused feed-management workspace

- Remove the fixed right-hand column from **Manage Catalog**. Give the feed inventory the available width and keep the primary feed workflow in view.
- Use a compact header with the view title, total catalog count, current matching count, an **Add feed** action, and an overflow (`...`) menu.
- Put **Import OPML** and **Load starter pack** in the overflow menu. Keep these commands discoverable by keyboard and give the menu an accessible name.
- Move category and collection administration out of the feed-list scroll path into a clearly named **Manage categories and collections** dialog or focused subview. Do not leave taxonomy controls below thousands of feed rows.
- Preserve the current feed row actions, but consider placing less frequent actions (health check and remove) in a row-level context menu. Keep Edit easy to discover.

### 2. Search, filters, and result counts

Match the familiar behavior of **Follow Sources** where it applies, while keeping management-specific facets:

- Search feed name, description, category, feed URL/domain, and publisher website with case-insensitive, whitespace-tolerant token matching.
- Provide category and collection filters, including **All** options and counts where practical.
- Replace or complement **Show metadata gaps only** with explicit metadata-completeness options, such as **All**, **Has gaps**, and (if useful) a specific missing-field filter.
- Add health state filtering only for persisted, timestamped checks, for example **Not checked**, **Last check succeeded**, and **Last check failed**. Always show when the check occurred; a past success is not a guarantee of current availability.
- Show a concise count such as **Showing 42 of 1,280 feeds**, and retain a clear **Clear filters** action. Distinguish zero catalog entries from zero matches.
- Start with deterministic alphabetical sorting. Add sort choices such as category or last checked only when they support a real maintenance task; do not imply a quality ranking.
- Preserve the query, filters, sort, selection, and scroll position when opening and closing an editor or completing a bulk assignment.

### 3. Dense, virtualized feed results

- Replace the unvirtualized `ItemsControl` feed inventory with a WPF list control using UI virtualization and recycling. Avoid wrapping the virtualized list in an outer `ScrollViewer` that disables virtualization.
- Keep rows scannable: feed name, category, publisher/domain context, and compact metadata/health status. Truncate long URLs visually but retain the full value in a tooltip or accessible name.
- Make same-name feeds distinguishable using their publisher website or endpoint host. Keep metadata-gap and health indicators textual or accessible, not color-only.
- Add explicit loading, empty-catalog, no-match, and recoverable error states. Keep the matching count synchronized with every filter change.
- Preserve keyboard navigation, visible focus, screen-reader names, high-contrast behavior, and sensible layout at 100%-200% display scaling.

### 4. Collection assignment in feed workflows

Support the same collection assignment operations in two places:

- **Create/edit feed dialog:** show collection membership as a multi-select list of collections. On edit, initialize it from existing membership; on create, allow initial membership to be chosen before saving. Keep category selection distinct from collection membership.
- **Multi-select feed results:** add row checkboxes and a compact selection action bar with the selected count. Offer **Add to collection** and **Remove from collection**; each opens a collection chooser and clearly states whether it will add or remove membership.
- Keep selection bounded and explicit. Start with individually selected rows; if a future **Select all matching feeds** option is added, clearly show the number affected and require a deliberate confirmation before applying the bulk change.
- Define how bulk assignment handles feeds already in/not in the chosen collection. Prefer idempotent operations and report changed versus unchanged counts; failures must identify affected feeds and leave successful changes intact.
- Do not make users visit a separate feed/collection pairing panel. Collection creation, rename, and deletion remain taxonomy-administration tasks in the dedicated categories/collections workflow.

### 5. Import and destructive-action safeguards

- Keep OPML and starter-pack import in the overflow menu. Preserve the existing added/skipped summary and show recoverable parse or storage errors near the view header.
- Keep starter-pack loading repeatable and safe: duplicate feed URLs should be reported as skipped rather than creating duplicates.
- For OPML, consider a later import review step for entry counts, duplicates, and category mapping if real imports show ambiguity; do not block the initial layout change on that enhancement.
- Confirm feed removal and explain the impact on the shared catalog and any existing profile subscriptions before the destructive operation. Do not silently imply that removing a catalog entry deletes or preserves subscriptions unless the service behavior has been verified.
- Preserve existing URL validation and duplicate prevention. On same-name collisions, show enough URL/publisher context for the Catalog Master to choose the correct entry.

## Phased Roadmap

### Phase 1: Find and maintain feeds at scale

**Goal:** Make the feed inventory searchable, filterable, count-aware, and responsive; remove the right-side utility column.

**Work:**

- Replace the two-column layout with the focused workspace, header actions, search, facets, filter clearing, and matching/total counts.
- Move OPML and starter-pack commands into the overflow menu.
- Add deterministic sorting, empty/no-match states, and preserve browse state around current edit operations.
- Use a recycling virtualized list and verify virtualization is not defeated by its visual-tree parents.
- Keep metadata-gap filtering and per-feed health check available as facets/actions.

**Exit criteria:** A Catalog Master can find a feed using the same practical search/filter behavior as Follow Sources, identify matching versus total counts, clear filters, edit a feed, and return to the same list position. A generated catalog with at least 5,000 feeds remains scrollable and filterable without rendering every row.

### Phase 2: Edit collection membership and assign in bulk

**Goal:** Eliminate the separate one-feed/one-collection assignment panel.

**Work:**

- Add multi-select collection membership to the create/edit feed dialog.
- Add feed-row selection and bulk add/remove collection actions with explicit counts and clear success/partial-failure feedback.
- Add a separate, compact categories/collections management dialog or subview, including existing create/delete operations and clear empty states.
- Add confirmation and impact messaging for feed removal based on the verified service and subscription behavior.

**Exit criteria:** A feed can be assigned to multiple collections during create/edit; several selected feeds can be assigned or unassigned in one operation; category assignment is not confused with collection membership; failures and cancellations do not lose the user's browse state.

### Phase 3: Catalog operations and measured scale polish

**Goal:** Improve recurring curation tasks based on actual catalog size and curator feedback.

**Work:**

- Evaluate saved filter presets or metadata-review queues only if curators repeatedly use the same combinations.
- Consider bulk category changes, import preview/mapping, or bulk health checking as separate proposals with explicit impact, cancellation, rate limits, and partial-failure behavior.
- Measure filtering, initial display, and scrolling at documented catalog sizes. Add indexed database filtering or paging only when in-memory filtering plus UI virtualization no longer meets agreed targets.
- Review keyboard-only use, screen-reader flow, high-contrast display, and narrow/resized windows.

**Exit criteria:** Additional curator workflows are supported by observed maintenance needs, and performance targets are defined on a documented reference machine rather than guessed.

## Validation

- Add focused ViewModel tests for search normalization, category/collection/metadata/health filters, result counts, clear filters, deterministic sorting, selection, and selection command state.
- Add tests for collection membership during create/edit and multi-feed assignment/removal, including duplicates, already-assigned feeds, cancellation, and partial failure.
- Add WPF control tests for menu command wiring, list virtualization/recycling, accessible names, empty states, focus, and restored browse state.
- Use generated data at 575, 5,000, and 25,000 feeds where practical. Record timings without machine-specific assertions until a reference machine and thresholds are agreed.
- Keep tests deterministic: use fake feed responses and temporary SQLite stores; do not require live feed endpoints.
- Run existing Catalog Master and catalog browser tests to guard shared catalog behavior and the regular user's Follow Sources workflow.

## Decisions and Guardrails

- **Filter parity:** Reuse Follow Sources search and category/collection semantics where they make sense. Catalog Master filters must not apply regular-profile followed-state rules to shared catalog maintenance.
- **Selection semantics:** Decide whether filtered-out selections remain selected. Recommended initial behavior: retain explicit selections while filters change, display the total selected count, and provide a clear-selection command; never silently bulk-operate on hidden rows.
- **Bulk operations:** Collection membership is the first bulk operation. Defer bulk delete, bulk category changes, and catalog-wide health scans pending explicit requirements and risk review.
- **Data access:** Prefer the smallest change that meets measured performance needs. UI virtualization reduces visual-tree cost but does not itself reduce the cost of loading/filtering the entire catalog; measure before introducing paging or storage/API changes.
- **Import menu:** The overflow menu is for infrequent catalog-level actions. Keep **Add feed** visible because it is a common authoring action.
- **Profile boundary:** Categories and collections are shared catalog metadata; personal folders and subscriptions remain profile-owned and must not be changed by these catalog-management actions.

## Open Questions

- Should the result count report only matching feeds, or also show counts per category/collection option? Recommendation: show matching/total prominently; add facet counts if they remain clear and inexpensive.
- Should bulk collection assignment support adding/removing several collections at once, or one collection per operation initially? Recommendation: one collection per operation for clarity, while feed edit supports multiple memberships.
- What is the verified impact of removing a catalog entry on existing subscriptions and cached articles? Document this before finalizing confirmation text.
- Which screen/window should own category and collection CRUD: a modal management dialog or a dedicated subview? Choose the smallest option consistent with current navigation and keyboard flow.
- At what measured catalog size does loading all feeds become a user-visible bottleneck that warrants indexed queries or paging?

## Relationship to Existing Plans

This plan specializes the Catalog Master editing experience. The regular-user discovery and subscription roadmap remains in [Feed Catalog Discovery and Selection Plan](Feed-Catalog-Discovery-Plan.MD); the catalog snapshot and usability baseline remain in [Feed Catalog Discovery Baseline](Feed-Catalog-Discovery-Baseline.MD). Reuse browser behavior where appropriate without combining administrative actions with ordinary feed following. Keep the responsibilities of the two experiences distinct.
