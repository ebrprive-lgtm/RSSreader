# Reader interface improvements

Reading layout, application theme, article-list presentation, and reader typography are saved per profile. Existing profiles retain the original full-page reader, default article-list density, light theme, and default typography unless changed.

## Reading layout

Use the split-view icon beside the article-list view controls, or in the full-page reader toolbar, to toggle split view. The choice is saved per profile; full page remains the default. When split view is active, drag the divider to resize the article list and reader. The initial split gives the list 45% of the workspace and the reader 55%; dragging is bounded to keep each pane useful, and the chosen ratio is saved per profile. Split view is available when the reading workspace is at least 680 device-independent pixels wide; narrower workspaces fall back to the full-page reader to preserve usable column widths. Article selection and previous/next navigation continue to use the active reading route.

## Article list density

Open **Preferences → Appearance** and choose **Default**, **Compact**, or **Spacious**. The setting changes card dimensions and the spacing of cards, magazine entries, and title-only rows without changing the selected presentation, sort order, or virtualization behavior.

## Application theme and reader typography

Open **Preferences → Appearance** to choose the **Light**, **Dark**, or **Warm** application theme. The selected palette is used throughout the interface, including the article reader, dialogs, navigation, and controls. The **Limit article width** option remains in Preferences.

In the article viewer, select the typography icon to change reader font size, line spacing, and font type (**Sans serif**, **Serif**, or **Monospace**). These choices are saved per profile and apply immediately to rendered article content. Feed HTML remains sanitized; feed-provided inline styles cannot override the selected reader colors or typography.

Saving density, theme, or reader-typography changes preserves the current route and selected article. Changing the **Start page** preference can navigate to the newly selected destination.

## Selecting and acting on articles

Choose **Select** above the article list to enter bulk-selection mode. Select visible articles in Cards, Title-Only, or Magazine view, then use the toolbar to mark them read/unread or save/unsave them. **Select visible** applies to the current filtered list. Leaving selection mode or changing the route/filter clears the transient selection.

Right-click an article in any list presentation for actions to open it in the reader, open its original source, toggle its read/saved state, or show its source feed. The reader's **More article actions** button exposes the same menu alongside direct read and saved controls.

## Keyboard navigation

- **Ctrl+K** opens the command palette when focus is not in a text-entry control.
- Type to filter destinations and commands; use the arrow keys and **Enter** to choose a result.
- **Esc** closes the palette; otherwise it navigates back when history is available.
- **Alt+Left / Alt+Right** moves between articles.
- **Ctrl+Shift+R / Ctrl+Shift+S** toggles the selected article's read/saved state.

## Unread and empty states

Feed, folder, tag, and All entries show an unread-count badge when they contain unread articles. Fresh articles published within the last 24 hours are labeled **New**; read articles show a **Read** marker and use lighter typography, so state is not conveyed by color alone.

Empty article lists explain the current route or filter and offer a relevant action when possible. Initial library loading and refresh activity are shown separately from empty results. A failed initial library load keeps its error visible and offers a retry.
