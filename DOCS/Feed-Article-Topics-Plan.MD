# Feed Article Topics Plan

## Purpose

Use publisher-supplied RSS/Atom categories to make articles easier to scan, discover, and filter. The initial example is an RSS item category such as `<category><![CDATA[Press Releases]]></category>`.

This is an implementation roadmap, not a commitment to every future topic-discovery idea. Deliver the parsing, persistence, and single-topic filter as a coherent first release; expand into richer discovery only after that workflow is validated.

## Current Baseline

- `SyndicationFeedDownloader` already parses feeds through .NET `SyndicationFeed`; each `SyndicationItem` exposes its categories.
- `DownloadedFeedItem` and domain `FeedArticle` currently carry no category metadata.
- `SqliteReaderStore` persists articles in `Articles`; refresh updates existing article fields as well as inserting new items.
- `ProfileFeedTags` are profile-owned labels assigned to a feed. `MainWindowViewModel` currently copies those feed tags onto every article from that feed and filters them through the existing `tag:` behavior.
- Article views already filter an in-memory article set by active route, search, unread-only, saved-only, and sort/view mode.
- Application-wide control and action styles live in `src/RssReader.App/App.xaml`; the styling contract is `DOCS/Styling.MD`.

Relevant implementation surfaces include `src/RssReader.Infrastructure/SyndicationFeedDownloader.cs`, `src/RssReader.Application/IFeedDownloader.cs`, `src/RssReader.Application/FeedRefreshService.cs`, `src/RssReader.Domain/FeedArticle.cs`, `src/RssReader.Application/ArticleFilter.cs`, `src/RssReader.Application/ReadingService.cs`, `src/RssReader.Infrastructure/SqliteReaderStore.cs`, `src/RssReader.App/ViewModels/ArticleRowViewModel.cs`, `src/RssReader.App/ViewModels/MainWindowViewModel.cs`, and `src/RssReader.App/MainWindow.xaml`.

## Product Decisions

- Call publisher-provided article metadata **Topics** in the UI. Keep it distinct from user-assigned **Feed tags**.
- Do not rename or reinterpret existing `ProfileFeedTags`, `tag:` routes, or profile ownership. Existing personal organization must continue to work unchanged.
- Read RSS category text as data, not markup. CDATA wrappers are removed by XML parsing; trim surrounding whitespace and ignore empty values.
- Preserve the category term/name and optional scheme/label from the syndication model. Display `Label` when present, otherwise the term/name. Treat scheme as metadata for identity and future disambiguation, not as user-facing text in the first release.
- Deduplicate categories within an article using case-insensitive term plus scheme identity, while preserving the publisher's display casing.
- A feed refresh replaces the stored topic set for each returned article. A category removed by the publisher should no longer appear on that article. Articles absent from a refresh retain their last stored data.
- Existing articles start with no topics after the additive migration and acquire topics when they are next refreshed. Do not silently re-fetch old feed history to backfill them.

## Product Outcomes

Users should be able to:

- Understand an article's publisher-provided topics without opening the article.
- Filter the current article list by one topic and combine that selection with the current feed/folder, search, unread-only, and saved-only filters.
- See how many loaded articles match each topic and clear the topic filter without losing other filter state.
- Tell publisher topics apart from profile-assigned feed tags.
- Continue reading offline using the latest successfully stored topic metadata.

## Experience Proposal

### Article List

- Add a searchable or standard shared-style **Topic** ComboBox to the existing article-list filter row. Its default is **All topics**; each option displays a topic label and a count for the current article scope.
- The count scope should respect the active feed/folder, search, unread, and saved filters, but be computed before applying the selected topic. This keeps counts useful for comparing alternatives without changing the other filters.
- Make the selected topic visible in the workspace filter state and provide a compact clear action that clears only the topic selection. The existing route and other filters remain intact.
- Show a restrained set of topic labels in article list, card, and magazine presentations where space permits. Cap the number shown per article and show a remaining-count affordance rather than allowing long category lists to stretch rows. The full topic list belongs in the article reading view or an accessible detail/tooltip surface when truncated.
- Article topic text can be included in the existing local search. This makes a search for a publisher term find matching articles even when it is not in the title or summary.
- Initially keep topic selection as a list filter rather than adding a permanent sidebar item for every discovered topic. Feed taxonomies can have a long tail; a growing sidebar would overwhelm the current navigation.

### Topic Discovery Follow-Up

After the first release is useful, consider an **Explore topics** workspace with a searchable topic list, current-scope counts, and recent articles per topic. Keep it optional and separate from personal feed tags. Do not add ranking language such as "trending" without a defined, explainable local signal.

## Phased Roadmap

### Phase 0: Lock Semantics and UX

**Goal:** Prevent ambiguous meaning and settle category identity before persistence is added.

**Work:**

- Confirm labels: publisher metadata is "Topics"; user-assigned labels are "Feed tags".
- Confirm single-topic filtering for the first release. Multi-select OR/AND semantics are deferred.
- Decide how duplicate terms with different schemes are presented. The default should group by term/name when unambiguous and expose scheme only when needed to distinguish options.
- Confirm the count definition: current route/feed/folder plus search/read/saved filters, before the selected topic is applied.
- Review representative RSS and Atom feeds with zero, one, and many category values; automated fixtures remain deterministic and do not use live network requests.

**Exit criteria:** Topic identity, terminology, count scope, and single-select behavior are documented before the first persistence migration.

### Phase 1: Parse and Carry Topic Metadata

**Goal:** Preserve publisher topic values from the network response through the refresh boundary.

**Work:**

- Extend `DownloadedFeedItem` with an optional topic/category collection so existing feed-download fakes and callers remain easy to migrate.
- In `SyndicationFeedDownloader`, map every `SyndicationItem.Categories` entry to a domain-independent value containing term/name and optional scheme/label. Do not scrape category XML with string matching; the syndication parser already handles RSS and Atom namespaces and CDATA.
- Normalize by trimming values, dropping blanks, and deduplicating case-insensitively without losing the first publisher-provided casing.
- Carry the values through `FeedRefreshService.ToArticle` into the domain article metadata.

**Tests:** RSS category text and CDATA, multiple categories, empty/duplicate terms, scheme/label preservation, Atom categories, and refresh mapping. No live feeds.

**Exit criteria:** Download and refresh tests prove that category semantics survive parsing and mapping without affecting existing title/content/image parsing.

### Phase 2: Persist and Refresh Article Topics

**Goal:** Make topics local, profile-independent article metadata available offline.

**Work:**

- Add an additive `ArticleCategories` table keyed to `Articles`, storing the category term/name plus optional scheme/label. Use a uniqueness rule that prevents duplicate equivalent values per article and an index that supports topic lookup.
- Add migration-safe initialization for existing databases. Existing records remain valid and simply have zero topic rows until refreshed.
- Update `SaveArticlesAsync` within its existing transaction: insert/update the article and replace that article's topic rows on every successful feed refresh. A feed response with no categories clears the previous set for that returned article.
- Load topic rows for article retrieval using a bounded/batched query or equivalent set-based approach; do not issue one database query per article.
- Keep topics on the shared article, not `ProfileArticleStates` or `ProfileFeedTags`, because the publisher metadata describes the article and is identical across profiles.

**Tests:** fresh schema, migration of a database containing articles, categories round-trip, category removal/update on refresh, duplicate normalization, multiple profiles seeing the same article topics, and preservation of read/saved state.

**Exit criteria:** Existing databases initialize without data loss; refreshed topic sets round-trip and update atomically with their article.

### Phase 3: Application Filtering and View-Model State

**Goal:** Make topic filtering deterministic, composable, and independently testable.

**Work:**

- Add a clearly named article-topic field to `ArticleFilter`, separate from `TagName`; do not overload the current feed-tag filter.
- Add topic filtering to `ReadingService` for application-level callers, using case-insensitive term matching and scheme disambiguation where required.
- Carry topic names into `ArticleRowViewModel` as a distinct property; do not reuse its existing `Tags` property, which currently means profile feed tags.
- Add selected-topic state, available topic options/counts, and a clear-topic command/property to `MainWindowViewModel`.
- Compose the topic predicate with the current active route, search, unread-only, and saved-only filters. Clearing the topic must not reset those other filters. Search should also match topic labels/terms.
- Define empty state text for no articles in the current scope versus no articles matching the selected topic, and keep the selected option valid if a refresh removes its last matching article.

**Tests:** application service exact/case-insensitive matching, no cross-match with profile feed tags, route/filter composition, search by topic, clear-topic behavior, count scope, and empty-state transitions.

**Exit criteria:** Topic filters compose without changing current feed-tag, folder, read, saved, or search behavior.

### Phase 4: WPF Topic Controls and Presentation

**Goal:** Add a polished, accessible topic workflow that adheres to `DOCS/Styling.MD`.

**Work:**

- Place the Topic ComboBox in the existing article-list filter row, using the implicit application ComboBox style in `App.xaml`; set `DisplayMemberPath` or an explicit `ItemTemplate` for the topic option type so model `ToString()` output is never shown.
- Keep the control within the established compact control-height range (32-38 px), use shared `ContentBrush`, `ControlBorderBrush`, `TextBrush`, and `MutedTextBrush`, and retain the shared selected, hover, disabled, dropdown, and keyboard-focus states. Do not introduce local hex colors or a native gray ComboBox.
- Give it an `AutomationProperties.Name`, a visible selection label, keyboard operation, and predictable focus order. Use a tooltip only if it adds context rather than repeating the visible label.
- Render topic labels as compact inline metadata, not nested cards. Use existing text hierarchy (13-14 px for controls/labels and 10-12 px for metadata), a 3-5 px radius if a framed label is needed, and shared neutral/accent resources. Keep truncation stable and expose the complete value accessibly.
- Ensure topic labels do not compete with primary article title/source. Use the same treatment across row, card, and magazine templates; preserve existing article selection and virtualization behavior.
- Provide a clear filter action using the established action styles. Any icon-only control requires a tooltip and automation name. Avoid redundant tooltips on visible topic names.
- Do not add a new window for the initial filter. If the later Explore Topics feature requires a dialog, it must follow the custom titleless chrome, top-right close control, visible subtle dialog border, and sizing rules in `Styling.MD`.

**UI tests:** WPF STA tests for visible selected labels, category option display, focus/keyboard behavior, topic-label overflow, clear-filter behavior, and interaction with existing article selection. Review resized and high-DPI layouts; visual comparison does not replace behavior tests.

**Exit criteria:** The new control and labels follow `Styling.MD`, remain accessible and stable at resized window sizes, and do not impair virtualized scrolling or article opening.

### Phase 5: Topic Exploration and Quality Follow-Up

**Goal:** Expand from a useful filter to deliberate topic discovery only after the first workflow is validated.

**Possible work:**

- Add an optional Explore Topics view with a search field, alphabetic or count ordering, topic counts, and a short list of matching recent articles.
- Add opt-in pinning of frequently used topics to a compact area; keep this distinct from profile feed tags and do not automatically turn every publisher topic into sidebar navigation.
- Consider multi-select only after documenting whether multiple selected topics mean OR or AND; expose the mode rather than relying on hidden behavior.
- Add topic-specific saved searches only if users need reusable combinations with folders, read state, or source feeds.
- Measure local filter responsiveness on realistic large article sets before moving filtering into more complex database paging.

**Exit criteria:** Each extension addresses observed user tasks, remains local-first, and has defined accessibility, persistence, and filter-composition semantics.

## Styling.MD Compliance Checklist

- Reuse global control styles and palette resources from `App.xaml`; do not add ad hoc brushes or duplicate the ComboBox template locally.
- Use the shared ComboBox dropdown and item styles, with explicit display binding for model objects.
- Keep article views dense and scannable; use whitespace and alignment rather than decorative cards or card-within-card layouts.
- Keep topic labels subordinate to article titles and source identity; respect typography and compact 32-38 px controls.
- Use shared borders and 3-5 px control radii. Avoid pills and oversized badge treatments.
- Provide selected, hover, disabled, and keyboard-focus feedback through established neutral/accent resources.
- Avoid redundant tooltips; provide automation names for controls and complete accessible text for truncated topics.
- Preserve virtualized lists and scrollbar spacing/styling. Do not wrap article lists in an outer scrolling container that defeats virtualization.
- If any dialog is introduced later, use custom titleless chrome, top-right close action, visible subtle border, and shared dialog action styles.

## Cross-Cutting Engineering Requirements

- Preserve MVVM boundaries: parsing and HTTP stay in Infrastructure; domain types remain WPF-independent; application services own filter semantics; the view model owns presentation state; XAML only renders and binds state.
- Keep profile boundaries explicit. Publisher topics are shared article metadata; read/saved state and feed tags remain profile-specific.
- Keep refresh failure behavior unchanged: a failed feed refresh must not delete its stored articles or topics.
- Use safe SQLite migrations and transactions. Avoid schema changes that rewrite or discard existing profile tags.
- Test with fake HTTP, temporary SQLite databases, and deterministic in-memory article sets. Do not depend on live feeds or timing-sensitive UI waits.
- Update `DOCS/Architecture.MD` only if implementation changes an accepted data-boundary decision; keep this feature plan as the product/engineering roadmap.

## Success Measures

- A user can identify an article's topics at a glance without mistaking them for personal feed tags.
- A user can filter by a topic and combine it with other article filters without losing state.
- Topic counts match the stated current-scope definition, including after refresh and clear actions.
- Existing feed-tag behavior and profile isolation remain unchanged.
- Topic filtering remains responsive and list virtualization/scroll behavior remain intact at representative article volumes.
- All new controls pass keyboard and accessibility-name checks and comply with `DOCS/Styling.MD`.

## Recommended Delivery Order

Complete Phases 0-4 for the first release: semantics, parser, persistence, filter behavior, then the styled WPF experience. Defer Explore Topics, pinned topics, multi-select, and saved topic searches until the single-topic workflow has been used and validated.
