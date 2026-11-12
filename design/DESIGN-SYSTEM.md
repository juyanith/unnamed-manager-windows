# Unnamed Manager design system

Which token to use, and when. The tokens are in `src/UnnamedManager/UI/`. The Fluent look stays: WinUI's theme resources come first, and only what WinUI has nothing for gets a token. [docs/DESIGN.md](../docs/DESIGN.md) describes the surfaces these tokens are used on.

## Theme.cs: accent and app theme
- **The app follows the Windows accent** (`Theme.DefaultId = "system"`), and there's no picker. `APP_ACCENT=<id>` (`tools/snap.ps1 -Accent green`) is only for comparing snapshots.
- **Read the accent through theme brushes:** `AccentFillColorDefaultBrush` for fills, `AccentTextFillColorPrimaryBrush` for accent text, `TextOnAccentFillColorPrimaryBrush` for text on an accent fill. Never a hex, never a fixed white.
- **The Guide uses the accent once:** the outline that flashes for a key that isn't bound. Everything else in it is primary or secondary text.
- **One accent button per page or dialog**, for the completing action or an empty state's first action. Most pages have none.
- **One app theme**, the config's `theme` (Light, Dark, Use system setting): the Guide, the tray menu and every window follow it, through `RequestedTheme` on each root and `TitleBar` `PreferredTheme` for the caption buttons (S-035).

## Theme brushes: color
- **Text:** `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush` (path separators, footer, descriptions), `TextFillColorDisabledBrush` (unavailable rows, with "not available in <App>").
- **Warning:** `SystemFillColorCautionBrush` with the warning glyph (E7BA), always beside words: config errors, failed actions, apps running as administrator. Errors that block saving use `InfoBar` `Severity="Error"`.
- **Surfaces:** cards use `CardBackgroundFillColorDefaultBrush` with `CardStrokeColorDefaultBrush`. The Guide's solid fallback is `SolidBackgroundFillColorBaseBrush`.
- **No `Surfaces.xaml` yet.** Add it only for a color WinUI has no brush for, with `Light`, `Dark` and `HighContrast` values (`HighContrast` pointing at a system color such as `SystemColorWindowTextColor`).
- **Never reuse a semantic brush for another job** (a stroke brush as text, a text brush as a fill). `{ThemeResource}` for brushes; `{StaticResource}` only for numbers, thicknesses and styles.

## Space.xaml and Grid.cs: spacing
- **Spacing comes from the 4 epx scale:** `SpaceXs` 4, `SpaceSm` 8, `SpaceMd` 12, `SpaceLg` 16, `SpaceXl` 24, `SpaceXxl` 36, `SpaceSection` 48. In code, `Space.Sm` and friends.
- **Pages:** `PageMargin` (36 on the sides), `PageHeaderMargin` (24 under the title), `GroupHeaderMargin` (32 above, 8 below), `CardPadding` (16).
- **The Guide:** `GuidePadding` (16) inside; `SpaceSm` between the path and the rows and above the footer; `SpaceMd` between a keycap and its title; `GuideColumnSpacing` (24) between columns.
- The `layout-grid` skill has the checks: `tools/grid-lint.ps1` and `tools/check-layout.ps1` both print PASS.

## Corners
- **WinUI's two radii are enough:** `ControlCornerRadius` (4) for controls and keycaps, `OverlayCornerRadius` (8) for cards, flyouts and dialogs.
- **The Guide's corners come from DWM** (`DWMWA_WINDOW_CORNER_PREFERENCE`, round), which matches `OverlayCornerRadius` and draws the system border and shadow. Never draw them in XAML.
- **No `Radius.xaml`.** Never write `CornerRadius="4"` in a page: use the theme resource.

## Layout.xaml: sizes
- **Bindings window columns:** `BindingsListColumnWidth` (360) on Bindings, `ListColumnWidth` (320, in Space.xaml) on Actions and Contexts; the detail card takes the rest. Help is one column at most `HelpMaxWidth` (640). Settings uses `ContentMaxWidth`.
- **Window sizes** (1040 × 720, minimum 760 × 520, docs/DESIGN.md) go in `UI/WindowBehavior.cs` when S-038 sets them.
- **The Guide:** rows at least `GuideRowMinHeight` (32); icons `GuideIconSize` (24); Top and Bottom positions `GuideEdgeOffset` (48) from the work area's edge; the flash outline `GuideFlashThickness` (2). Up to 12 rows make a column, at most three columns, never wider than 80 % of the work area. Those counts are constants in the Guide's code, next to the layout that uses them.
- **Keycaps:** at least `KeyCapMinWidth` × `KeyCapMinHeight` (24 × 24), so a column of single keys lines up; `KeyCapPadding` for longer names ("Backspace").
- **Min sizes, never fixed heights,** on anything holding text, so it grows with Windows' text size.

## Styles.xaml: named styles
- **`KeyCapStyle`** (a `Border`) with **`KeyCapTextStyle`** inside: the key in the Guide, Home, the Help page and the key recorder. `ControlFillColorDefaultBrush` with a `ControlStrokeColorDefaultBrush` border, like the keys in Windows' own shortcut hints.
- **`KeyCapCaptionTextStyle`:** the smaller key names in the Guide's footer.
- **Style controls with `Style`,** and when that isn't enough, a copy of the `ControlTemplate` with every visual state. Never rebuild a control from shapes: it loses the keyboard, focus and Narrator.

## Motion.cs: animation
- **Every animation goes through `Motion`.** `Motion.Animate(d)` returns zero when animation effects are off, so the setting applies everywhere.
- **Fluent's timings:** `Fast` 83 ms, `Normal` 167 ms, `Slow` 250 ms.
- **The Guide fades in and out with `Motion.GuideFade`** (`Fast`). It never slides or scales. A menu change inside it is instant: typing speed matters more than motion. Never animate the Guide's first layout.
- **A key that isn't bound** shows the flash outline for `Motion.ErrorFlashHold` (300 ms). A failed action's message stays `Motion.ErrorMessageHold` (2 s), then the Guide closes. These are states, so they show with animation effects off too.
- **In the Bindings window,** lists use WinUI's own add, remove and reorder transitions; no custom motion. Pointer hover never moves the selection.

## Still to add
- **`Overlay.cs`**, with S-005: one builder for the Guide's window (no-activate styles, DWM corners, acrylic with `IsInputActive` forced on, the solid fallback, position on the foreground window's monitor, the fade).
- **The tray icon's glyph and states**, with S-059 and the `app-icon` skill.

## Check
This should find nothing outside the token files:

```powershell
Get-ChildItem src -Recurse -Include *.xaml,*.cs |
  Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.Name -notmatch '^(Theme\.cs|Space\.xaml|Grid\.cs|Layout\.xaml|Styles\.xaml|Motion\.cs)$' } |
  Select-String -Pattern '#[0-9A-Fa-f]{6,8}\b|Color\.FromArgb|Colors\.(White|Black|Gray)|CornerRadius="\d|Duration="0:0:'
```
