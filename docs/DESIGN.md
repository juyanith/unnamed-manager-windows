# Unnamed Manager for Windows: design plan

The windows-design plan for [BRIEF.md](BRIEF.md). It follows the Windows 11 rules in the windows-design skill (`design-rules.md`, cited as "rule N"). Words come from [../spec/GLOSSARY.md](../spec/GLOSSARY.md) and [GLOSSARY.md](GLOSSARY.md); on screen they're in sentence case. The Mac app's plan (`../unnamed-manager-mac/docs/DESIGN.md`) made the same choices for macOS; where Windows differs, this file says so.

## Feeling

**Quick and out of the way.** The app is felt, not seen. A known sequence runs with nothing on screen. When the Guide appears, it's small and still, shows its rows at once, and is gone the moment the sequence ends. No splash screen, no tour, no sound except the error beep, and no motion beyond the shortest fade.

## Archetype

A **tray app**: tray icon, the Guide panel, and one Bindings window with Settings as its last page. The real entry point is the leader chord. The tray menu is for status and control. The Bindings window is for setup, which you rarely do.

| Surface | What it is | Opened by |
| --- | --- | --- |
| Guide | A borderless WinUI window: topmost, never activated (`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, shown with `AppWindow.Show(false)`), not in the taskbar or Alt+Tab. Acrylic, with Windows 11's rounded corners and shadow from DWM. | Pausing during a sequence (S-005, S-031) |
| Tray icon | The app icon's glyph, with ready, off and warning states (S-059). Its tooltip says the state. | Always there |
| Tray menu | Looks and acts like a Windows 11 menu: a WinUI `MenuFlyout` shown from a small borderless host window at the tray icon (decided by the agent: a Win32 popup menu stays light in dark mode, can't show tooltips or a focusable error row, and can't be captured in a snapshot). | Right-click the tray icon, Shift+F10 or the Menu key on it, or a left click until the Bindings window exists |
| Bindings window | Mica, the `TitleBar` control, a left `NavigationView`: Bindings, Actions, Contexts, Help, and Settings as the gear at the bottom (rule 3, 4). Your own data lives here. | Edit bindings, Settings or Help in the tray menu; a left click on the tray icon (from S-038); starting the app again (S-010); a Show Help binding (S-051) |
| Welcome | A small Mica window with no navigation, shown on first start only (S-058). It lists the steps that finish setup; it isn't a tour. | First start |

**Why one window:** Windows 11 apps keep Settings as a page of their main window (rule 4). A tray utility with a separate Settings window would need the same navigation rules there anyway, and here the main window already exists for your bindings.

**Taskbar:** the app has no taskbar button while no window is open. The Bindings window and Welcome have one while they're open, like any window, so Alt+Tab and Win+Tab reach them. Closing the Bindings window hides it (`AppWindow.Closing` cancelled, then `Hide()`); Exit in the tray menu is the only way out. The window is created hidden at start and never minimized before its first frame.

**Tray left click:** in M1 a left click opens the tray menu, as there's no window yet. From S-038 a left click opens the Bindings window and a right click the menu, the usual split on Windows (Familiar). Update S-009's UX note when S-038 lands.

## Information architecture

**Bindings window pages** (one level, each with a Segoe Fluent Icons glyph):

| Page | Glyph | Content |
| --- | --- | --- |
| Bindings | Keyboard (E765) | A tree of leaders › menus › bindings on a card on the left, each row showing key, title and action, in file order; the selected row's form on a card on the right (S-038 to S-043) |
| Actions | Lightning bolt (E945) | A list of actions on the left, alphabetical; the selected action's form on the right, which changes with its type (S-044, S-045) |
| Contexts | App windows (E7C4) | An ordered list of contexts with their apps on the left; the selected context's apps on the right (S-046) |
| Help | Help (E897) | One centered column, at most 640 epx wide: your leaders and direct bindings grouped as in Home, the keys that work in every sequence, the words, and links (S-050) |
| Settings | the NavigationView gear | Windows Settings layout (below) |

Each page opens with its title in `TitleTextBlockStyle` and its commands in a `CommandBar` on the title row (rules 8, 9). List and detail: the list card is 360 epx wide on Bindings (key, title and action need the room) and 320 on Actions and Contexts; the detail card takes the rest. The window title names the page: "Bindings - Unnamed Manager" (rule 5).

**Window size:** opens at 1040 × 720 epx on the monitor you used last, remembers its size and page (rule 6), minimum 760 × 520 with every page usable (rule 7; the NavigationView collapses to a rail below 1008).

**Settings** (S-047, S-054), one page of `SettingsCard`s under group headers, most used first:
- **Appearance:** App theme (Light, Dark, Use system setting). It's the config's `theme`, so the Guide, the tray menu and the windows follow one setting (decided by the agent: the Mac keeps `theme` for the Guide only, since macOS discourages an app theme; Windows apps offer one, and two theme settings would confuse).
- **Guide:** Show the Guide (toggle), Wait before showing (ms), Close after (ms), Position (Center, Top, Bottom).
- **Keys:** Cancel key, Back key (key recorder buttons).
- **Feedback:** Flash on a key that isn't bound, Play a sound.
- **Windows:** Gap between windows (`NumberBox`, 0 to 64 epx, steps of 2).
- **Startup:** Run at startup (toggle). When Task Manager turned it off, the card says so and offers "Open startup apps" (`ms-settings:startupapps`).
- **About:** Unnamed Manager and its version, with "Open config file" and "Open log folder".

No accent picker: the app follows the Windows accent, as the brief says. The template's Accent color card goes in the design-system step.

## Command map

| Command | Where | Shortcut |
| --- | --- | --- |
| Status lines ("Not capturing leaders", "An app running as administrator is in front", Office shortcuts) | Tray menu, top, only when they apply | — |
| Config error lines (click to open the file) | Tray menu, only on error | — |
| Enabled | Tray menu, toggle item | — |
| Reload config | Tray menu; Bindings page overflow | F5 in the Bindings window |
| Open config file | Tray menu; Bindings page overflow; Settings › About | Ctrl+O in the Bindings window |
| Open file location | Tray menu | — |
| Edit bindings | Tray menu | — |
| Settings | Tray menu; the NavigationView gear | — (Windows has none) |
| Help | Tray menu; the Help page | F1 in the Bindings window |
| Turn off Office shortcuts / Turn on Office shortcuts | Tray menu, Welcome | — |
| Open log folder | Tray menu; Settings › About | — |
| Version line, config line | Tray menu, dimmed, above Exit | — |
| Exit | Tray menu, last | — |
| New binding / New menu | Bindings command bar; context menu | Ctrl+N / Ctrl+Shift+N |
| New action / New context | Their page's command bar | Ctrl+N |
| Record key | Context menu; the form's key field | Ctrl+K |
| Rename (edit the title) | Context menu | F2 |
| Duplicate | Context menu; overflow | Ctrl+D |
| Move up / Move down | Context menu | Alt+Up / Alt+Down |
| Delete | Command bar (last); context menu | Delete (scoped to the list, rule 19) |
| Undo / Redo | Command bar overflow | Ctrl+Z / Ctrl+Y |
| Bindings / Actions / Contexts / Help | NavigationView | Ctrl+1 / Ctrl+2 / Ctrl+3 / Ctrl+4 |
| Close the window | Title bar | Ctrl+W, Alt+F4 |

Command bars put New first and Delete last (commands.md); commands on the selection are disabled with nothing selected; context menus hide what doesn't apply (rule 17). The self-test checks that every command has a label and a shortcut.

**Inside a sequence** (the Guide, not menus): Cancel (Esc), Back (Backspace), and the step keys. The Guide's footer teaches Cancel and Back. Bindings are global chords, so they never appear as menu items.

## Empty states

A glyph, a one-line title, a line on what to do, and the first action as an accent button (rule 11).

| Where | Title | Line | Button |
| --- | --- | --- | --- |
| Bindings (only if the starter couldn't be written) | No bindings | Add a leader such as Hyper+Space, then the keys under it. | New binding |
| Actions | No actions | Actions are what bindings do: open an app, run a command, send a shortcut. | New action |
| Contexts | No contexts | A context is a set of apps, such as your browsers, that Context Aware actions check. | New context |
| Help, no bindings | No bindings | Add a leader with keys under it, or a direct binding such as Hyper+T. Your keys are listed here. The rest of the page still shows. | New binding |
| Bindings, invalid config | An error `InfoBar` above the tree lists the errors; the tree shows the last valid config (S-038, S-049). | — | — |

The Guide has no empty state: validation forbids empty menus.

## The Guide

The base for the design-system tokens and S-005:
- **Material:** acrylic (`DesktopAcrylicController`, base kind), as Windows gives flyouts. It's a transient panel, so acrylic is right (rule 30). Rows sit on the acrylic with no cards (rule 31). The Guide is never the active window, so its backdrop configuration sets `IsInputActive` to true, or the acrylic would fall back to a flat color.
- **Shape:** DWM's rounded corners (`DWMWA_WINDOW_CORNER_PREFERENCE`, round: 8 epx, `OverlayCornerRadius`) with the system's border and shadow. Keycaps use `ControlCornerRadius` (4).
- **Layout:**
  - Padding 16 epx. The path row first in `BodyStrongTextBlockStyle`, its › separators in secondary text, then 8 epx to the rows.
  - Each row, 32 epx tall: the keycap (24 epx tall, at least 24 wide, the key name centered in Body Strong; `ControlFillColorDefaultBrush` with a `ControlStrokeColorDefaultBrush` border, like the keys in Windows' own shortcut hints), 12 epx, then the title in Body, then a trailing › (chevron E76C) on menus in secondary text.
  - Icons (S-060): 24 epx between keycap and title; when any row in a column has one, every row in it keeps the space.
  - Footer below a divider, 8 epx above it: the Cancel and Back keycaps in Caption with their words, in secondary text.
  - Columns: up to 12 rows make one column, up to 24 two, more three, 24 epx apart. Home (S-021) puts group headers in Body Strong above each group.
- **Size:** fits its content, never resizable, never wider than 80 % of the work area.
- **Position:** centered on the monitor of the foreground window; Top and Bottom sit 48 epx from the work area's edge (S-034).
- **States:** a dimmed row uses `TextFillColorDisabledBrush` with "not available in <App>" (S-014). An error line shows the warning glyph (E7BA) in `SystemFillColorCautionBrush` with the words, never color alone (rule 28). The flash for an unbound key is a 2 epx `AccentFillColorDefaultBrush` outline for 300 ms.
- **Motion:** a short fade in and out, no slide or scale; a menu change is instant, because typing speed matters most. With animation effects off, it appears and goes at once (rule 41).
- **Transparency effects off or a contrast theme:** a solid `SolidBackgroundFillColorBaseBrush` background; theme brushes handle the rest (rule 29).
- **Theme:** the config's `theme` sets the Guide's `RequestedTheme`; with `system` it follows Windows' app mode live (S-035).

## Tray menu

- Items in the order of S-009's UX note, each with an access key; separators between groups.
- Status and error rows have a glyph (warning E7BA) and stay focusable, so the keyboard and Narrator reach them; an error row's tooltip holds the full text.
- Enabled is a `ToggleMenuFlyoutItem`. The version and config lines are dimmed items with no action.
- The menu follows the app theme and closes on Esc or a click elsewhere.

## Review set

Snapshot pages, as each surface lands (STORIES.md lists them all): `guide`, `guide-error`, `guide-home`, `guide-home-wide`, `guide-window`, `tray-menu`, `tray-menu-warning`, `welcome`, `bindings`, `bindings-error`, `actions`, `actions-window`, `contexts`, `help`, `settings`, and the `empty-*` pages. The Guide, the tray menu and Welcome need their own captures in `Snapshot.cs`. Every window is captured active, in light and dark, and once at the minimum window size.

## App icon and tray icon

**App icon** (`Icon/AppIcon.svg`, done): two violet keycaps, the back one lighter, and a white › on the front one. Press a key, go to the next: the Mac icon's idea, drawn the Windows way with no tile, flat layers, one soft gradient lit from the top left, and a thin shadow on the back key along the front key's top and left edges. Hand-drawn 16, 20, 24 and 32 px versions (`AppIcon-<size>.svg`) drop the gradient and keep every edge on a whole pixel. It stands apart from the apps pinned on this PC (File Explorer, Chrome, Edge).

**Tray icon** (S-059, planned): a one-color version of the same glyph, like Windows' own tray icons (network, volume), not the color app icon. At small sizes the color icon's two violets blur into one shape, and a one-color glyph matches the taskbar it sits on.
- **Glyph:** the back key as a 1 px outline, the front key filled, with the › cut out of it so the taskbar shows through. Drawn by hand at each size Windows asks for (`SM_CXSMICON`): 16 px at 100 %, 20 at 125 %, 24 at 150 %, 32 at 200 %.
- **Color:** white on a dark taskbar, near-black on a light one, read from `SystemUsesLightTheme` under `HKCU\...\Themes\Personalize` (the taskbar's theme, not the app's) and redrawn on `WM_SETTINGCHANGE` with `ImmersiveColorSet`.
- **States:** ready is the glyph alone. Off draws it at 40 % opacity. Warning keeps it at full strength and adds a small amber dot at the bottom right, with a dark rim so it reads on both taskbars. The tooltip always says the state in words ("Unnamed Manager, off"), so the state never rests on the picture alone.
- **Files:** `Icon/Tray-<state>-<light|dark>-<size>.svg`, rendered to `.ico` files by `make-icon.ps1` (extended in S-059 to take the tray set), loaded with `LoadImage` at the size the tray asks for. The review sheet shows each state on both taskbar colors, enlarged.

## Known gotchas

- **Never `Activate()` the Guide.** `Window.Activate()` takes focus from the app in front. Show it with `AppWindow.Show(false)` and the no-activate styles, and check in the self-test that the foreground window didn't change.
- **Tray icons are remembered forever** under `HKCU\Control Panel\NotifyIconSettings`, keyed by exe path. Test runs, snapshots and worktree builds skip `Shell_NotifyIcon`.
- **A new tray icon starts in the overflow.** Welcome tells you once how to keep it on the taskbar (Settings › Personalization › Taskbar › Other system tray icons).
- **The tray menu's host window** must take the foreground before the menu opens, or the menu won't close on an outside click.
