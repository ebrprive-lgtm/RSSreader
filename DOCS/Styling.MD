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
| `ControlBorderBrush` | `#D8DFDB` | Shared subtle control and frame borders |
| `PrimaryHoverBrush` | `#3B5D53` | Hover state for primary actions |
| `PrimaryPressedBrush` | `#314E45` | Pressed state for primary actions |
| `DisabledSurfaceBrush` | `#DCE5E0` | Disabled primary-action surface |
| `DestructiveBrush` | `#A33C35` | Destructive actions and error text |
| `DestructiveWashBrush` | `#FCE8E6` | Destructive icon-action hover surface |
| `SuccessBrush` | `#218739` | Successful status |
| `WarningBrush` | `#A65B00` | Warning status |

Use semantic status colors only where meaning requires them. Prefer `DestructiveBrush` for shared error/destructive styling; retain another established red value only where a view-specific treatment requires it.

## Typography

Use the platform UI font inherited by WPF controls. Keep text compact and scannable:

- Workspace title: about 25 px, semibold.
- Dialog and section headings: 19-21 px, semibold.
- Main controls and labels: 13-14 px.
- Metadata, counts, and helper text: 10-12 px in `MutedTextBrush`.

Use `Segoe MDL2 Assets` for the existing icon glyphs. Pair icons with clear text for primary actions and leave about 8 px between an icon and its label.

## Layout and Shape

- Keep operational views dense, aligned, and easy to scan; favor whitespace through consistent margins rather than decorative cards.
- Use the existing 286 px navigation sidebar and restrained content margins in the main window.
- Use compact control heights around 32-38 px; keep icon buttons on stable square dimensions.
- Use small corner radii: 3-5 px for controls and 5-7 px for dialogs or framed surfaces. Avoid pill-shaped controls.
- Align counts and secondary values to the right while preserving space for the primary label.

## Controls and States

- Use `PrimaryActionButtonStyle` for primary commands, `SecondaryActionButtonStyle` for neutral text actions, `IconActionButtonStyle` for compact icon commands, and `DestructiveIconActionButtonStyle` for destructive icon commands.
- Primary actions use `AccentBrush`, white semibold text, and a compact icon-plus-label layout. Hover and pressed states deepen the accent; disabled controls use a muted surface and muted text. Shared action styles provide a visible keyboard-focus ring.
- Secondary actions remain transparent or neutral until hover. Selected navigation and toggles use `AccentWashBrush` with `AccentBrush` accents.
- Destructive actions use the established red semantic palette and appear in context, such as on row hover or keyboard focus. Destructive icon actions use `DestructiveWashBrush` on hover.
- Every icon-only action needs a tooltip and an automation/accessibility name.
- Avoid the default gray WPF button appearance: apply an explicit shared or feature-level button style.

## Resources

The application-wide color brushes and reusable action button styles are declared in `src/RssReader.App/App.xaml`. Main-window feature-specific control styles remain in `src/RssReader.App/MainWindow.xaml`; dialog-specific styles remain with their owning window unless they are reused elsewhere.
