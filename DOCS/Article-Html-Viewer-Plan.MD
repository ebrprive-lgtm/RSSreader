# Article HTML Viewer Plan

## Status

Implementation in progress.

Completed so far:

- Pinned Microsoft WebView2 and HtmlSanitizer package versions.
- Added an explicit allowed-tag, attribute, CSS, and URI policy for feed HTML.
- Added a WebView2 article viewer with a native WPF header and a single browser-owned body scroll surface.
- Configured WebView2 to create its profile under the user's LocalAppData directory before initialization, avoiding access-denied failures from an unwritable default profile location.
- Preserved RSS/Atom body markup and HTML descriptions when no separate full-content element is supplied.
- Added feed/article base-URL handling for relative images, reader-owned document styles, CSP, disabled scripts, and external-link routing.
- Added focused parser, sanitizer, document-building, and WPF integration tests, including asynchronous WebView2 initialization and access-denied checks.

Remaining before considering the plan complete: manually verify reader scrolling and common layouts at narrow and wide sizes; verify external-link and missing-runtime behavior; confirm WebView2 Runtime deployment expectations for supported installations.

## Purpose

Replace the hand-built article-body `TextBlock` rendering with a dedicated HTML viewer that presents the complete content supplied by an RSS or Atom item. Common article markup, especially paragraphs, links, lists, tables, and inline images, must retain browser-like document flow without clipping, artificial page breaks, or visible HTML tags.

The viewer displays content provided by the feed. It does not fetch the publisher's full web page or bypass paywalls, authentication, or publisher access controls.

## Original Behavior

- `SyndicationFeedDownloader` retrieves RSS/Atom items and stores normalized summary and body fields in `FeedArticle`.
- RSS `content:encoded` and Atom content may contain HTML. The body markup is now preserved so inline images can reach the UI.
- `MainWindow.xaml` hosts the article body in a WPF `TextBlock` inside the detail view's `ScrollViewer`.
- `ArticleContentRenderer` parses a small subset of HTML and translates it into WPF `Run`, `LineBreak`, and `Image` inlines. This loses or approximates browser layout, CSS, tables, nested structures, and some whitespace. Large inline images and block elements can also interact poorly with WPF text layout.
- Article summary is plain text and can be a deliberately shortened publisher excerpt. When full body content exists, the detail view should show the body rather than making the excerpt appear to be the end of the article.

The renderer's growing collection of special cases is not the intended long-term solution.

## Goals

- Render the full available article body as one continuous HTML document.
- Support common RSS/Atom article elements with their normal structural meaning.
- Keep images within the reader width while preserving aspect ratio; allow them to load from valid HTTP or HTTPS URLs.
- Resolve relative image URLs against the article link, with the feed URL as a fallback base when available.
- Keep article navigation, loading, and error states inside the existing WPF reader workflow.
- Keep feed-provided markup isolated from application privileges and block active or local content.
- Preserve the existing summary fallback for feeds that provide no article body.
- Cover parsing, sanitization, control behavior, and critical reading workflows with deterministic tests.

## Non-Goals

- Extracting an article from the publisher's website when the feed omits it.
- Executing publisher JavaScript, loading embedded third-party applications, or displaying arbitrary iframes.
- Replicating every publisher stylesheet or guaranteeing identical rendering across publishers.
- Replacing the separate `Show RAW` view, which remains a diagnostic view of the matching feed item XML.

## Proposed Design

### Viewer Control

Add an `ArticleHtmlViewer` WPF `UserControl` in `RssReader.App`, backed by Microsoft WebView2. Give it a narrow input contract: sanitized body markup, a validated base URI, and an event for requested external links. Keep feed parsing in Infrastructure and WPF state in the App project; do not introduce WPF references into Domain or Application.

Replace the current article-body `TextBlock` and `ArticleContentRenderer` with the viewer control. The article title, source, date, read/save actions, and back action remain native WPF controls.

Load a complete app-authored HTML document around the sanitized article fragment. Apply reader-owned typography, colors, image sizing, table layout, and margins. Set the document language and viewport metadata, and use a base URL only after validating it as HTTP or HTTPS.

### Scrolling and Layout Spike

Before completing the control, prove the scroll behavior with a local fixture containing long text, several paragraphs, a tall image, and a wide table. WebView2 has its own scroll surface and should not be nested inside the current article `ScrollViewer` without testing.

Preferred initial layout: keep the WPF article header/actions visible in their existing region and give the WebView2 body the remaining reader height, with its own continuous browser scroll. Confirm that wheel, touchpad, keyboard, and scrollbar input feel natural. If the current header must scroll with the body, evaluate a measured-height WebView2 approach as a separate spike; do not add unbounded resize messaging or a second competing scroll viewer without evidence that it is stable.

### Content Extraction

- Continue to parse RSS and Atom with `SyndicationFeedDownloader`.
- Preserve raw article-body markup from RSS `content:encoded`, Atom HTML/XHTML content, and other supported content forms. Keep the summary normalized as plain text for list cards and excerpt fallback.
- Define deterministic precedence when a feed supplies more than one body source. Prefer the full content element over the summary/description; only use a summary as reader content when no body exists.
- Preserve current image discovery for the article's lead image unless the product decision is to remove the separate lead-image presentation. Avoid showing the same image twice when it is also in the body only if a reliable, tested comparison is possible.
- Persist HTML in the existing article `Content` field. Confirm the current SQLite upsert replaces that field after a feed refresh; no schema migration is expected because the column already stores text.

### HTML Sanitization and Resource Policy

Feed HTML is untrusted input. Sanitize it before navigation using a maintained HTML sanitizer compatible with the target framework. Pin the selected package version in the project and keep sanitization behind one App-level service so the allowlist is reviewable and independently testable.

Initial supported elements:

| Group | Elements |
| --- | --- |
| Document structure | `article`, `section`, `div`, `span`, `p`, `br`, `hr`, `header`, `footer` |
| Headings and emphasis | `h1`-`h6`, `strong`, `b`, `em`, `i`, `u`, `s`, `del`, `ins`, `small`, `sub`, `sup`, `abbr`, `cite`, `q` |
| Lists and quotations | `ul`, `ol`, `li`, `blockquote`, `dl`, `dt`, `dd` |
| Links and images | `a`, `img`, `figure`, `figcaption`, `picture`, `source` where safe and supported |
| Tables | `table`, `caption`, `thead`, `tbody`, `tfoot`, `tr`, `th`, `td` |
| Code and separators | `pre`, `code`, `kbd`, `samp`, `hr` |
| Optional media | `audio`, `video`, and `source` only with native controls and validated HTTP(S) media URLs; omit from the first release if the spike reveals excessive complexity |

Sanitization policy:

- Remove scripts, event-handler attributes, forms, iframes, objects, embeds, refresh/meta navigation, and unsupported active content.
- Remove `javascript:`, `data:`, `file:`, and other non-HTTP(S) resource and navigation URLs. Do not load local files.
- Permit only a documented subset of safe attributes, such as `href`, `src`, `alt`, table spans, and selected presentation attributes.
- Prefer reader-owned styles. If inline CSS is retained, allow only a small, reviewed property set for typography, color, spacing, borders, and alignment; reject URL-bearing CSS values.
- Use WebView2 navigation/resource events as a second boundary: keep top-level navigation in the article document, open validated HTTP(S) links using the existing external-link handler, block popups, and prevent feed content from running scripts.
- Do not grant the article document application host objects, local filesystem access, or arbitrary WebView2 script messaging.

### Rendering Behavior

- Paragraphs and block elements use natural browser flow and spacing.
- Images use `max-width: 100%`, automatic height, and lazy loading where supported. Missing or failed images do not prevent text from rendering.
- Tables fit the viewport where possible and scroll horizontally when their intrinsic width is larger than the reader.
- Links are visually identifiable, keyboard accessible, and opened outside the embedded viewer after URI validation.
- Headings, lists, quotations, inline emphasis, code, and line breaks retain their expected semantics.
- The summary is shown only when no readable full body is available. Feed-provided ellipses remain visible only if they are part of the full body itself, not as a substitute for body content.
- Empty, malformed, or blocked content falls back to the plain-text summary when present and never prevents the rest of the profile from opening.

### Lifecycle and Availability

- Initialize WebView2 asynchronously and do not block profile startup or the WPF dispatcher.
- Handle article selection changes without retaining stale page state or allowing an old article's resources to render into the new selection.
- Cancel or ignore late initialization/navigation results after the viewer is unloaded or the selected article changes.
- Handle missing WebView2 Runtime and initialization failures with a reader-level fallback message; do not show a raw exception dialog for ordinary article markup failures.
- Dispose event subscriptions and stop outstanding navigations when the control is unloaded.
- Verify package/runtime deployment expectations for supported Windows installations before release.

## Implementation Phases

1. **Prototype and decisions:** Add a small WebView2 spike with local fixture HTML. Verify runtime availability, natural body scrolling, keyboard focus, wheel/touchpad behavior, image sizing, and WPF layout before committing to the final control shape.
2. **Extraction contract:** Add tests for RSS `content:encoded`, Atom HTML and XHTML, plain text content, missing body, and summary fallback. Ensure the body remains markup and the list summary remains plain text.
3. **Sanitization boundary:** Select and pin the sanitizer package. Implement and test the tag, attribute, URI, and CSS policies before loading feed content in WebView2.
4. **Viewer control:** Build the reusable control, app-authored document wrapper, resource sizing, link handling, initialization states, and lifetime behavior.
5. **Reader integration:** Replace `ArticleContentRenderer` and the article-body `TextBlock`; preserve native article actions and lead-image behavior. Refresh stored articles once to populate preserved HTML in existing rows.
6. **Regression and release checks:** Run focused parser, sanitizer, Application, and App tests; manually inspect representative RSS and Atom articles with long content, missing images, tables, links, malformed HTML, and narrow/wide reader sizes.

## Test Plan

- **Infrastructure:** Feed fixtures verify full RSS encoded content, Atom HTML/XHTML, relative image markup, summary/body precedence, and plain-text fallback without live HTTP requests.
- **Sanitizer:** Verify common allowed tags survive; scripts, event attributes, unsafe schemes, local-file paths, iframes, and malformed active markup are removed or neutralized.
- **Viewer/control:** Use local deterministic HTML fixtures to verify image sizing, paragraph and list flow, tables, link interception, initialization failure, article switching, and no stale content after selection changes.
- **App workflow:** Verify selecting an article renders its full body, hides the excerpt when full content exists, and preserves the summary-only state when no body exists.
- **Manual matrix:** Test with WebView2 Runtime available and unavailable, offline image resources, long articles, wide tables, keyboard-only navigation, and resizing.
- Avoid live publisher dependencies, pixel-exact assertions, and tests that require external image URLs.

## Acceptance Criteria

- The article body is rendered by WebView2 as one HTML document rather than reconstructed from WPF text inlines.
- The listed common elements render with the expected structure; inline images display at the correct position and scale without breaking surrounding text flow.
- The reader displays the complete body supplied by the feed, and does not replace it with a shortened summary when both exist.
- Unsafe scripts, event handlers, local resources, and non-HTTP(S) URLs cannot execute or load.
- Links leave the embedded viewer through the existing validated external-link path; no feed content can navigate the application window to an arbitrary page.
- Missing runtime, malformed markup, failed images, or an unavailable feed do not crash the reader or block profile selection.
- Focused tests cover RSS/Atom extraction, sanitization, control behavior, and reader integration; all relevant test suites pass.