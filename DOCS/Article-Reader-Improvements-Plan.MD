# Article Reader Improvements Plan

## Status

Planning only. No implementation changes have been made.

## Goals

1. Let readers request a clean, full-article view when a feed supplies only a short preview.
2. Show an article's feed thumbnail at the start of its reader content when the content has no inline image.
3. Prevent the feed-tag management icon from overlapping the unread-count badge when a feed row is hovered.

## Recommended behavior

- Fetch a publisher page only after the reader explicitly requests **Load full article**. Do not fetch publisher pages during normal feed refresh.
- Keep the feed-provided content and summary intact. Show extracted content as a separate view/state with an obvious **Show feed preview** action.
- Reuse a stored extraction when available; allow the reader to retry fetching it. If extraction fails, keep the feed preview available and offer the existing publisher link.
- For article rendering, use the supplied thumbnail as a leading image only when there is no valid inline image in the body. Do not show the same image twice.
- In the expanded feed list, reserve a distinct trailing layout area for the unread count and hover/focus actions. Never place the tag or unfollow actions over the count.

## Implementation plan

### 1. Prototype article extraction

- Define an application-level article-content service and an Infrastructure implementation that retrieves HTML and returns extracted article HTML plus useful metadata such as title, author, and retrieval time.
- Evaluate a Mozilla Readability-based .NET parser such as [SmartReader](https://github.com/strumenta/SmartReader). Test it against representative static pages and malformed/unreadable fixtures before selecting it; extraction quality is publisher-dependent.
- Keep fetching separate from extraction so requests remain controlled by the application. Use the original article URL as the base URL when extracting so relative links and images resolve correctly.
- Treat JavaScript-rendered pages, paywalls, anti-bot pages, and unavailable pages as normal extraction failures. Do not execute publisher scripts or bypass publisher restrictions.

### 2. Add safe, on-demand retrieval and persistence

- Validate the article target as an absolute HTTP(S) URL. Reject loopback, link-local, and private-network destinations; revalidate redirect targets to avoid redirects into local services.
- Apply cancellation, a bounded timeout, response-size limits, and HTML content-type checks. Surface errors in the reader instead of silently replacing content.
- Fetch through an application service, not by navigating WebView2 to the publisher. Preserve the WebView's current navigation restrictions and disabled-script behavior.
- Store extracted HTML and its retrieval timestamp separately from feed `Content` and `Summary`. Add nullable SQLite migration support and ensure ordinary feed refreshes do not overwrite a cached extraction.
- Reuse stored extracted content for repeat opens/offline reading. Keep refresh/retry behavior explicit to the user.

### 3. Wire the reader action and extracted-content view

- Add a **Load full article** action in the article-reader header when a publisher URL is available. Add a preview cue when the existing preview detection identifies summary-only content.
- Show loading and failure status; prevent duplicate concurrent requests for the same selected article.
- Display extracted HTML using the existing reader typography, theme, width, and link behavior. Pass it through the existing sanitizer and article document builder.
- Add **Show feed preview** and allow an explicit retry. Preserve the current article title, metadata, read state, and source-link behavior.
- Keep any word-count or preview heuristic advisory; do not suppress the action just because an item happens to be short.

### 4. Use the thumbnail as a leading reader image when needed

- Extend the article document-building flow so the article's available thumbnail is considered for both feed previews and full feed content.
- If the rendered body contains a usable inline image, preserve the body and do not prepend the thumbnail.
- If it does not contain an inline image, render the valid HTTP(S) thumbnail before the text. Resolve relative image URLs against the article URL, falling back to the feed URL, and keep HTML-encoding/sanitization behavior.
- Avoid duplicate display when the thumbnail is already represented by an inline image (including resized variants where existing duplicate-image logic can identify the same source).
- Preserve current behavior when no valid thumbnail is available.

### 5. Fix the feed-row hover overlap

- Restructure the feed-entry row in [SidebarView.xaml](./../src/RssReader.App/SidebarView.xaml) and its layout styles in [MainWindowSidebarResources.xaml](./../src/RssReader.App/Resources/MainWindowSidebarResources.xaml).
- Replace overlay positioning of the hover/focus tag-management and unfollow buttons with an explicit trailing action area. Reserve enough room for those actions while keeping the unread badge in its own non-overlapping column.
- Preserve the current compact/collapsed sidebar behavior, folder-row delete action, hover/focus visibility, click targets, tooltips, and accessibility names.
- Keep the fix within the shared palette and sizing guidance in [Styling.MD](./Styling.MD).

## Checks and acceptance criteria

### Article fetching and extraction

- [ ] A fixture with article text plus navigation, sidebar, footer, and ad elements displays the article body without those surrounding elements.
- [ ] Fixtures cover headings, paragraphs, lists, blockquotes, tables, images, captions, and relative links/images.
- [ ] Malformed HTML, empty extraction, missing pages, paywall/challenge pages, and non-HTML responses produce a visible, recoverable failure and retain the feed preview.
- [ ] HTTP(S) validation rejects unsupported schemes and local/private destinations; redirect handling rejects a redirect to a blocked destination.
- [ ] Cancellation, timeout, and response-size limits are enforced and tested.
- [ ] The WebView displays sanitized extracted output without running page scripts or navigating to the publisher in-process.
- [ ] Reopening an article reuses stored extracted content; routine feed refresh does not erase or replace it.
- [ ] SQLite migration is tested against an existing database without the new columns.
- [ ] Unit tests use local HTTP handlers/HTML fixtures and do not require live websites.

### Thumbnail placement

- [ ] A full article with no inline image and a valid thumbnail displays the thumbnail before the article text.
- [ ] A summary-only preview with no inline image continues to display its thumbnail at the beginning.
- [ ] An article with one or more inline images does not gain a duplicate leading thumbnail.
- [ ] Duplicate/resized thumbnail and inline-image variants are covered where they can be matched.
- [ ] Missing, invalid, or unsupported-scheme thumbnail URLs do not create broken images or alter text rendering.
- [ ] Relative thumbnails resolve against the article URL, or the feed URL if the article URL is unavailable.
- [ ] Existing image-centering, sanitization, theme, typography, and article-width tests continue to pass.

### Feed list hover layout

- [ ] In an expanded feed row with an unread badge, the badge does not intersect the tag-management or unfollow button bounds on hover or keyboard focus.
- [ ] The unread count remains visible and readable while either hover action is shown.
- [ ] Tag management and unfollow actions remain clickable, retain their existing commands, and do not activate the feed row.
- [ ] Verify the layout at the normal and narrow supported sidebar widths, including the collapsed sidebar state.
- [ ] Folder rows retain their existing expand/delete behavior and do not gain feed-only actions.
- [ ] XAML/UI tests verify visibility, automation names, and non-overlapping bounds; run the App test suite for regressions.

## Likely change areas

- Article display and document generation: [ArticleReaderView.xaml](./../src/RssReader.App/ArticleReaderView.xaml), [ArticleReaderView.xaml.cs](./../src/RssReader.App/ArticleReaderView.xaml.cs), [ArticleHtmlDocumentBuilder.cs](./../src/RssReader.App/ArticleHtmlDocumentBuilder.cs), and [ArticleHtmlSanitizer.cs](./../src/RssReader.App/ArticleHtmlSanitizer.cs).
- Article state/service/data flow: [MainWindowViewModel.cs](./../src/RssReader.App/ViewModels/MainWindowViewModel.cs), the Application service interfaces, [App.xaml.cs](./../src/RssReader.App/App.xaml.cs), [FeedArticle.cs](./../src/RssReader.Domain/FeedArticle.cs), and [SqliteArticleStore.cs](./../src/RssReader.Infrastructure/SqliteArticleStore.cs).
- Feed-list hover layout: [SidebarView.xaml](./../src/RssReader.App/SidebarView.xaml) and [MainWindowSidebarResources.xaml](./../src/RssReader.App/Resources/MainWindowSidebarResources.xaml).
- Regression coverage: article document/viewer and view-model tests, Infrastructure extraction and SQLite tests, plus WPF layout tests for feed-row hover/focus behavior.
