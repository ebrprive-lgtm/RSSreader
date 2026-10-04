# Styling Consistency Plan

## Purpose

Bring every application view into consistent alignment with [Application Styling](Styling.MD). This is a focused styling and accessibility pass, not a visual redesign or behavior change.

## Implementation Status

- Phases 1-4 are implemented: shared action styles, action migration, custom window chrome, and palette/shape normalization.
- Phase 5 accessibility names and focused WPF verification are implemented.
- Manual visual review at 100% and 150-200% Windows display scaling remains outstanding.
- OS-owned file pickers retain system chrome and are outside this implementation scope.

## Current Baseline

The application already has shared palette resources in `src/RssReader.App/App.xaml`, reusable MainWindow styles, and a custom green Refresh action. The audit found several gaps:

- Several common actions still use the default WPF button template, including catalog entry actions, article-view actions, catalog filters and actions, preferences, and raw-feed close.
- Primary actions use different implementations. Some use an accent background without explicit hover, pressed, and disabled states.
- Create Folder and Choose Folder use 10 px corner radii, above the 5-7 px dialog limit in the style guide.
- Similar neutral hover and border colors are hard-coded differently across views instead of using named shared resources.
- A few icon-only controls have tooltips but no explicit automation name, including read-later and sidebar pin/peek controls.
- Profile chooser and splash headings are larger than the documented title scale without a documented display-title exception.

Views in scope: `MainWindow.xaml`, `ProfileChooserWindow.xaml`, `PreferencesWindow.xaml`, `SplashWindow.xaml`, `CatalogEntryWindow.xaml`, `CatalogFeedPreviewWindow.xaml`, `CreateFolderWindow.xaml`, `FolderSelectionWindow.xaml`, `RawFeedWindow.xaml`, `MessageDialogWindow.xaml`, and `ArticleHtmlViewer.xaml`, plus application resources in `App.xaml`.

## Goals

- Make primary, secondary, icon-only, and destructive actions visually consistent across views.
- Replace accidental platform-default button chrome with explicit application styles.
- Use shared palette resources for repeated surfaces and states; keep semantic status colors explicit.
- Align dialog and control radii with the documented shape scale.
- Replace native Windows title bars on application-owned windows and dialog popups with application chrome.
- Give every icon-only control a useful tooltip and an explicit automation/accessibility name.
- Document any intentional typography or control exceptions in `Styling.MD`.
- Preserve existing commands, bindings, keyboard behavior, and window workflows.

## Non-Goals

- Do not redesign layouts, navigation, or article presentation.
- Do not change control behavior, commands, data bindings, or wording except accessibility labels where necessary.
- Do not force all dialogs to use the same background surface; preserve the distinction between application canvas, content surface, and transparent shadowed modal shells.
- Do not replace the existing Segoe MDL2 glyph set or add a new icon/font dependency.
- Do not remove semantic success, warning, or destructive colors when they communicate status.
- Preserve resizability, modal results, keyboard cancellation, and other existing window behavior while changing chrome.
- OS-owned file pickers remain system UI; this pass covers application-owned windows and popup dialogs.

## Proposed Work

### Phase 1: Define reusable control variants

- Add shared or consistently reusable styles for primary actions, secondary actions, compact icon actions, and destructive icon actions.
- Primary actions use `AccentBrush`, white semibold text, compact sizing, and explicit hover, pressed, disabled, and focus states.
- Secondary actions use a neutral or transparent resting surface and the documented neutral hover state.
- Icon actions use stable square dimensions and consistent glyph sizing; destructive actions use the established red palette.
- Promote repeated neutral hover and border colors to named resources only where the same semantic role is used across views.
- Update `Styling.MD` with any deliberate exceptions, including profile chooser/splash display-title sizing and the behavior of focused row actions.

**Exit criteria:** The intended style variants and semantic resource names are clear, and repeated colors no longer require copy-pasted hex values.

### Phase 2: Migrate actions without changing workflows

- Apply the primary and secondary variants to `CatalogEntryWindow`, `CreateFolderWindow`, `PreferencesWindow`, `ProfileChooserWindow`, and `RawFeedWindow` actions.
- Apply the appropriate styles to MainWindow actions such as Show RAW, catalog Add/Retry/Clear actions, and other currently unstyled buttons.
- Preserve the custom Refresh style and the existing Catalog Feed Preview health-check/delete/close affordances, adjusting them only if the shared variants make them more consistent without losing their semantic distinction.
- Keep common keyboard behavior such as `IsDefault` and `IsCancel` intact.

**Exit criteria:** No frequently used app-owned button accidentally falls back to native gray WPF chrome; primary actions have consistent state treatment and workflows remain unchanged.

### Phase 3: Custom window chrome

- Remove the default Windows title bar from every application-owned window and dialog popup.
- Keep dialog window titles empty and provide a top-right X close action with a tooltip and automation name.
- Add custom minimize, maximize/restore, and close controls to the main application window while preserving resize behavior.
- Add a top-right close action to other application windows and titleless dialogs.
- Preserve the Catalog Master feed-preview exception: its Delete and Check Feed actions remain in the top-right action area instead of a close X; other feed previews retain their close action.
- Replace in-app `MessageBox.Show` dialogs with a reusable titleless modal that preserves OK and Yes/No results and provides an X close action. Do not replace the OS-owned OPML file picker.

**Exit criteria:** No application-owned window displays the default Windows title bar or title text; each dialog popup has a working top-right X except the Catalog Master preview, and existing resize/modal/command behavior is preserved.

### Phase 4: Normalize shape and palette use

- Bring Create Folder and Choose Folder modal corner radii into the documented 5-7 px range.
- Review other controls against the documented 3-5 px control radius and 5-7 px dialog/frame radius; preserve circular avatars and status markers as intentional exceptions.
- Replace near-duplicate neutral hover and border literals with shared resources where they have the same meaning.
- Retain explicit semantic colors for success, warning, error, and destructive actions.

**Exit criteria:** Repeated visual roles use the same resource values; remaining local colors and shape exceptions have a semantic or documented reason.

### Phase 5: Accessibility and view-wide verification

- Add explicit `AutomationProperties.Name` values to icon-only controls, including read-later toggles and sidebar pin/peek actions. Keep tooltips as supplementary descriptions rather than relying on them as accessible names.
- Review focus-visible states and ensure focused controls retain adequate contrast and hit targets.
- Review typography in the splash/profile chooser and either bring it into the documented scale or record its intentional entry-screen exception.
- Review all views at normal and enlarged Windows text/display scaling; check for clipped text, shifted layouts, and inconsistent button sizes.

**Exit criteria:** Every icon-only action has a meaningful automation name and tooltip; keyboard focus is visible; all views conform to the documented type and shape scales or have an explicit exception.

## Validation

- Add or extend focused WPF tests that verify style assignment, primary-action colors/states, icon control automation names, and modal corner radii.
- Keep behavioral tests unchanged except where a new accessibility name or style key is asserted.
- Build `RssReader.App` and run the focused App tests after each migration phase.
- Verify titleless custom chrome and dialog close behavior, including the Catalog Master preview exception and OK/Yes/No modal results.
- Perform a visual pass over all application views and popup windows listed above at 100% and 150-200% display scaling, including hover, pressed, disabled, selected, and keyboard-focus states.
- Re-run `git diff --check` and verify no command, binding, default/cancel action, or accessibility workflow regressed.

## Files Likely to Change

- `src/RssReader.App/App.xaml` for shared visual resources and reusable styles, if appropriate.
- `src/RssReader.App/MainWindow.xaml` for main workspace actions and icon-only control names.
- `src/RssReader.App/ProfileChooserWindow.xaml`, `PreferencesWindow.xaml`, `CatalogEntryWindow.xaml`, `CatalogFeedPreviewWindow.xaml`, `CreateFolderWindow.xaml`, `FolderSelectionWindow.xaml`, `RawFeedWindow.xaml`, and `SplashWindow.xaml` for view-specific styles and custom chrome.
- `src/RssReader.App/MessageDialogWindow.xaml` and `.xaml.cs` for titleless in-app confirmation and alert dialogs.
- `src/RssReader.App/App.xaml.cs`, `MainWindow.xaml.cs`, `FolderSelectionWindow.xaml.cs`, and `ProfileChooserWindow.xaml.cs` for app-owned popup routing.
- `src/RssReader.App/ArticleHtmlViewer.xaml` only if its embedded viewer status styling needs a shared muted-text resource.
- `tests/RssReader.App.Tests/MainWindowViewModelTests.cs` and focused owning-window tests for accessibility and style assertions.
- `DOCS/Styling.MD` to clarify intentional exceptions and shared variants.

## Implementation Guardrails

- Inspect local bindings and event wiring before applying shared templates; templates must preserve `Content`, `ContentTemplate`, padding, alignment, and command behavior.
- Prefer changing styles/resources over restructuring view layout.
- Do not impose primary styling on neutral Cancel, close, filter, or context-menu actions.
- Keep the scope limited to the documented styling gaps; any functional or layout redesign should be proposed separately.
