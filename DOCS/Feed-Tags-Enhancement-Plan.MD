# Feed Tags Enhancement Plan

## Purpose

Make profile-assigned feed tags a useful organization and discovery tool, building on the existing **TAGS** section in the left sidebar. This plan concerns user-created labels assigned to subscribed feeds. It does not change publisher-provided article topics.

## Current Baseline

- `ProfileFeedTags` stores a tag assignment for a specific profile and subscribed feed.
- `ReadingService` and `IReaderStore` already support reading, adding, and removing assignments. The service validates that a feed is subscribed before adding a tag.
- `MainWindowViewModel` loads the current profile's assignments, builds the sidebar tag links, copies feed tags onto article rows, and applies a selected `tag:` route as an article filter.
- The sidebar tags are sorted alphabetically and appear only when at least one subscribed feed has the tag.
- Unsubscribing removes the feed's assignments through the existing foreign-key cascade.
- The App has no user-facing command to add or remove assignments. In practice, the sidebar can filter tags that exist in storage, but the UI cannot create or manage them.
- Publisher article topics are separate article metadata and have their own filter. Do not merge those concepts or automatically convert topics into feed tags.
- UI additions must follow `DOCS/Styling.MD` and use the existing application styles.

## Suggestions

1. **Make tags assignable from a feed.** Add a `Manage tags` action to a subscribed feed's actions. Show existing tags as checkable choices and allow creating a new tag while assigning it. Removing a check removes only that feed's assignment.
2. **Show feed tags where they help explain a view.** Display compact, clearly named feed-tag chips on article rows/cards, distinct from publisher topics. This lets readers understand why an article appears in a tag-filtered view and makes the feed's organization visible elsewhere.
3. **Strengthen sidebar tag navigation.** Show a count of subscribed feeds per tag, preserve alphabetical ordering initially, and make the selected tag/filter state obvious. Clearing the tag filter should not clear search, folder, read, saved, or topic filters.
4. **Support bulk assignment.** Let users select several subscribed feeds and add or remove one or more tags in one action. This is especially valuable when importing or following a group of related feeds.
5. **Add a tag library manager.** Provide profile-scoped rename, delete, and merge operations. Show how many feeds use a tag before a destructive operation; define merge behavior so an already-existing assignment is not duplicated.
6. **Consider multi-tag views later.** Allow selecting multiple tags with explicit OR/AND semantics only after single-tag assignment and filtering are easy to use. Keep the initial sidebar as one tag per view.
7. **Defer automatic rules.** Keyword- or publisher-topic-based auto-tagging could be useful, but should be opt-in, explainable, and separately designed. It must never silently turn publisher topics into personal feed tags.

## Product Decisions

- Tags are personal labels on a feed subscription and remain scoped to the active profile.
- An article inherits the tags of its source feed for display and filtering; users edit tags on the feed, not independently on each article.
- A tag view includes articles from every subscribed feed carrying that tag and composes with the existing feed, folder, search, unread, saved, and topic filters.
- Unsubscribe continues to remove that feed's tag assignments through the existing cascade. Deleting or merging a tag affects assignments only in the active profile.
- Normalize surrounding whitespace and prevent blank labels. Define case-insensitive duplicate detection and collision behavior before implementing rename/merge.
- A tag's sidebar count should explicitly mean **feeds**, not articles, to avoid ambiguity.

## Phased Roadmap

### Phase 0: Confirm Semantics and Interaction

**Goal:** Keep feed tags predictable and distinct from publisher topics.

**Work:**

- Confirm that tags belong to subscribed feeds and are edited at feed level.
- Choose the most discoverable entry point for `Manage tags` on a subscribed feed row.
- Define trimming, case-insensitive duplicate handling, rename collisions, merge behavior, and empty-tag behavior.
- Confirm that counts mean tagged feeds and that existing filters compose without resetting one another.

**Exit criteria:** Product decisions are recorded before adding UI or changing persistence behavior.

### Phase 1: Single-Feed Tag Assignment

**Goal:** Make the existing storage and service operations available in the app.

**Work:**

- Add a feed-level tag editor with a clear list of existing tags, create, assign, and remove actions.
- Reuse `ReadingService.AddFeedTagAsync` and `RemoveFeedTagAsync`; add only the minimal view-model commands and input validation required by the UI.
- Refresh the sidebar tag links and affected article rows after an assignment changes.
- Keep all changes profile-scoped and unavailable for feeds not subscribed by the active profile.

**Tests:** Service validation for subscribed versus unsubscribed feeds; view-model command behavior; UI accessibility names, keyboard operation, and refreshing of the sidebar and article list.

**Exit criteria:** A user can create a tag and assign/remove it on one subscribed feed without leaving the current profile.

### Phase 2: Tag Visibility and Navigation

**Goal:** Make tags legible and useful beyond the sidebar list.

**Work:**

- Show compact feed-tag labels on article list/card presentations, visually distinct from publisher topics.
- Add a per-tag subscribed-feed count to the sidebar and retain the current alphabetical order.
- Provide a clear active-tag indicator/action that clears only the tag route/filter.
- Verify that a selected tag composes with feed/folder, search, read/saved, and publisher-topic filters.

**Tests:** Correct tag display and counts; empty and single-feed tags; filter composition and clear behavior; no cross-profile leakage or topic/tag cross-matching.

**Exit criteria:** Users can recognize feed tags on articles and understand the scope of a tag view without disrupting other filters.

### Phase 3: Bulk Tag Assignment

**Goal:** Reduce repetitive work when organizing many feeds.

**Work:**

- Add multi-feed selection to the subscribed-feed management surface.
- Support applying existing tags, creating a tag for the selection, and removing a tag from the selection.
- Report partial or failed operations clearly and refresh affected navigation and article state.

**Tests:** Bulk add/remove across several feeds; duplicate assignment; empty selection; transaction or recovery behavior if an operation is interrupted.

**Exit criteria:** Common multi-feed organization takes one bulk operation and preserves each feed's other tags.

### Phase 4: Tag Library Management

**Goal:** Keep the tag set understandable as it grows.

**Work:**

- Add profile-scoped rename, delete, and merge actions with feed-use counts and confirmation for destructive changes.
- Implement rename/merge as a transactional update over `ProfileFeedTags`; handle destination collisions without duplicate assignments.
- Remove a sidebar link when the last assignment is deleted and preserve the current route sensibly if its tag is removed.

**Tests:** Rename, delete, merge, collision, empty tag, and profile-isolation cases at the service and SQLite levels; update visible state after each operation.

**Exit criteria:** Users can maintain tags without leaving stale routes or changing another profile's organization.

### Phase 5: Optional Advanced Filtering

**Goal:** Support more expressive tag-based browsing only if single-tag usage demonstrates demand.

**Work:**

- Evaluate multi-select tag filtering and specify OR versus AND semantics before implementation.
- Consider saved tag views or explicit user-authored auto-tag rules as separate features.
- Keep publisher topics separate and do not claim a tag is automatic or publisher-supplied unless the user configured that behavior.

**Exit criteria:** Advanced tag features have a clear user need, documented semantics, and focused tests before implementation.

## Non-Goals

- Replacing the existing sidebar tag section.
- Turning RSS/Atom categories into user-assigned feed tags.
- Adding global tags to the shared feed catalog; these labels belong to each profile.
- Adding automatic tagging or multi-tag AND/OR behavior to the first release.
- Introducing a new persistence model for tag definitions before the existing assignment model proves insufficient.

## Implementation Order

Deliver Phases 0-2 as the first useful increment: users can assign tags, see them on feeds/articles, and navigate them from the sidebar. Implement bulk and library management next, preserving profile isolation. Treat automated rules and multi-tag views as separate follow-up decisions.
