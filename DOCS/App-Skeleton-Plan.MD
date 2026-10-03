# App Framework and Skeleton Plan

## Purpose

Define the first buildable framework for the Windows RSS reader. The sidebar screenshot is a reference for information hierarchy and compact navigation, not a pixel-perfect specification. The app will use its own name, visual identity, labels, and styling; it will not reproduce Feedly branding.

This plan implements the decisions in `Architecture.MD`. Unresolved product choices there remain open and should not be silently turned into requirements.

## Proposed Solution Structure

```text
RSSreader.sln
src/
  RssReader.App/                 WPF entry point, windows, views, resources, view models
  RssReader.Domain/              Core entities, value rules, and authorization rules
  RssReader.Application/         Use cases, service contracts, validation, navigation state
  RssReader.Infrastructure/      SQLite, feed HTTP client/parser, repository implementations
 tests/
  RssReader.Domain.Tests/        Domain rules and profile/catalog authorization
  RssReader.Application.Tests/   Use cases, services, and view models
  RssReader.Infrastructure.Tests/ SQLite and feed parsing/refresh integration tests
  RssReader.App.Tests/           WPF composition and critical UI workflow tests
DOCS/
  Architecture.MD
  App-Skeleton-Plan.MD
```

The app project is the composition root: it configures the .NET Generic Host, dependency injection, logging, configuration, and WPF application lifetime. Views bind to view models; view models call application use cases; infrastructure implements application-facing storage and feed-fetching interfaces. Domain and application projects must not reference WPF.

Use MVVM commands and observable state consistently. Select and pin supported .NET and NuGet package versions when scaffolding, based on the installed SDK and current project requirements. Keep package-specific APIs behind application or infrastructure boundaries where practical.

## Main Window Shell

Use a resizable WPF window that fills the available work area. The desktop reading layout has three regions:

```text
+----------------------+----------------------+------------------------------+
| Navigation sidebar   | Article list         | Selected article             |
| Profile and routes   | For current route    | Reading view                 |
+----------------------+----------------------+------------------------------+
```

The sidebar is a stable, collapsible column, initially around 280 device-independent pixels, with a sensible minimum width. The article list starts around 360 device-independent pixels. The reading pane takes remaining space. These are starting proportions, not fixed sizes: smaller windows should collapse the sidebar or article list without clipping the active content. Resizing must not cause text, controls, or selection state to overlap.

### Sidebar Hierarchy

1. **Profile header:** current profile name and a profile-switch action.
2. **Primary routes:** Today, Follow sources, Search, and Go to.
3. **Reading shortcuts:** Read later and Recently read.
4. **Personal feeds:** All, expandable folders, and a Tags section with feed tags.
5. **Catalog Master mode:** show catalog-management navigation only when that profile is active.

Show unread/article counts where meaningful, aligned consistently and allowed to truncate or abbreviate at narrow widths. Use icon-and-label navigation with a clear selected state, tooltips for icon-only collapsed navigation, keyboard focus indication, and accessible names. Keep the profile chooser separate from the sidebar's profile-switch action.

Today, folder, tag, feed, search, Read later, and Recently read selections all open the same main workspace shell with a route-specific article query. Follow sources opens the shared catalog browser. Go to is a quick navigation/search command; its exact matching behavior can be refined during implementation.

The reference includes “Create AI Feed”; AI-generated feeds are not part of the agreed v1 scope. Keep the navigation composition extensible, but do not show a nonfunctional AI-feed control in v1.

## Startup and Profile Flows

- On startup, open the profile chooser before the reader shell.
- With no regular profiles, make **Create new profile** the primary visible action; do not show an empty profile list as the only content.
- With regular profiles, list them for selection and provide a create-profile action.
- Hold Shift to reveal the reserved Catalog Master entry. Handle both key-down and key-up and reset the modifier state if the window loses focus.
- Selecting a regular profile opens it directly if it has no password, or asks for its optional local unlock password.
- Selecting Catalog Master opens catalog-management mode. It starts without a password as specified; Shift visibility is not security. Enforce catalog permissions in application use cases even when the management controls are hidden.
- Profile creation validates names to letters, numbers, and spaces only, with at least one letter or number. Reserve the name Catalog Master. Case sensitivity and uniqueness still need a product decision.
- A recovery email can be stored as optional profile data, but the UI must not claim password recovery until a trusted recovery flow exists.

Profile switching must clear or reload all active-profile data before displaying the next profile. No article, subscription, folder, tag, or saved state from one profile may leak into another.

## Application and Data Boundaries

Keep shared catalog data distinct from personal profile data.

- **Shared catalog:** feed entries, categories, and collections, editable only through Catalog Master use cases.
- **Profile data:** profile settings, subscriptions to catalog feeds, folders, feed tags, article read state, and saved-for-later state.
- **Articles:** normalized metadata and feed-provided summary/content; each profile's read and saved state is separate from the shared article data.

Expected application use-case groups:

- Profile: list/create/select/switch/unlock profiles.
- Navigation: resolve the selected sidebar route into an article query.
- Catalog: browse and search the catalog; add/remove feeds, categories, and collections with Catalog Master authorization.
- Subscriptions: subscribe/unsubscribe to catalog feeds; organize subscriptions into folders and assign feed tags.
- Articles: refresh, filter, search, mark read/unread, and save/unsave for later.

All persistence queries that access personal state take the active profile identity explicitly. Authorization is checked in application logic, not inferred from whether a button is visible. Feed polling and SQLite work run asynchronously and never block the WPF dispatcher.

## Test Plan

“Test every element” means test each element's behavior at the appropriate layer, not write brittle unit tests for WPF pixels or framework internals.

- **Domain unit tests:** profile-name validation and reserved names; Catalog Master permissions; folder/tag rules; independent read and saved state; article filter combinations.
- **Application unit tests:** profile creation and switching, Shift reveal state, catalog permission enforcement, route-to-query mapping, search/filter behavior, error and empty states, and every view-model command/state transition.
- **Infrastructure tests:** RSS/Atom parsing, malformed and partial input, deduplication, conditional HTTP refresh, per-feed failures, SQLite persistence, and profile isolation using temporary databases and fake HTTP responses.
- **UI tests:** profile chooser empty/non-empty states, Shift reveal and focus-loss behavior, password prompt, main sidebar navigation, resizing/collapse, catalog-management visibility, and critical article read/save workflows.
- **Regression rule:** each feature adds or updates focused tests in the same change. Tests use deterministic fakes and do not depend on live feeds, real email delivery, or arbitrary sleeps.

## Build Phases

1. **Solution and shell:** create the solution/projects, configure the Generic Host and WPF startup, add logging and test projects, and show a blank profile chooser. Verify the app starts and tests run.
2. **Profiles:** implement profile persistence, creation/validation, chooser states, Shift-revealed Catalog Master, optional password hashing/unlock, and profile switching/isolation.
3. **Navigation shell:** implement the resizable sidebar, main workspace columns, profile header, routes, folders/tags presentation, and empty/loading/error states using fake data first.
4. **Catalog administration:** add catalog storage and browse/manage flows; enforce and test Catalog Master-only write access.
5. **Reader foundation:** implement subscriptions, feed polling/parsing, article persistence, filters, search, read state, and saved-for-later behavior.
6. **Fit and finish:** accessibility and keyboard navigation, responsive sizing, persistence migration checks, packaging, and end-to-end regression tests.

Each phase remains buildable and has passing focused tests before the next phase adds functionality.

## Skeleton Completion Criteria

- The WPF app launches into the correct profile chooser state.
- Create/select/switch profile flows are wired to application services, with profile isolation tests.
- Shift reveals Catalog Master, and catalog write permissions are tested outside the UI.
- The main shell presents the agreed sidebar hierarchy and three-region reading workspace with fake data.
- Sidebar routes update workspace state without putting data access or business rules in code-behind.
- All test projects build and their focused tests pass; resizing and keyboard navigation have UI-level coverage.
