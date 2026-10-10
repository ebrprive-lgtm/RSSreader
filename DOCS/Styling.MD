# Application Styling

This guide records the shared visual language for the WPF reader. Prefer the shared resource keys and established control styles over introducing local colors or one-off control templates.

## Color Palette

| Resource or role | Color | Use |
| --- | --- | --- |
| `WindowBrush` | `#F7F8F8` | Application and dialog background |
| `SidebarBrush` | `#F1F3F3` | Navigation sidebar surface |
| `TextBrush` | `#202927` | Primary text |
| `MutedTextBrush` | `#687773` | Secondary text, metadata, disabled labels |
| `AccentBrush` | `#476F63` | Primary actions, links, selected icons |
| `AccentWashBrush` | `#E4ECE8` | Selected and active surfaces |
| `ContentBrush` | `#FFFFFF` | Main reading and dialog content |
| `NeutralHoverBrush` | `#E8EEEB` | Hover state for neutral controls |
| `NeutralPressedBrush` | `#DCE4DF` | Pressed state for neutral controls |
| `WindowFrameBrush` | `#B8C4BE` | Contrasting outline around the main application window |
| `ControlBorderBrush` | `#D8DFDB` | Shared subtle control and frame borders |
| `SeparatorBrush` | `#E3E7E5` | Light separators between content sections and rows |
| `PrimaryHoverBrush` | `#3B5D53` | Hover state for primary actions |
| `PrimaryPressedBrush` | `#314E45` | Pressed state for primary actions |
| `PrimaryActionForegroundBrush` | `#FFFFFF` | Primary-action text in the light theme |
| `DisabledSurfaceBrush` | `#DCE5E0` | Disabled primary-action surface |
| `DestructiveBrush` | `#A33C35` | Destructive actions and error text |
| `DestructiveWashBrush` | `#FCE8E6` | Destructive icon-action hover surface |
| `SuccessBrush` | `#218739` | Successful status |
| `SuccessWashBrush` / `SuccessBorderBrush` | `#E8F4EA` / `#B7D8C0` | Successful status surfaces |
| `SuccessHoverBrush` / `SuccessPressedBrush` | `#D7EBDD` / `#C4E0CB` | Successful action hover and pressed states |
| `WarningBrush` | `#A65B00` | Warning status |
| `ModalScrimBrush` | `#66000000` | Dimmed backdrop behind modal overlays |
| `PublisherTopicPillBrush` / `PublisherTopicPillTextBrush` | `#E8F2EC` / `#53695C` | Publisher-provided topic pills |
| `UserFeedTagPillBrush` / `UserFeedTagPillTextBrush` | `#EAF1F8` / `#536B82` | User-created feed-tag pills |

Use semantic status colors only where meaning requires them. Prefer `DestructiveBrush` for shared error/destructive styling; retain another established red value only where a view-specific treatment requires it.

The **Light**, **Dark**, and **Warm** application themes update this palette across open windows and dialogs. XAML references to palette brushes must use `DynamicResource` so existing controls refresh when the profile theme changes.

## Typography

Use the platform UI font inherited by WPF controls. Keep text compact and scannable:

- Workspace title: about 25 px, semibold.
- Dialog and section headings: 19-21 px, semibold.
- Main controls and labels: 13-14 px.
- Metadata, counts, and helper text: 10-12 px in `MutedTextBrush`.

Use `Segoe MDL2 Assets` for the existing icon glyphs. Pair icons with clear text for primary actions and leave about 8 px between an icon and its label.

Article text size, line spacing, and font family are selected from the article viewer's compact typography popup; these settings affect article content only. Available font families are Segoe UI, Georgia, and Consolas.

## Layout and Shape

- Keep operational views dense, aligned, and easy to scan; favor whitespace through consistent margins rather than decorative cards.
- Article cards are an intentional content-surface exception: use a restrained 10-12 px corner radius and subtle separator, without heavy shadows.
- Keep article-card hover and keyboard-focus feedback visible but restrained; use the same clickable green publisher-topic pills in cards, magazine, and article views, and render clickable user-created feed-tag pills in light blue so they navigate to that tag's article list. Preserve a consistent `UniformToFill` image crop.
- Selecting a publisher-topic pill filters the current article list; from the article view, it returns through navigation history with that topic selected.
- Keep the title-only article list focused on the headline, with publisher and age as compact metadata; show folder labels and folder groups only in folder-sort mode, and keep date-sort mode as one global newest-first list.
- Keep the unsaved read-later bookmark hidden until its row is hovered or keyboard-focused; saved bookmarks remain visible.
- Use the existing 286 px navigation sidebar and restrained content margins in the main window.
- The navigation sidebar can be resized between 220 px and 480 px using its splitter; persist the chosen width with main-window placement.
- Use compact control heights around 32-38 px; keep icon buttons on stable square dimensions.
- Use small corner radii: 3-5 px for controls and 5-7 px for dialogs. The main reader window is an exception with an 18 px radius.
- Dialog boxes use a visible, subtle border around the surface, using `ControlBorderBrush` and a 5-7 px corner radius.
- Align counts and secondary values to the right while preserving space for the primary label.

## Window Chrome

- Application windows and dialog popups must not use the default Windows title bar; use application-owned window chrome instead.
- The main reader window uses an interactive resize frame and 18 px rounded corners. Keep its outer corners transparent by clipping the inner window content to the matching rounded geometry; preserve a hit-testable frame along resize edges and use square, edge-to-edge framing when maximized.
- Persist the main reader window's normal bounds and maximized state under the local application-data directory, and clamp restored bounds to the current virtual desktop.
- Dialog popups must have no native title bar or window-title text. In-content headings remain part of the dialog layout. Provide a visible X close button in the top-right corner of each dialog popup.
- The Catalog Master feed-preview popup keeps its Delete and Check Feed actions in the top-right action area, followed by a Close (X) button that only dismisses the preview. It still follows the no-default-title-bar and no-title-text rules.
- These rules apply to application-owned windows and popups. Operating-system-owned file pickers retain system chrome.

## Controls and States

- Use `PrimaryActionButtonStyle` for primary commands, `SecondaryActionButtonStyle` for neutral text actions, `IconActionButtonStyle` for compact icon commands, and `DestructiveIconActionButtonStyle` for destructive icon commands.
- Primary actions use `AccentBrush`, white semibold text, and a compact icon-plus-label layout. Hover and pressed states deepen the accent; disabled controls use a muted surface and muted text. Shared action styles provide a visible keyboard-focus ring.
- Secondary actions remain transparent or neutral until hover. Selected navigation and toggles use `AccentWashBrush` with `AccentBrush` accents.
- Destructive actions use the established red semantic palette and appear in context, such as on row hover or keyboard focus. Destructive icon actions use `DestructiveWashBrush` on hover.
- Every icon-only action needs a tooltip and an automation/accessibility name.
- Do not add tooltips that merely repeat an adjacent visible label. Use tooltips for icon-only actions, additional context, or truncated content.
- ComboBoxes use a white content surface, subtle border, consistent padding, a clear dropdown indicator, and custom hover, selected-item, disabled, and keyboard-focus states. Dropdown rows use the same neutral hover and accent selection colors.
- CheckBoxes use a consistent custom square indicator, accent fill when checked, visible keyboard focus, and a muted disabled state. Keep the label within the clickable control.
- Use RadioButtons for mutually exclusive choices, with a clear selected indicator and visible hover, disabled, and keyboard-focus states.
- Tab controls distinguish selected, unselected, hover, and keyboard-focus states using the shared accent and neutral resources; avoid native raised gray tab chrome.
- ComboBox selected values use `DisplayMemberPath` or an explicit `ItemTemplate` for model objects; do not display record/type `ToString()` output.
- Popup menus use a white surface, subtle border and shadow, compact padding, neutral hover, accent submenu selection, and a custom separator. Avoid native gray menu gutters and borders.
- Scrollbars use a slim rounded thumb with neutral, hover, and drag states. Keep a 2 px inset between the scrollbar and the viewport edge so it does not touch the content frame.
- Preferences actions use the shared primary, secondary, and icon action styles; do not fall back to native gray button chrome.
- Avoid the default gray WPF button appearance: apply an explicit shared or feature-level button style.

## Resources

The application-wide color brushes are declared in `src/RssReader.App/Resources/AppThemeResources.xaml`, and reusable action/form/tab/menu/scrollbar styles are in the resource dictionaries merged by `src/RssReader.App/App.xaml`. Main-window feature-specific control styles remain in `src/RssReader.App/MainWindow.xaml`; dialog-specific styles remain with their owning window unless they are reused elsewhere.
