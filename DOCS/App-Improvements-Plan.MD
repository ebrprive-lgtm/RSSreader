# RSS Reader App Improvements Plan

## Status

Proposed roadmap. This document organizes candidate improvements; it does not authorize unrelated architecture changes or commit the product to every later phase. Confirm each phase's scope and checks before implementation.

## Purpose

Improve trust, speed, and usability across the recurring RSS reader workflows: following sources, refreshing feeds, finding articles, reading publisher HTML, and maintaining the shared catalog. Prioritize observable user problems and regression prevention over speculative features.

## Current Baseline

- The application is a Windows WPF reader using MVVM, RSS/Atom syndication parsing, SQLite persistence, and WebView2 for article content.
- Users can browse and follow catalog feeds, organize subscriptions into folders, assign profile-scoped feed tags, filter by publisher topics, and mark articles read or saved.
- The selected-article view displays the feed, author/date metadata, publisher website link when available, and sanitized article HTML. The raw XML dialog supports tree inspection and copying the original selected-item XML.
- Cards and Magazine presentations use the feed/article image metadata. Article HTML uses the images in the article body, with handling for duplicate lead images and centered image markup.
- Catalog Master can create and maintain catalog feeds, categories, and collections. A newly created feed is synchronized to Follow Sources without restarting.
- Existing test projects cover Domain, Application, Infrastructure, and App behavior. The WPF App test host has had intermittent shared-Application/WebView2 shutdown failures; run WPF integration tests in isolation when diagnosing host failures.

## Prioritization

1. Prevent feed-content regressions and improve refresh failure visibility.
2. Make finding and reading articles more efficient.
3. Reduce catalog maintenance work.
4. Improve accessibility and resilience for weak/offline network conditions.
5. Optimize performance only after measuring representative data on a documented reference machine.

## Product Guardrails

- Do not depend on live public feeds in automated tests. Use deterministic RSS/Atom and HTML fixtures, temporary SQLite databases, and fake HTTP handlers.
- Keep profile-owned state (subscriptions, feed tags, read/saved state, preferences) distinct from shared catalog data and publisher-owned article metadata (topics, author, article images).
- Treat all publisher markup and URLs as untrusted. Preserve the existing sanitizer, safe-scheme policy, and WebView2 navigation boundary.
- Preserve existing behavior unless a phase explicitly changes it and includes a migration or regression test.
- Prefer incremental, independently testable work. Do not introduce database paging, background indexing, automatic tagging, or large UI redesigns without measured need.
- Follow `DOCS/Styling.MD` for WPF layout, shared resources, keyboard focus, automation names, dialogs, and compact controls.

## Phase 0: Establish a Feed-Quality Regression Baseline

**Goal:** Make representative feed parsing, image handling, and article rendering reproducible before expanding reader features.

**Work:**

- Maintain small, sanitized RSS 2.0, Atom, and HTML fixtures that cover the formats the app supports. Keep only the markup needed to reproduce each behavior; avoid committing unnecessary full publisher articles.
- Cover RSS `content:encoded`, RSS description containing HTML, Atom HTML/XHTML, namespaces, CDATA, relative URLs, empty content, and malformed input.
- Add image cases for: no image; one body image; a lead image duplicated later at a resized URL via `srcset`; multiple distinct images; linked images; centered images; and unsafe URLs/attributes.
- Record expected precedence between full content and summary and verify that the feed-level image is not injected into the article body.
- Keep feed tags, publisher topics, author, and dates as separate metadata concepts in fixtures and assertions.

**Checks:**

- [ ] Infrastructure tests prove supported RSS/Atom metadata is parsed without live HTTP.
- [ ] Sanitizer tests reject scripts, event handlers, unsafe schemes, and unsupported active markup.
- [ ] Article document tests verify image count, duplicate detection, `aligncenter` layout markers, relative URL resolution, summary fallback, and distinct-image retention.
- [ ] App test confirms article selection passes body content to the viewer without injecting the Cards/Magazine image.
- [ ] WPF visual check compares the Armagh article against the supplied website example at narrow and wide reader sizes.

**Exit criteria:** Feed and article rendering regressions can be reproduced with local tests, and all expected differences from publisher websites are documented rather than handled with feed-specific hacks.

## Phase 1: Feed Refresh Diagnostics and Recovery

**Goal:** Help users understand stale or unavailable sources and recover without guessing.

**Work:**

- Define the user-facing meaning of refresh state: last attempt, last successful refresh, and current failure should not be conflated.
- Show refresh progress and a concise failure result for each affected feed, with a retry action scoped to the failed feed or current selection.
- Keep previously stored articles available after a refresh failure. Never clear cached articles or metadata because a request timed out or returned invalid content.
- Distinguish a feed with no new articles from a feed that could not be fetched.
- Decide whether refresh timestamps belong in shared catalog health metadata or profile-local refresh state before adding persistence. Do not repurpose Catalog Master health checks as proof of a user's recent content refresh.
- Avoid noisy notifications for routine auto-refresh; summarize failures in a stable status surface.

**Checks:**

- [ ] Application tests cover success, no-new-articles, timeout, cancellation, invalid XML, and partial failure across several feeds.
- [ ] SQLite tests verify that a failed refresh leaves existing article content, topics, author, images, and read/saved state intact.
- [ ] View-model tests verify retry scope, progress cleanup, and readable error summaries.
- [ ] Manual check with one working and one failing fake feed confirms successful feeds still update and the failure is actionable.
- [ ] Verify auto-refresh does not repeatedly display a modal dialog or block article reading.

**Exit criteria:** Users can identify which feed failed, when content last refreshed successfully, and retry without losing cached content or interrupting other feeds.

## Phase 2: Article Discovery and Search

**Goal:** Reduce the time needed to find relevant articles across existing folders, feeds, topics, and tags.

**Work:**

- Review current search behavior and document its fields and matching rules before extending it.
- Evaluate adding author, publisher topic, and feed name to local search, while keeping matching case-insensitive and deterministic.
- Add optional date-range filters only if existing date ordering and unread/saved filters do not cover the common task. Keep filter composition explicit.
- Improve empty states so they distinguish no articles in the current source, no articles matching search, and no articles matching the combined filters.
- Preserve search and filter state when moving between Follow Sources, a feed/folder, and the article reader unless the user explicitly clears it.
- Consider saved searches only after the core search fields and filter semantics are stable.

**Checks:**

- [ ] Application tests cover exact and partial matches in each supported field, case handling, blank queries, and special characters.
- [ ] Combination tests prove search composes with route, topic, feed-tag, unread, saved, and date filters without leaking state between profiles.
- [ ] View-model tests verify empty-state text and state retention when opening/backing out of an article.
- [ ] Manual check with a representative article collection verifies that search results are understandable and clearing one filter does not reset unrelated filters.
- [ ] Performance test records filter latency at increasing article counts without machine-specific pass/fail thresholds until a reference system is agreed.

**Exit criteria:** Search behavior is documented, filter composition is covered, and representative collections remain responsive.

## Phase 3: Reader Navigation and Reading Flow

**Goal:** Make repeated reading sessions require fewer pointer and navigation actions.

**Work:**

- Evaluate previous/next article controls that respect the current filtered and sorted article sequence.
- Provide keyboard commands for back to list, next/previous article, mark read/unread, and save/unsave. Show discoverable tooltips or menu labels for shortcuts.
- Preserve scroll position and selected article when returning to the same result list, unless the active filters changed.
- Confirm long article behavior: the reader should retain one clear scroll surface, keep image scaling predictable, and avoid text wrapping around centered block images.
- Keep link activation separate from article selection and continue opening validated external HTTP(S) links outside the embedded reader.

**Checks:**

- [ ] View-model tests cover previous/next boundaries, filtered ordering, deleted/removed article cases, and selection state.
- [ ] WPF tests cover keyboard focus, shortcut commands, action automation names, and Back behavior.
- [ ] Manual check reads a multi-article feed using keyboard only and verifies focus remains predictable.
- [ ] Manual check verifies long pages, centered and linked images, tables, and narrow-window layout.
- [ ] Verify external links still pass through the validated external-link handler and do not navigate the embedded viewer away from its article document.

**Exit criteria:** A reader can move through a filtered list and back without losing context, using either keyboard or pointer input.

## Phase 4: Catalog Maintenance and Following Sources

**Goal:** Make shared-catalog cleanup safer and reduce repetitive curator work.

**Work:**

- Add duplicate-feed URL detection/reporting with a clear distinction between exact URL duplicates and feeds that merely share a name.
- Improve feed health review with explicit check time and result; never imply that an old successful check guarantees current availability.
- Evaluate bulk category or collection updates, import preview/mapping, and batch health checks as separate changes with cancellation and partial-failure semantics.
- For destructive actions, show the impact on subscriptions, cached articles, collections, and profiles before confirming. Document whether deleting a catalog entry cascades existing data.
- Preserve the Follow Sources workflow and immediate synchronization after catalog changes.
- Defer automatic catalog cleanup and feed removal rules until a curator approves the proposed results.

**Checks:**

- [ ] CatalogService tests cover duplicate URL normalization, same-name distinct feeds, category/collection membership, and authorization.
- [ ] SQLite tests cover delete/update behavior and the agreed effects on subscriptions, cached articles, and collection membership.
- [ ] View-model tests cover selection, filtered-out selections, bulk-operation cancellation, and partial failures.
- [ ] App tests create/edit/delete a catalog feed, then verify Follow Sources reflects the change immediately without restarting.
- [ ] Manual review of a generated duplicate report confirms it recommends candidates but does not delete or merge feeds automatically.

**Exit criteria:** Catalog maintenance is reviewable, profile boundaries remain intact, and routine actions are recoverable and tested.

## Phase 5: Accessibility and Layout Resilience

**Goal:** Make the main reading and catalog workflows usable across keyboard, assistive technology, scaling, and resized windows.

**Work:**

- Audit accessible names for icon-only buttons, feed rows, article actions, dialogs, and custom title-bar controls.
- Verify keyboard order, visible focus, selection announcements, and escape/close behavior across article list, Follow Sources, preferences, and modal dialogs.
- Check minimum window sizes, high DPI, display scaling, long feed/article titles, long author/topic labels, and narrow layouts for clipping or overlap.
- Ensure article WebView2 content zoom and WPF text scaling have documented behavior; do not rely on color alone to communicate read/saved/failure state.
- Keep all controls aligned with `DOCS/Styling.MD` and shared application styles.

**Checks:**

- [ ] Automated WPF tests assert automation names on actionable controls and labels on input controls.
- [ ] Keyboard tests cover tab order and activation for core workflows.
- [ ] Manual screen-reader review covers opening a source, finding an article, reading metadata, and returning to the list.
- [ ] Manual layout matrix checks 100%, 150%, and 200% display scaling at minimum and wide window sizes.
- [ ] Capture and review screenshots for title truncation, metadata wrapping, toolbar overlap, and scroll affordances.

**Exit criteria:** Core workflows remain operable without a mouse and do not clip or overlap at supported scaling and window sizes.

## Phase 6: Offline and Network Resilience

**Goal:** Keep the user's library useful when sources or image hosts are temporarily unavailable.

**Work:**

- Document what is available offline: stored article text/HTML, metadata, and article state. Identify whether remote images are expected to remain unavailable offline.
- Evaluate bounded caching of remote article images only if offline image access is a demonstrated user need. Define disk limits, expiration, privacy, and cleanup before implementation.
- Keep network calls cancellable and bounded by existing size/time limits. Do not block window startup while waiting for feeds or remote images.
- Present cached article content when refresh fails and communicate that the content may be stale.
- Ensure retrying does not duplicate articles or reset read/saved state.

**Checks:**

- [ ] Tests simulate offline, timeout, cancellation, partial response, and oversized feed content.
- [ ] SQLite round-trip tests verify cached article HTML and metadata remain readable without network access.
- [ ] Manual airplane-mode check opens previously loaded articles, exposes stale/failure state, and does not hang the UI.
- [ ] If image caching is approved, tests cover cache size limits, eviction, expiration, unsafe URLs, and cleanup on profile/catalog changes.

**Exit criteria:** Previously stored text and state remain available offline, and network failure never blocks basic navigation.

## Phase 7: Performance and Data-Scale Review

**Goal:** Identify actual bottlenecks before adding expensive caching, paging, or indexing.

**Work:**

- Define representative article/feed volumes and a Windows reference machine before setting numeric targets.
- Measure startup, initial library load, feed refresh, search/filter updates, Follow Sources filtering, and scrolling separately.
- Track generated data sizes and test durations so measurements are comparable; avoid flaky thresholds tied to shared CI hardware.
- Preserve WPF virtualization and avoid nested scroll viewers that disable it.
- Add database indexes, paging, or background indexing only when measurements identify a specific hot path and a focused benchmark demonstrates improvement.

**Checks:**

- [ ] Record the reference machine, build configuration, dataset size, warm/cold state, and measurement method.
- [ ] Add deterministic generated-data tests for large feed and article collections.
- [ ] Profile allocations and UI-thread time for any measured hot path before changing its design.
- [ ] Compare before/after latency and memory use on the same reference setup.
- [ ] Re-run virtualization, sorting, filtering, and state-retention tests after performance changes.

**Exit criteria:** Any performance change has a reproducible baseline, a measured improvement, and no regression in correctness or scrolling behavior.

## Cross-Cutting Verification Checklist

Run checks relevant to the changed layer; do not use a full-suite run as a substitute for a focused regression test.

- [ ] `dotnet build src/RssReader.App/RssReader.App.csproj --configuration Debug`
- [ ] `dotnet test tests/RssReader.Domain.Tests/RssReader.Domain.Tests.csproj --configuration Debug`
- [ ] `dotnet test tests/RssReader.Application.Tests/RssReader.Application.Tests.csproj --configuration Debug`
- [ ] `dotnet test tests/RssReader.Infrastructure.Tests/RssReader.Infrastructure.Tests.csproj --configuration Debug`
- [ ] `dotnet test tests/RssReader.App.Tests/RssReader.App.Tests.csproj --configuration Debug`
- [ ] Run the narrow test filter for each changed behavior, especially when WPF host stability is involved.
- [ ] `git diff --check`
- [ ] Manually verify the affected workflow using deterministic data before release.

Use isolated output paths for verification builds when the repository's normal `bin`/`obj` directories may be in use, for example `-p:BaseOutputPath="$env:TEMP\RssReaderVerification\bin\"` in PowerShell.

## Release Readiness Checklist

- [ ] Scope and acceptance criteria for the phase are approved before implementation.
- [ ] Existing profile data initializes and migrates without loss.
- [ ] New and updated tests cover success, empty, failure, cancellation, and relevant boundary conditions.
- [ ] No live feed, image host, or WebView2 network dependency is required by automated tests.
- [ ] Accessibility names, keyboard behavior, and resized layout are reviewed for user-facing changes.
- [ ] Focused tests pass; broader test runs and any known WPF host limitations are documented.
- [ ] Build succeeds with no new warnings or errors.
- [ ] `git diff --check` passes and documentation reflects any changed product semantics.
