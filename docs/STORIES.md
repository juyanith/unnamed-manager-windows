# Unnamed Manager for Windows: stories

Built from [BRIEF.md](BRIEF.md), the shared spec in [../spec/CONFIG-SPEC.md](../spec/CONFIG-SPEC.md), and the Mac app's backlog (`unnamed-manager-mac/docs/STORIES.md`), which this one ports. Each story's `Mac:` line names the Mac story it follows; read that story's acceptance and "Verified" notes for behavior details and edge cases already found. There is no reference app, so `Screens:` names the snapshot pages that show each story. Words come from [../spec/GLOSSARY.md](../spec/GLOSSARY.md) and [GLOSSARY.md](GLOSSARY.md), which adds the Windows terms (tray menu, Exit, Run at startup) and says the UI uses sentence case: a name written "New Binding" here shows as "New binding".

**Which app a story changes:** from M10 on, each story has an `Apps:` line. The stories before that were built for WinUI. WPF is the app in `src/UnnamedManager.Wpf`, on the `wpf-app` branch until S-105.
- `WinUI`: only the WinUI app changes.
- `WPF`: only the WPF app changes.
- `WinUI, WPF (shared code)`: one change in Core or a linked file reaches both. Check it in both.
- `WinUI, WPF (own copies)`: each app has its own file, so the change is made twice.

When a story names both apps, its `[self]` and `[snap]` lines hold in both (S-106 gives WPF those checks). A line about a page only WinUI has holds in WinUI alone.

**Snapshot pages:**

| Page | Shows |
| --- | --- |
| `guide` | The Guide over a sample app |
| `guide-error` | The Guide after an unbound key or a failed action |
| `guide-home`, `guide-home-wide` | Home in one column, and groups side by side |
| `guide-window` | The Guide on a Windows menu with every window action |
| `tray-menu` | The tray menu, all OK (the app draws the menu itself, so snapshot mode can capture it) |
| `tray-menu-warning` | The tray menu with a config error, an Office key warning, or an app running as administrator in front |
| `welcome` | The first-run window |
| `bindings`, `bindings-error` | The Bindings page, and the same with an error InfoBar |
| `actions`, `actions-window`, `actions-focus`, `actions-swap`, `actions-arrange`, `actions-contextual` | The Actions page, the window action forms, and Context Aware, which has no form yet |
| `bindings-builtin` | A binding that uses a built-in action, with the Built in menu button |
| `contexts` | The Contexts page |
| `help`, `help-builtin` | The Help page, and its built-in actions |
| `settings`, `settings-windows`, `settings-end` | The Settings page, its Windows and Focus and swap groups, and its Apps and About cards |
| `empty-bindings`, `empty-actions`, `empty-contexts`, `empty-help` | Each page with nothing in it |
| `bindings-direct`, `bindings-repeat`, `bindings-contents`, `bindings-icon` | A direct binding's help, a repeat-key error, Contents on a menu with keys, the Icon field (S-086, S-091, S-092, S-095) |
| `actions-move`, `actions-contextual-fallback` | Move Window's Spot list, a Context Aware fallback (S-089, S-097) |
| `wpf-guide`, `wpf-guide-held`, `wpf-guide-home`, `wpf-guide-error`, `wpf-tray` | The WPF app's Guide and tray menu (S-106). A Guide page a later story names gets a `wpf-` twin |
| `guide-icons`, `guide-recent-empty`, `guide-recent-more` | Entries' own icons, an empty list, a list's More (S-093, S-100, S-101) |

**How each acceptance line is checked:** `[snap]` snapshot, `[unit]` unit test, `[self]` self-test, `[manual]` a person must try it.

**Seed data:** snapshot mode loads a fixture config (through `APP_STORE`) with two leaders, a nested menu, a direct binding, actions of every type, two contexts, and a Windows menu with one binding of each window action. Each of these can be created in the Bindings window or Settings (S-039 to S-047).

---

## M1 Core loop

Press a leader, step through a menu from the config file, and switch to an app. You can use it for a day by editing the JSON by hand.

Keyboard gotchas to respect throughout:
- The hook is `WH_KEYBOARD_LL` on its own thread with its own message loop. Its callback only hands the key to the engine and returns. Windows silently removes a hook whose callback is slow (`LowLevelHooksTimeout`), so nothing in it waits on the UI, the disk or another process.
- When the hook eats a key while Win is held, Windows still sees the Win key go up alone and opens Start, and with Hyper, the Office hub. Send a masking key (an unassigned virtual key, marked as the app's own) before the Win key-up.
- Mark every event the app injects (`dwExtraInfo`) and let the hook pass marked events through untouched.
- Match keys by scan code, so the physical position decides, not the layout. Extended scan codes (arrows, Insert, the right-hand Ctrl and Alt) carry the extended flag.

### S-001 Settle the Windows questions in the shared spec
As the maintainer, I decide in the spec repo how a Windows config names an app, which chords Windows reserves, and what `icon` names on Windows, with fixtures, so the Windows app builds a settled format.
Screens: none (no UI)
Mac: S-061
Acceptance:
- [unit] `CONFIG-SPEC.md` defines `exe` (a file name such as `Code.exe`, or a full path) and `appId` (an AppUserModelID) for Use Application and for contexts on Windows: exactly one of them per app, with rule ids, and `bundleId` an error on Windows.
- [unit] The Windows row of the reserved chords table is filled: at least `Cmd+L` (Win+L) and Ctrl+Alt+Delete, and what the app does about Hyper and the Office shortcuts.
- [unit] `icon` on Windows is defined (a Segoe Fluent Icons name or code point, or a file path).
- [unit] `fixtures/manifest.json` has Windows-only fixtures (`"platforms": ["windows"]`): one valid config with every Windows field, and one invalid fixture per new rule.
- [unit] The spec is tagged `spec-v4`, the `spec/` submodule here is pinned to it, and `SpecFixtureTests` runs every fixture for `windows`, with a pending list naming the story that builds each type not done yet.
Status: done
Verified: spec-v4 (spec repo e7bd731) defines `exe` and `appId` with `other-platform-field`, `bad-app-id` and `conflicting-fields`; reserves `Cmd+L` and Delete with Ctrl and Alt; makes the Office shortcuts the warning `office-shortcut`; and defines `icon` as a `U+` code point or a path (`bad-icon`). 11 Windows-only fixtures; the 35 shared fixtures that used `bundleId` only to have an action now use `exec`, so they hold on both platforms. `SpecFixtureTests` runs all 73 Windows fixtures and they pass; the pending list is empty, since S-002 reads every type in the spec (what each one does still comes with its story). Not yet run by the Mac app, which pins spec-v3 (noted in the spec CHANGELOG).

### S-002 Load and check the config file
As the user, I write `%USERPROFILE%\.config\unnamed-manager\config.json` and the app loads it when it starts.
Screens: tray-menu
Depends: S-001
Mac: S-001, S-039, S-059
Acceptance:
- [unit] Every rule in the spec's Validation rejects a config with a message naming the entry or action, and each issue carries the rule id; unused actions and contexts give warnings. The spec fixtures pass (S-001).
- [unit] A config with only `version` and one leader with one binding loads, with every setting at its default; entry order is kept exactly as in the file.
- [self] With no file, the app writes a starter config with the leader "Commands" (Hyper+Space) and two bindings, F → File Explorer (`exe` `explorer.exe`) and T → Terminal (Windows Terminal's `appId`), plus a `$schema` reference to `config.schema.json`, written next to it. It never writes over an existing file.
- [self] `APP_STORE` names the file instead, and tests and snapshots refuse to run without it.
UX:
- Flow: start the app → it reads the config file → missing: writes the starter config and loads it → the tray icon shows ready. Nothing to undo: the starter is never written over a file.
- Surface: none of its own. Status shows in the tray menu (S-008) and the tray icon's tooltip.
- States: missing → starter written, and the tray menu shows "Created config file" until the next reload. Invalid → the error state of S-008. Valid → ready.
- Words: the starter's leader is "Commands" (Hyper+Space) with bindings "File Explorer" (F) and "Terminal" (T). Errors name the path and the rule in one sentence, in the spec's wording, such as "Binding “Hyper+Space Q” uses action “open-foo”, which isn't defined." No codes, no blame.
- Access: n/a.
Status: done
Verified: `ConfigReader` and `ConfigValidator` in Core check every spec rule with its id and subject (73 fixtures), keep file order, and give unused actions and contexts as warnings; reading stops at the first problem, as in the Mac app. Every action type is read through `ActionRegistry`, one family per module. Unit tests: defaults, file order, error wording, key names, icons, the starter (98 tests). Self-test (11 checks): with no file the app writes the starter and `config.schema.json` next to it, never over a file, and loads it; a broken file is read, not replaced. Self-test and snapshots refuse without `APP_STORE` in %TEMP%. The template's SQLite list is gone; the window holds Settings only until M5. The tray menu's "Created config file" line comes with S-008.

### S-003 Start a sequence with a leader chord
As the user, I press Hyper+Space anywhere and the app takes over the keyboard until the sequence ends.
Depends: S-002
Mac: S-003
Acceptance:
- [unit] When idle, the engine consumes only leader chords; every other key passes through.
- [unit] Once a sequence starts, every key-down, repeat and matching key-up is consumed.
- [self] The hook callback returns in under 1 ms for 1,000 synthetic events, and events marked as the app's own pass through.
- [manual] Hyper+Space in a text field types nothing, and letting go of the modifiers opens neither Start nor the Office hub.
- [manual] Win+L and Ctrl+Alt+Delete still work while the app runs.
Status: done
Verified: `SequenceEngine` (a port of the Mac engine) and `KeyboardTracker` in Core: idle passes everything but leader and direct binding chords; in a sequence every key-down, repeat and key-up is consumed, keys with no name too; the masking key is asked for once while Win or Alt is held (unit tests). `KeyboardHook` is a `WH_KEYBOARD_LL` hook on its own thread; the self-test drives it with injected F24 F22 F23 and sees the menu, the child menu and the run; the callback takes 0.0003 ms on average and 0.008 ms at most over 1,000 events; marked events pass; turning capture off and on removes and installs the hook (self-test, 19 checks). Not yet tried by hand: both manual lines (Hyper+Space in a text field, Start and the Office hub on release, Win+L and Ctrl+Alt+Delete).

### S-004 Step through menus with single keys
As the user, I press A then T after the leader to reach Applications › Terminal.
Depends: S-003
Mac: S-004
Acceptance:
- [unit] A child menu's key opens that menu, and a child binding's key ends the sequence with that binding.
- [unit] Leader modifiers still held are ignored on step keys; any other modifier makes the step not match.
- [unit] Keys match by scan code (US ANSI positions), whatever the active layout. A test runs with a French AZERTY layout loaded.
- [unit] Menus nest without limit; a test with 6 levels passes.
Status: done
Verified: child menus open and child bindings end the sequence; held leader modifiers are ignored on step keys until let go, and any other modifier makes the step not match; 6 nested levels; keys match by scan code, with the extended flag telling Left from numpad 4, and a test that loads the French AZERTY layout (where the A position types Q) still picks A and T (unit tests, 115 in all). Through the real hook: F24 F22 F23 (self-test).

### S-005 See the options in the Guide
As the user, I see a small panel showing where I am and which keys I can press next.
Screens: guide
Depends: S-004
Mac: S-005
Acceptance:
- [snap] The Guide shows the path ("Commands › Applications") and one row per child, in file order: the key in a keycap, the title, and a › on rows that open a menu. The footer shows "Esc Cancel" and "Backspace Back".
- [snap] Light and dark both read clearly over a busy background; after 12 rows it uses two columns, after 24 three.
- [self] Showing the Guide never changes the foreground window, and the Guide is never in the taskbar or Alt+Tab.
- [manual] The app in front keeps its focus and caret, and gets no keys while the Guide is up.
UX:
- Flow: press the leader → the Guide appears (after the delay, S-031) → press a key → the Guide updates in the same frame, or closes when a binding runs. Esc cancels and Backspace goes back (S-007).
- Surface: a floating panel (side pane row: a tool kept open beside the work that you watch while you type). Topmost, never activated, borderless, 8 px rounded corners, acrylic, no title bar or close button, not in the taskbar or Alt+Tab.
- States: normal (path plus rows). Over 12 rows → two columns; over 24 → three. Long titles trim with an ellipsis, and Narrator reads the full title. A Context Aware binding with no match is dimmed and reads "not available in <App>" (S-014). An empty menu can't happen: validation blocks it.
- Words: the path is the menu titles joined with " › ", starting with the leader's title: "Commands › Applications". Each row: a keycap ("E", "Space", "F5"), the title, and a trailing "›" on menus. Footer keycaps "Esc" Cancel and "Backspace" Back, in secondary text.
- Access: keys come from the hook, so the Guide needs no focus. It raises a UI Automation notification with the path when it opens and on each menu change ("Applications, 3 keys"), without moving Narrator's focus. Keycaps don't rely on color. Text uses the type ramp, 12 epx or larger. With transparency effects off or a contrast theme, the background is solid in system colors. With animation effects off, it appears with no slide or fade.
Status: done
Verified: `GuideWindow` is borderless, topmost, `WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW`, out of Alt+Tab, shown with `AppWindow.Show(false)` and never activated; acrylic through `DesktopAcrylicController` kept active, DWM's rounded corners, border and shadow; a solid background with transparency effects off or a contrast theme. It fits its content (`ResizeClient`, then the caption height Windows adds is taken back off), centered or at Top or Bottom of the foreground window's monitor, at most 80 % of the work area wide. Rows are keycaps lined up per column, titles trim with an ellipsis, menus get a ›; up to 12 rows make a column, 24 two, more three. Narrator gets a notification ("Applications, 7 keys") and reads each row in words. Self-test (27 checks): the foreground window stays the same, the styles, Alt+Tab, path and row order, the footer, the column split. Snapshots `guide` and `guide-columns` in light and dark, over the app's own window; grid checks pass. Not done yet: the fade (it appears and goes at once; the fade comes with `Overlay.cs`), and a snapshot over a busy desktop. Not yet tried by hand: the manual line.

### S-006 Open or switch to an app with a binding
As the user, I press Hyper+Space E and VS Code comes to the front, starting first if it isn't running.
Depends: S-004
Mac: S-006, S-060
Acceptance:
- [unit] Use Application is the first action family on the registry: it decodes and checks its own fields with the spec's rule ids, writes itself back in a stable key order, gives its part of the JSON Schema, its UI name and its plan. The engine knows no action type by name.
- [self] Running means a top-level window belongs to a process with that exe, or a window carries that AppUserModelID. That window is restored if minimized and brought to the front.
- [self] Not running: an `exe` file name is found through App Paths and `PATH`, a full path is used as is, and an `appId` starts through `shell:AppsFolder\<appId>`.
- [self] An app that isn't installed ends the sequence and the failure is recorded (shown in S-016).
- [manual] The app comes to the front with focus, not just flashing on the taskbar, in under 100 ms when it's already running.
Status: done
Verified: Use Application reads, writes (exe or appId, then mode unless it's the default), gives the spec's two schema variants, its UI name and its plan; `ActionRunner` maps plans to Windows and knows no type elsewhere (unit tests, 131). `AppSwitcher` finds the most recent visible, uncloaked, unowned top-level window whose process has that exe (file name in any folder, a full path only that file, compared by real path) or whose window or process has that AppUserModelID (a Store app's content process behind ApplicationFrameHost); restores it and takes the foreground after pressing the masking key. Starting: App Paths, then PATH; `shell:AppsFolder` for an appId, checked first; a new window gets the foreground when it shows. Self-test (40 checks), against the app's own window: found by file name and by path, a minimized window restored and in front in under 100 ms, notepad.exe resolved, Explorer's appId found, a missing exe and a missing appId recorded as failures. Not tried: starting a real app from a binding, and the manual line with other apps.

### S-007 Cancel or go back mid-sequence
As the user, I press Escape to stop, or Backspace to go up one menu.
Depends: S-005
Mac: S-007
Acceptance:
- [unit] Cancel ends the sequence without running anything, from any depth.
- [unit] Back goes to the parent menu. In a leader's own menu it ends the sequence (Home comes in S-021).
- [snap] After Back, the Guide shows the parent menu's path and rows.
UX:
- Flow: during a sequence, Esc → the Guide closes and nothing runs. Backspace → the Guide shows the parent menu; in the leader's own menu, Backspace ends the sequence (Home replaces that in S-021).
- Surface: the Guide (S-005).
- States: if the Guide was still waiting for its delay, nothing appears. The feedback is that the next key types normally.
- Words: the footer keycaps name the configured keys; the Back keycap hides when Back is off (S-037).
- Access: a Narrator notification says "Cancelled" on Cancel, and the parent menu's name on Back.
Status: done
Verified: Cancel ends the sequence from any depth and runs nothing; Back goes to the parent menu, and from a leader's own menu ends the sequence (unit tests). Snapshot `guide-back` presses Hyper+Space A Backspace through the real engine and shows Commands with its rows, in light and dark. The footer names the configured keys and hides Back when it's off. Narrator gets "Cancelled" on Cancel and the parent menu's name on Back (the same notification as any menu change). Not tried by hand with Narrator.

### S-008 Reload the config and see its errors from the tray
As the user, I choose Reload config or Open config file in the tray menu, and if the file is broken I see why.
Screens: tray-menu, tray-menu-warning
Depends: S-002
Mac: S-008
Acceptance:
- [unit] A reload with errors keeps the last valid config running.
- [snap] With an error, the tray icon shows its warning state, its tooltip says "Config error", and the menu shows the first error with its entry path, plus "+2 more" when there are more.
- [self] Open config file opens the file in the app set for `.json`, or Notepad when none is; Open file location opens its folder with the file selected.
UX:
- Flow: tray icon (left or right click) → "Reload config" → valid: the menu closes, and the next open shows "Config loaded at 14:02". Invalid: the tray icon shows its warning state, and the menu shows the error above "Open config file". Way back: the last valid config keeps running, so nothing breaks.
- Surface: rows in the tray menu (tray icon flyout row: status while no window is open). Never a dialog or a notification: a broken file doesn't stop the app.
- States: OK → a dimmed line "Config loaded at 14:02" under the version line (S-009). Error → a section header "Config error", then the first error in sentence case, wrapped to 2 lines, with the full text in its tooltip, plus "+2 more" when there are more. Clicking the error opens the config file. Warnings (unused actions) never show in the menu, only in the Bindings window (S-049) and the log.
- Words: "Reload config", "Open config file", "Open file location", "Config error". Tray tooltip "Unnamed Manager, config error".
- Access: Win+B, the arrow keys and Enter or Shift+F10 open the tray menu. Items have access keys (R reload, O open, L location). The error row is a focusable item, so Narrator reads it in order.
Status: done
Verified: `ConfigState` keeps the last valid config on a reload with errors, and gives the first error and how many more (unit tests). The tray icon (`Shell_NotifyIcon` on a hidden window, version 4) shows ready, off or warning, with the tooltip in words; test runs never add it to the shell. The tray menu is a WinUI `MenuFlyout` from a borderless host window that takes the foreground first: "Config error" with the warning glyph, the first error with its entry path (a click opens the config file, the full text in its tooltip), "+2 more", Reload config, Open config file, Open file location, with access keys. Self-test (61 checks): reload, the warning state and tooltip, the error rows, fixing the file clears it, the editor (the app set for .json, else Notepad), Open file location's command. Snapshots `tray-menu` and `tray-menu-warning`, light and dark. Differs from the UX note: the error row is one line, trimmed, with the full text in its tooltip (a MenuFlyoutItem doesn't wrap). The tray icon's states come from the app icon until S-059 draws the one-color glyph. Not seen by me: the real tray icon on the taskbar.

### S-009 Turn it off and on, see the version, or exit from the tray
As the user, I right-click or click the tray icon, turn off Enabled so no keys are captured, see which version runs, or exit.
Screens: tray-menu
Depends: S-003
Mac: S-009, S-051
Acceptance:
- [self] When off, the hook is removed; turning it back on installs it again. Enabled is remembered across starts.
- [snap] When off, the tray icon is dimmed and its tooltip says "Unnamed Manager, off".
- [self] A release shows "Version 0.1.3"; a local build shows "Version 0.1 (branch, commit)", with ", modified" for a dirty tree.
- [self] Exit removes the hook and the tray icon before the app exits.
- [self] When Explorer restarts (`TaskbarCreated`), the tray icon comes back.
UX:
- Flow: tray icon → "Enabled" (a check mark that toggles) → the icon dims and leaders type normally again → choose it again to turn it back on. "Exit" stops the app with no confirmation, since nothing is lost.
- Surface: the tray menu, a toggle item (inline: changes one value). A right click, Shift+F10 or the Menu key opens the menu; a left click or Enter opens the Bindings window (S-038, DESIGN.md, Tray left click).
- States: on → checked, normal icon. Off → unchecked, dimmed icon, and a first line "Not capturing leaders". Enabled is remembered across starts.
- Words: menu order is the status and warning sections (only when there are any), "Enabled", a separator, "Reload config", "Open config file", "Open file location", a separator, "Edit bindings", "Settings", "Help" (each shown once its M5 story is done), a separator, "Open log folder" (S-016), the version line, the config line, "Exit". Version line "Version 0.1.3".
- Access: the tray icon's tooltip, which Narrator reads, includes the state: "Unnamed Manager, off". Enabled is a toggle item, so Narrator reads "checked" or "not checked".
Status: done
Verified: Enabled is a `ToggleMenuFlyoutItem`; off removes the hook, dims the icon, says "Unnamed Manager, off" and puts "Not capturing leaders" first; on installs the hook again; it's saved in the app's settings and read back as the next start would. The version line comes from `AppVersion` (unit tests): a local build shows "Version 0.1 (main, 02655db, modified)", stamped by build.ps1. Exit removes the hook and the tray icon, then exits; TaskbarCreated adds the icon again; the menu's items are in the UX note's order (self-test). A left and a right click both open the menu. The app now starts with no window: the Guide shows and draws with no window ever opened (checked by starting the app normally on a test config and pressing its F24 leader from outside). Not tried by hand: the real tray icon, Win+B and Shift+F10.

### S-010 Run only one copy
As the user, starting the app again while it runs doesn't capture keys twice.
Depends: S-003
Acceptance:
- [self] A second start finds the first copy (a named mutex for this user's session), tells it to open its tray menu, and exits without installing a hook.
- [manual] Starting it from Start while it runs shows the tray menu of the copy already running.
UX:
- Flow: start the app from Start or its exe while it runs → the new copy exits → the running copy opens its tray menu, with focus on the first item. From M5 it opens the Bindings window instead (S-038), since that's what starting an app from Start usually asks for. Way back: Esc closes the menu.
- Surface: the tray menu (tray icon flyout row), later the Bindings window.
- States: the tray icon is in the hidden icons overflow → the menu opens at the pointer instead.
- Words: none new.
- Access: the opened menu takes keyboard focus, and Narrator reads "Unnamed Manager menu".
Status: done
Verified: a named mutex (`Local\UnnamedManager`, or one per test config) finds the first copy; the second posts it a registered message through its tray window and exits with code 0 before any hook or window. The first opens its tray menu at the pointer with keyboard focus on the first item (self-test starts a real second copy). Not tried by hand: starting it again from Start.

## M2 Actions

Every action type in the spec, Home, and direct bindings. Each type is a family on the registry from S-006, added with no change to the engine or the Guide.

### S-011 Open a file, folder or URL
As the user, I bind a key to `~\notes` or `https://…` and it opens in its default app.
Depends: S-006
Mac: S-010
Acceptance:
- [unit] A leading `~` expands to `%USERPROFILE%`; nothing else is expanded. `/` and `\` both work as separators.
- [self] Files, folders and URLs open through the shell, as double-clicking them would.
Status: done
Verified: Open writes its `target`, gives the spec's schema part, and plans the target with only a leading `~` (then `/` or `\`) expanded to the home folder; URLs and other protocols are told from paths (unit tests, `ActionFamilyTests`). The runner hands the target to the shell with the `open` verb, as a double-click would, and a file or folder that isn't there is a failure ("Couldn't open C:\…\notes.txt: it isn't there."). Self-test (66 checks): `~` is the home folder; a folder, a file and a URL each go to the shell; the missing file. Not tried: opening a real file, folder and URL from a binding (the self-test doesn't open anything on screen).

### S-012 Run a command
As the user, I bind a key to `["code", "~\\notes"]` and it runs with no shell.
Depends: S-006
Mac: S-011
Acceptance:
- [unit] Arguments are passed literally, quoted so the program's `CommandLineToArgvW` gets each one back exactly, spaces and quotes included. Only a leading `~` in `command` and `cwd` is expanded.
- [self] `cwd` defaults to the home folder, and `env` is added to the inherited environment.
- [self] A program that can't be found ends the sequence with a failure; the app doesn't wait for one that starts.
Status: done
Verified: Run Command writes `command`, then `cwd` and `env` (keys sorted), gives the spec's schema part, and plans with only a leading `~` expanded in each part of `command` and in `cwd`. `CommandLine.Quote` quotes each argument so the real `CommandLineToArgvW` gives it back exactly (spaces, quotes, trailing backslashes, empty); `env` is added to the inherited environment (unit tests, 168). Self-test (70 checks), with real processes and their consoles kept closed: `cmd.exe` writes its folder (the home folder) and an `env` value; a 3-second `ping` returns at once; a program that can't be found is a failure. A console program opens its console window, as when started from Run.

### S-013 Send a shortcut to the app in front
As the user, I bind Hyper+F F to send Ctrl+F to the app I was using.
Depends: S-006
Mac: S-012
Acceptance:
- [unit] `send` parses one or more chords, and `Primary` maps to Ctrl.
- [self] Before the chord, the app sends key-ups for the leader modifiers still held, so the app in front sees only the chord's own modifiers; every sent event is marked as the app's own.
- [self] A chord with Win sends the masking key, so Start doesn't open.
- [manual] Ctrl+F reaches the app that was in front when the leader was pressed.
Status: done
Verified: Send parses one or more chords, `Primary` is Ctrl, it writes them as the app writes key paths, and its schema's key pattern is generated from the reader's names and equals the spec's character for character (unit tests). `ShortcutEvents` builds what's injected: key-ups for the leader modifiers still held, each chord's modifiers down, the key by scan code (with the extended flag), the modifiers up, and the masking key before Win or Alt goes up (unit tests, 176). `KeyboardHook.Inject` sends them in one `SendInput` call, every event marked; the self-test (73 checks) sees them pass the real hook without starting a sequence, and the runner read the held modifiers from the hook's tracker. Not tried by hand: the manual line.

### S-014 Pick an action by the app in front
As the user, I bind one key that sends Ctrl+L in a browser and Ctrl+F everywhere else.
Screens: guide
Depends: S-013
Mac: S-013
Acceptance:
- [unit] The first context, in `contexts` order, that matches the foreground window's exe or AppUserModelID and has a variant wins. Otherwise `fallback` runs; with no fallback, the action is unavailable.
- [snap] In the Guide, a Context Aware binding that's unavailable for the app in front is dimmed and reads "not available in <App>".
- [unit] Pressing an unavailable binding gives error feedback and runs nothing.
Status: done
Verified: `ContextResolver` (a port of the Mac's) runs the variant of the first context, in `contexts` order, whose apps include the app in front, matched by exe (file name or full path) or AppUserModelID; otherwise the fallback; with neither, the binding is unavailable. The engine knows the app in front at the leader and carries it to the Guide and the run; an unavailable binding is consumed and gives the `Unavailable` effect, staying in its menu; an unavailable direct binding lets its chord through (unit tests, 182). The app follows the window in front with `SetWinEventHook` (EVENT_SYSTEM_FOREGROUND) and names it from its file description. Self-test (77 checks): the app's own window is known by its exe, the runner runs its variant, nothing runs elsewhere. Snapshot `guide-back`: "Address bar" dimmed with "not available in Unnamed Manager", light and dark. Not done yet: the error flash and sound on an unavailable key (S-032, with unbound keys).

### S-015 Reserve a key with Do Nothing
As the user, I bind a key to `noop` to hold its place or show a note in the Guide.
Depends: S-004
Mac: S-014
Acceptance:
- [unit] A Do Nothing binding ends the sequence and does nothing else.
Status: done
Verified: A Do Nothing binding ends the sequence and plans `Nothing`, which the runner carries out without a failure; it writes no fields, and its schema part (and Show Help's) is the spec's (unit tests, 185). Show Help plans `ShowHelp`; until the Help page exists (M5), running it is a failure that says so.

### S-016 See why an action failed
As the user, when an app isn't installed or a command can't start, the Guide tells me and the log records it.
Screens: guide-error
Depends: S-006
Mac: S-015
Acceptance:
- [snap] The Guide shows a warning line, such as "Couldn't open Code.exe: not installed", for 2 seconds, then closes.
- [self] Every failure is written to `%LOCALAPPDATA%\UnnamedManager\logs\`, one file a day, the last 7 kept. Open log folder is in the tray menu.
Status: done
Verified: A failed action shows in the Guide as one warning line (the glyph in `SystemFillColorCautionBrush`, then the words, which wrap at 420 epx), even when the Guide wasn't up, and closes after `Motion.ErrorMessageHold` (2 s); a new sequence replaces it. Narrator hears the message. Every failure goes to `%LOCALAPPDATA%\UnnamedManager\logs\unnamed-manager-<date>.log` (a test run's sits next to its config), one file a day, the last 7 kept (`LogFiles`, unit tests, 188). The tray menu has Open log folder (G) before the version line. Self-test (81 checks): the Guide shows the failure, today's log has it, the Guide closes 2 s later, the menu item. Snapshot `guide-error`, light and dark. The wording differs from the example ("Can't find Code.exe. Check that it's installed, or use its full path."): it's the runner's message, which says how to fix it.

### S-017 Use Application with Focus, Start, or Focus or Start
As the user, I choose whether an app binding only focuses the app, starts a new copy, or focuses it and starts it if needed.
Depends: S-006
Mac: S-046
Acceptance:
- [unit] `mode` is `focusOrStart` (default, left out of the file), `focus` or `start`; an unknown mode is an error.
- [unit] Each mode plans its own way in: focus or start, focus only, a new copy.
- [self] Focus on an app that isn't running fails with "<App> isn't running."
- [manual] Start on a running Notepad opens a second window.
Status: done
Verified: `mode` reads `focusOrStart` (the default, left out when written), `focus` or `start`, and an unknown mode is `bad-app-mode` (fixtures, `UseApplicationTests`); each mode plans its own way in, and the runner focuses or starts by it (S-006). Focus on an app that isn't running now fails with "<App> isn't running.", naming the app by its program's own name ("Character Map isn't running.") and starting nothing (self-test, 82 checks). Start looks for a new window of the app, not an old one. Not tried by hand: Start on a running Notepad.

### S-018 Focus returns to the last window I used
As the user, I press a Use Application binding for an app that's running and land in the window I used last, not whichever of its windows happens to be on top.
Depends: S-017
Mac: S-047
Acceptance:
- [unit] The app's window highest in z-order wins, then a minimized one, which is restored; tool windows, cloaked windows and the app's own windows are skipped.
- [self] A window that doesn't answer (`IsHungAppWindow`) is skipped within a second, and the next one is used.
- [manual] With two File Explorer windows, use the second, switch to another app, press its binding: the second one is in front.
Status: done
Verified: `WindowPicker` in Core picks, front to back in z-order, the app's highest window that isn't minimized, else its highest minimized one (which the runner restores), skipping hidden, tool, cloaked, owned and hung windows and Unnamed Manager's own (unless the app asked for is Unnamed Manager) (unit tests, 197). `AppSwitcher.FindWindow` gathers those facts for every visible top-level window, asking `IsHungAppWindow`, which answers at once rather than waiting on the window. Self-test (82 checks): the app's own window found by name and path, a minimized one restored and in front in under 100 ms, the Guide never counted. Not checked by the self-test: a real hung window (the unit test covers the rule). Not tried by hand: the two File Explorer windows.

### S-019 Run an action straight from a chord
As the user, I give a top-level chord such as Hyper+T an action, and pressing it runs the action at once, with no menu and no Guide.
Depends: S-003, S-006
Mac: S-048
Acceptance:
- [unit] A top-level entry with an action loads as a direct binding; nothing can go under it.
- [unit] Its chord runs the action for the app in front and starts no sequence; holding it runs once; inside a sequence it's a key that isn't bound.
- [unit] A Context Aware direct binding with nothing to run for the app in front lets the chord through: the hook decides before it eats the key-down.
- [manual] Bind Hyper+T to Terminal, press it in another app: Terminal comes forward and no Guide appears.
Status: done
Verified: A top-level entry with an action loads as a direct binding with nothing under it (an entry under it is `parent-is-binding`); its chord runs the action for the app in front with no sequence and no Guide; holding it runs once, repeats and the key-up consumed; inside a sequence it's a key that isn't bound; a Context Aware direct binding with nothing for the app in front lets the chord through, decided before the key-down is eaten (unit tests, 201, `DirectBindingTests` and `ContextAwareTests`). The Guide only shows on a menu effect, so a direct binding never shows it. Not tried by hand: Hyper+T to Terminal from another app.

### S-020 Write Meh, CAG, Win and the Mac's modifier names
As the user, I write `Meh+W`, `CAG+T` or `Ctrl+Alt+T` in the config file and the app reads and writes them by the spec's names.
Depends: S-004
Mac: S-049
Acceptance:
- [unit] Every name in the spec's modifier table parses in any case, and `Cmd` means Win; exactly Meh's or CAG's modifiers print as `Meh+…` and `CAG+…`, and a group plus the missing modifier prints as `Hyper`.
- [unit] The symbols ⌃⌥⇧⌘ parse too, joined with + or right before the key, so a file written on a Mac reads; the app writes names.
- [unit] The JSON Schema accepts every form the app reads.
Status: done
Verified: Every name in the spec's modifier table parses in any case (Control, Ctrl, Option, Opt, Alt, Shift, Command, Cmd, Primary, Hyper, Meh, CAG); `Cmd` is Win and `Primary` is Ctrl; exactly Meh's or CAG's modifiers print as `Meh+…` and `CAG+…`, and a group plus the missing modifier prints as `Hyper`. The Mac's symbols ⌃⌥⇧⌘ read too, joined with + or right before the key, and the app writes names. The schema's key pattern accepts every form the reader takes and rejects what it rejects (unit tests, 238). The spec says the symbols work on the Mac; reading them on Windows goes past it without breaking a fixture, so a file written on a Mac reads.

### S-021 Go Back to Home
As the user, I press Back in a leader's menu and see Home: every leader and direct binding, with the ones on my current modifiers at the top.
Screens: guide-home, guide-home-wide
Depends: S-007, S-019, S-020
Mac: S-050
Acceptance:
- [unit] With two or more top-level entries, Back in a leader's menu opens Home; with one, it ends the sequence. Back or Cancel in Home ends it.
- [unit] Home groups top-level entries by exact modifiers, the group you came from first; picking keys follows the Mac's rules (S-050).
- [unit] Picking a leader opens its menu; picking a direct binding runs it for the app that was in front.
- [snap] Home with 12 rows or fewer in one column with group headers, and with more rows as groups side by side, never split.
Status: done
Verified: `Home` and the engine port the Mac's S-050 rules: with two or more top-level entries, Back in a leader's menu opens Home ("Home" starts the path), with one it ends the sequence; Back or Cancel in Home ends it; groups by exact modifiers, the one you came from first, the others in order of their first entry, rows in file order; in the current group the key alone picks with some, all or none of its modifiers held, others need their exact chord, a bare key prefers the current group; a leader opens its menu, a direct binding runs for the app that was in front, an unavailable one stays in Home (unit tests, 246). The Guide shows Home in one column with 12 rows or fewer (the current group, a line, then each other group under its header), and with more, the groups side by side under their headers, never split. Narrator hears "Home, 3 groups, 3 keys". Snapshots `guide-home` and `guide-home-wide`, light and dark; the self-test still passes (82 checks). Differs from the Mac: no vertical divider after the current group in the wide layout; its header says which group it is.

## M3 Window actions

Manage the window in front from the keyboard. Fields, ranges and geometry are in the spec's Window actions (v2) and the gap (v3). Each story takes its fixtures off the pending list.

Every window action works on the **foreground window**, the one the Guide leaves in front. A failure ends the sequence and shows as for any failed action.

Window gotchas to respect throughout:
- `GetWindowRect` includes Windows 10 and 11's invisible resize borders. Measure with `DWMWA_EXTENDED_FRAME_BOUNDS` and add the border back when calling `SetWindowPos`, so placed windows line up with no visible gap or overlap.
- The app is per-monitor DPI aware (v2). Spec points are effective pixels: physical pixels divided by the monitor's scale. Test with monitors at 100, 150 and 200 % side by side.
- Monitors left of or above the primary one have negative coordinates. Keep all geometry in one place in the core library and test monitors on every side.
- A window moved to a monitor with another scale resizes itself on `WM_DPICHANGED`. Move it first, then set its size.
- A maximized or snapped window is restored (`SW_RESTORE`) before it's placed, moved or resized.
- Windows report their own minimum and maximum sizes. Place them as close as they allow, anchored to the edge they belong to, and don't call it a failure.
- Cloaked windows (other virtual desktops, suspended Store apps) are never candidates.
- A window of an app running as administrator can't be changed from a normal app (UIPI). Fail with a message that says so (S-056).
- Full-screen windows (borderless and covering the whole monitor) are left alone, with a failure that says so.

### S-022 Maximize the window in front, and restore it
As the user, I press a key and the window fills its monitor's work area; with Press Again to Restore, pressing it again puts it back.
Screens: none (no UI)
Depends: S-001, S-006
Mac: S-062
Acceptance:
- [unit] The target is the work area (`rcWork`, without the taskbar) of the monitor that holds most of the window.
- [self] A window that can maximize is maximized with `SW_MAXIMIZE`, and pressing again restores it with `SW_RESTORE` (decided by the agent: the real maximize keeps Snap and the title bar button in step).
- [unit] A window that can't maximize is set to fill the work area, and its frame before is remembered per window, in memory only; a window that moved since forgets it.
- [self] No foreground window fails with "<App> has no window to maximize."
- [manual] In Edge, VS Code and File Explorer, Maximize and restore in one press each.
Status: done
Verified: The window actions run through `IWindowSystem` and `WindowControl` in Core, with `Win32WindowSystem` in the app and `FakeWindowSystem` for tests. The target is the work area of the monitor with most of the window, the primary when it's on none, including a monitor left of the primary (unit tests). A window that can maximize uses its own `SW_MAXIMIZE` and `SW_RESTORE`; without `toggle` it stays maximized. One that can't fills the work area and goes back, its frame kept per window in memory; a window that moved since forgets it; within 2 points counts as filled, at the monitor's scale; an app that sizes in steps still restores (unit tests, 261). Self-test: the app's own window maximizes and restores to the same frame; no window in front fails with "Notepad has no window to maximize." (85 checks). A full-screen window is left alone: "Edge is full screen. Leave full screen first." The desktop and the taskbar count as no window. Not tried by hand yet in Edge, VS Code and File Explorer.

### S-023 Minimize the window in front
As the user, I press a key and the window goes to the taskbar.
Screens: none (no UI)
Depends: S-001, S-006
Mac: S-063
Acceptance:
- [self] Against a fake window, Minimize minimizes it.
- [self] No foreground window fails with "<App> has no window to minimize."
- [manual] Focus moves to the next window, as when you minimize it from its title bar.
Status: done
Verified: Minimize plans a `WindowCommand.Minimize`, and `Win32WindowSystem` minimizes with `SW_MINIMIZE`, which activates the next window as the title bar button does (unit tests, 265). Self-test against a fake window: Minimize minimizes it, and no window in front fails with "Notepad has no window to minimize." (87 checks). Focus moving to the next window not tried by hand yet.

### S-024 Place the window on a half, third, quarter or corner
As the user, I press a key and the window moves and resizes to a part of the screen, such as the left half or the top right quarter.
Screens: none (no UI)
Depends: S-022
Mac: S-064
Acceptance:
- [unit] Presets cover halves, thirds, two thirds, quarters and center, and each maps to the expected frame on work areas of several sizes and scales.
- [unit] Fractions turn into whole pixels, and neighbouring placements share an edge with no gap or overlap (before the gap of S-030).
- [unit] Geometry is right for monitors on every side of the primary one, and for mixed scales.
- [self] The visible frame after placing (extended frame bounds) matches the target within 1 pixel, invisible borders included.
- [self] A fake window with a minimum size larger than the placement ends as close as it allows, anchored to the placement's edge.
- [manual] Left half, then right half in Edge, VS Code and Windows Terminal, on two monitors with different scales.
Status: done
Verified: All 14 presets map to their frames as exact fractions; edges round to whole pixels and sizes follow, so halves, thirds, two thirds and quarters share edges with no gap or overlap on work areas of odd sizes and on monitors left of, above, right of and below the primary; a window on a monitor at 150 % left of the primary is placed there; a larger minimum keeps the placement's edge, a smaller maximum stays centered in Center; a maximized window is restored and placed (unit tests, 292). `Win32WindowSystem` adds the invisible borders back for SetWindowPos and moves to another monitor before sizing. Self-test: the app's own window placed on leftHalf, rightTwoThirds and center has its visible frame on the target within 1 pixel; a fake window with a larger minimum ends anchored to the right edge (91 checks). The gap comes with S-030. Not tried by hand in Edge, VS Code and Windows Terminal or across monitors with different scales.

### S-025 Grow or shrink the window through preset widths
As the user, I press one key to make the window wider and another to make it narrower, through widths I chose.
Screens: none (no UI)
Depends: S-024
Mac: S-065
Acceptance:
- [unit] The next width is the smallest preset wider than the window now (or the largest narrower, for shrink); at the end of the list it stays put.
- [unit] The window keeps its left edge, unless that would push it off screen, when it keeps its right edge.
- [unit] With no `widths`, the default list is 1/4, 1/3, 1/2, 2/3 and 3/4.
Status: done
Verified: Grow goes to the smallest width more than 2 points wider, Shrink to the largest narrower, and at the end of the list it stays put; widths are fractions of the work area in any order; the window keeps its left edge and height, and its right edge when the left would run past the work area; with no `widths` the list is 1/4, 1/3, 1/2, 2/3 and 3/4. Resize Window's writer and schema are done for every variant (the schema matches the spec's); `to` and `by` run with S-027 (unit tests, 310).

### S-026 Move the window by a step or to a spot
As the user, I nudge the window by a set number of points, or send it to a spot, such as Center or Top Left.
Screens: none (no UI)
Depends: S-024
Mac: S-066
Acceptance:
- [unit] `by` moves by points; `to` places the window at fractions of the free space, so `{ 0.5, 0.5 }` is centered.
- [unit] A move never leaves the window off screen: at least 40 points of its title bar stay on a monitor.
- [self] Against a fake window, its size is unchanged after a move.
Status: done
Verified: `to` places the window at fractions of the free space ({ 0.5, 0.5 } centered, a window wider than the work area at its left edge); `by` moves by points, which are effective pixels at the monitor's scale; a move keeps at least 40 points of the title bar on the work area on every side; the writer leaves out a zero `dx` or `dy`, and the schema matches the spec's (unit tests, 329). Self-test: a fake window moved to the center and then by (-30, 20) keeps its size (92 checks).

### S-027 Resize the window by a step or to an exact size
As the user, I make the window bigger or smaller by a set amount, or set it to a fraction of the screen, with one corner or the center kept in place.
Screens: none (no UI)
Depends: S-024
Mac: S-067
Acceptance:
- [unit] `by` changes width and height by points; `to` sets them as fractions of the work area; the anchor stays put (default top-left).
- [unit] The result is clamped to the work area.
- [self] A fake window with a fixed size reports no failure and stays as it was.
Status: done
Verified: `by` changes width and height by points at the monitor's scale, `to` sets them as fractions of the work area, and each of the nine anchors stays put (default topLeft); the size is clamped to the work area and to 1 pixel, and a window that would run past the work area moves back inside; a window with a minimum keeps its anchor at the size it took (unit tests, 343). Self-test: a fake fixed-size window reports no failure and stays as it was (93 checks).

### S-028 Focus the nearest window left, right, up or down
As the user, I press a direction key and the window next to the one I'm in comes to the front with focus, even on another monitor.
Screens: none (no UI)
Depends: S-022
Mac: S-068
Acceptance:
- [unit] Candidates are visible, not minimized, not cloaked top-level windows on the current virtual desktop that show in Alt+Tab; tool windows, the Guide and the app's own windows are skipped.
- [unit] The nearest window is the one whose center is closest in that direction, within a 45° cone; ties go to the one with more overlap on the other axis.
- [unit] With no window that way, it fails with "No window to the left." (right, above, below).
- [self] With fake windows in a 2×2 grid across two monitors, each direction picks the expected window.
- [manual] Focus moves between VS Code and Windows Terminal side by side and across monitors, and the focused window gets the keys.
Status: done
Verified: Candidates are the windows Use Application already counts (visible, not cloaked, no owner, not tool windows), not minimized, without this app's own windows, the desktop and the taskbar. The pick is the nearest center within a 45° cone that way, and a tie in distance goes to the one with more overlap on the other axis, whatever their order; with none that way it fails with "No window to the left." (right, above, below); a minimized window is no candidate; a full-screen window can be focused away from (unit tests, 356). Self-test: fake windows in a 2×2 grid across two monitors at 100 and 125 % pick the expected window in each direction, and the real candidates leave out the app's own windows (95 checks). Focusing goes through Use Application's focus, which hands over the foreground with the masking key. Not tried by hand between VS Code and Windows Terminal yet.

### S-029 Focus the next or previous window
As the user, I press a key to go to the next window, and another to go back, in an order I can predict.
Screens: none (no UI)
Depends: S-028
Mac: S-069
Acceptance:
- [unit] The order is by monitor, left to right, then by window center, top to bottom and left to right; it wraps at the ends.
- [unit] The same windows always give the same order, whatever was focused last.
- [self] With fake windows, Next from the last goes to the first, and Previous comes back.
Status: done
Verified: The order is by monitor, left to right, then by window center, top to bottom and left to right, with owner and size breaking the ties that are left; the same windows give the same order in any order and whatever was focused last; Next and Previous wrap at the ends; with no window in front Next goes to the first and Previous to the last; with no other window it fails with "No other window to focus." (unit tests, 363). Self-test: on fake windows in a 2×2 grid across two monitors, Next from the last goes to the first and Previous comes back (96 checks).

### S-030 Leave a gap around placed windows
As the user, windows I place on halves and thirds stand a little apart from each other and from the screen's edges.
Screens: none (the Settings row is in S-047)
Depends: S-024
Mac: S-071
Acceptance:
- [unit] `windows.gap` defaults to 8, is written only when changed, and a negative gap is `negative-gap`; the spec-v3 fixtures pass.
- [unit] Edges on the work area's edge move in by the whole gap and edges inside it by half, so neighbouring presets end exactly one gap apart, for even and odd gaps.
- [self] With a gap of 8, the left and right halves stand 8 points from the edges and from each other.
Status: done
Verified: `windows.gap` defaults to 8 and a negative gap is `negative-gap`; the spec-v3 fixtures pass (none pending). Place Window rounds the gap to whole points, then to pixels at the monitor's scale; edges on the work area's edge move in by the whole gap and edges inside by half, the left and top by the larger half, so neighbouring halves, thirds and quarters end exactly one gap apart for gaps of 0, 1, 7, 8 and 6.6; a window keeps at least 1 pixel; a window that won't take its placement stays one gap in from the edge; Maximize leaves no gap (unit tests, 374). The runner reads the gap from the config each time a window action runs. Self-test: with a gap of 8, the app's own window on the left and right halves stands 8 points from the edges and from the other half (97 checks). "Written only when changed" waits for the config writer, which comes with the Bindings window (M5).

## M4 Timing and feedback

### S-031 Finish a known sequence with nothing on screen
As the user, I set `hudDelayMs` to 500 and fast sequences never show the Guide.
Depends: S-005
Mac: S-016
Acceptance:
- [unit] The Guide shows only after the delay passes with no key press; every key, Back included, restarts the delay.
- [unit] Once shown, the Guide stays until the sequence ends.
Status: done
Verified: `GuideSchedule` in Core holds a menu back until `hudDelayMs` passes with no key; every key, Back included, restarts the delay and the Guide shows the latest menu; a sequence that ends first shows nothing and hides nothing; once shown the Guide stays and follows the sequence until it ends (unit tests, 381). The app arms one dispatcher timer for the schedule's next deadline. The self-test passes its 97 checks; the delay with a real timer hasn't been tried by hand.

### S-032 Let an abandoned sequence time out
As the user, I set `timeoutMs` to 5000 and a forgotten Guide closes by itself.
Depends: S-031
Mac: S-017
Acceptance:
- [unit] The timeout runs only while the Guide is visible, and every key restarts it. With `showHud` false, it never fires.
Status: done
Verified: `timeoutMs` starts only once the Guide is visible; every key, unbound keys included, restarts it; when it runs out the Guide hides and the sequence ends in the key engine; with `showHud` false or a timeout of 0 it never fires (unit tests, 386). Self-test, through the real hook and real timers: with a delay of 300 ms the Guide is hidden at 150 ms and shown at 500 ms, and with a timeout of 600 ms it has closed and the sequence has ended by 1,300 ms (99 checks). The Guide's timing now comes from the config the keys run with.

### S-033 Get feedback for a key that isn't bound
As the user, a wrong key flashes the Guide and beeps, and the sequence stays where it was.
Screens: guide-flash
Depends: S-005
Mac: S-018
Acceptance:
- [unit] An unbound key leaves the state unchanged and gives an error.
- [snap] With `errorFlash`, the Guide shows its flash state (an accent outline).
- [self] With `errorBell`, an error plays the Windows "Default Beep" sound; without it, nothing plays.
- [unit] If the Guide was still waiting for its delay, it appears at once.
Status: done
Verified: A key that isn't bound, or a Context Aware binding with nothing to run here, leaves the sequence where it was; the schedule then plays the bell (`errorBell`), shows the Guide at once if it was still waiting for its delay, restarts the timeout, and flashes it (`errorFlash`); each switch turns its part off (unit tests, 390). Self-test, through the real hook: F21 in a sequence plays the Default Beep once (`MessageBeep(MB_OK)`), outlines the Guide and keeps the sequence; the outline is gone after 300 ms; with both off nothing plays or flashes (102 checks). Snapshot: %TEMP%\UnnamedManager-shots-unnamed-manager-windows\guide-flash-light.png and -dark.png, a 2 px accent outline that follows the window's rounded corners. Not checked by ear.

### S-034 Choose where the Guide appears
As the user, I set `position` to center, top or bottom of the monitor I'm using.
Screens: guide
Depends: S-005
Mac: S-019
Acceptance:
- [self] The Guide appears on the monitor that holds the foreground window, not the primary one, at its center, top or bottom, sized for that monitor's scale.
- [manual] On two monitors with different scales, the Guide is sharp and the same size in points on each.
Status: done
Verified: Core's `GuidePlacement` picks the monitor the window in front overlaps most (the primary one with none in front), centers the Guide across it, and puts it at the work area's center, or `GuideEdgeOffset` (48 points at that monitor's scale) from its top or bottom, any case; the Guide is at most 80 % of the work area wide in points (unit tests, 398). The Guide takes its monitor from the same list as the window actions, so its own monitor code is gone. Self-test: with the app's window in front, the Guide lands at the center, top and bottom of that monitor within 1 pixel (103 checks). This PC has one monitor, so "not the primary one" and two scales are not yet tried: the self-test moves the window to a second monitor when there is one.

### S-035 Set the app theme
As the user, I set `theme` to system, light or dark, and the Guide, the tray menu and the windows all follow it.
Screens: guide, tray-menu
Depends: S-005
Mac: S-020
Acceptance:
- [snap] Light and dark Guides both match the token colors, whatever the Windows app mode.
- [self] With `system`, the Guide, the tray menu and open windows follow a change of Windows' app mode while the app runs.
- [self] The config's `theme` is the only theme setting: the template's own app theme preference is removed (DESIGN.md, Settings).
Status: done
Verified: The config's `theme` is the only theme setting: the template's app theme preference and its Settings card are gone (the card comes back with the config writer, S-047), and the window, the Guide and the tray menu all read `App.ThemeMode`; a reload applies a new theme to the open window. Self-test: with `light` and `dark` the Guide and the window are that theme; with `system` they match Windows' app mode and pin nothing (`ElementTheme.Default`), and the tray menu's status carries the same theme (108 checks). Snapshots: guide, settings and tray-menu, light and dark, in %TEMP%\UnnamedManager-shots-unnamed-manager-windows. Not tried by hand: switching Windows' app mode while the app runs.

### S-036 Run sequences with no Guide at all
As the user, I set `showHud` to false and never see the panel.
Depends: S-031
Mac: S-021
Acceptance:
- [unit] With `showHud` false, sequences still resolve, errors still beep, and no Guide window is created; failures go to the log only.
Status: done
Verified: With `showHud` false the schedule never asks for the Guide: a sequence still resolves to its binding, a wrong key still gives the bell, and nothing flashes or times out (unit tests, 399). The Guide's window is now made the first time it shows. Self-test, run first through the real hook: with `showHud` false, F24 F21 F22 F23 runs "Deep" and beeps once, a failing action is written to the log, and no Guide window has been made (110 checks).

### S-037 Choose my own cancel and back keys
As the user, I set `cancelKey` to Q and `backKey` to an empty string to turn Back off.
Depends: S-007
Mac: S-022
Acceptance:
- [unit] The set keys work, an empty `backKey` turns Back off, and a step on either key fails validation.
- [snap] The Guide's footer names the set keys, and hides Back when it's off.
Status: done
Verified: The reader, the validator and the footer were in place from M1; this story adds their tests. `cancelKey` Q cancels and `backKey` Left goes back, while Escape and Backspace become ordinary unbound keys; an empty `backKey` turns Back off, so Backspace is unbound in a sequence and may be a step key; a step on either key is `step-is-runtime-key`, the same key for both `same-cancel-back`, and a non-key `bad-runtime-key` (unit tests, 407). Snapshot: %TEMP%\UnnamedManager-shots-unnamed-manager-windows\guide-keys-light.png and -dark.png, with the footer showing only "Q Cancel".

## M5 Bindings window and Settings

Edit everything without touching JSON. The Bindings window is a WinUI window with Mica and a NavigationView: Bindings, Actions, Contexts, Help, and Settings in the footer (decided by the agent: Windows 11 apps keep Settings as a page, not a second window). It opens from Edit bindings in the tray menu, and Settings from Settings in the tray menu.

### S-038 See all bindings as a tree
As the user, I choose Edit bindings in the tray menu and see every leader, menu and binding in a tree.
Screens: bindings, bindings-error
Depends: S-002, S-009
Mac: S-023
Acceptance:
- [snap] The Bindings page shows a tree with key, title and action for each row, in file order.
- [snap] An invalid config shows an InfoBar above the tree listing the errors, and the tree shows the last valid config.
- [self] Closing the window keeps the app running in the tray.
- [self] From now on a left click on the tray icon opens this window, and a right click the tray menu (DESIGN.md, Tray left click). S-009's UX note is updated to match.
Status: done
Verified: self-test (a left click opens the window on Bindings, a right click the menu, the tree in file order with key, title and action, closing hides and the app keeps running, an invalid file shows its errors over the last valid tree), unit (BindingLabelsTests), snapshots bindings and bindings-error in light and dark.

### S-039 Add a menu or binding
As the user, I select a menu and choose New Binding or New Menu (Ctrl+N and Ctrl+Shift+N).
Screens: bindings
Depends: S-038, S-048
Mac: S-024
Acceptance:
- [self] The new entry goes at the end of the selected menu, with its key recorder active; with nothing selected, New Binding adds a direct binding (S-019).
- [unit] An entry with no key, or a key already taken in that menu, isn't saved, and the reason shows under the field.
Status: done
Verified: self-test (New binding goes at the end of the selected menu, selected, with the key field focused and recording; it isn't saved without an action and says so; with nothing selected it is a direct binding; New menu adds a menu; undo takes them out of the file), unit (ConfigEditsTests: first free key, Office chords and the cancel and back keys skipped, key clashes reported, the draft waits for an action).

### S-040 Record a key
As the user, I click a key field and press the chord or key I want.
Screens: bindings
Depends: S-039
Mac: S-025
Acceptance:
- [self] While recording, the hook stops matching leaders and sends the key to the recorder, so Hyper+Space and Win chords record instead of starting a sequence or opening Start.
- [self] A leader records a full chord, a step one key with no modifiers; keys are stored in canonical form ("Hyper+Space", "A").
- [snap] A clash with a sibling, the cancel or back key, or a chord Windows reserves shows under the field.
Status: done
Verified: self-test through the hook callback (Hyper+J records as the leader with no sequence started and is written as "Hyper+J", a Win chord records, Ctrl+X on a step and a taken key say why under the field while recording stays on, Esc stops), unit (KeyboardTrackerTests: recording consumes, masks and records; ConfigEditsTests: clashes, cancel and back, Win+L reserved), snapshot bindings-key in light and dark.

### S-041 Edit a binding's title and action
As the user, I change a binding's title or pick a different action from a list.
Screens: bindings
Depends: S-039, S-044
Mac: S-026
Acceptance:
- [self] Changing the title or action rewrites only that entry in the file.
- [unit] The action list holds every defined action, alphabetically (numbers by value, "open-2" before "open-10"), plus "New Action…".
Status: done
Verified: self-test (a new title and a new action each change one line of the file; an empty title isn't saved and says why; New action… makes a Do Nothing action and uses it; undo restores), unit (ConfigEditsTests: the Action list is alphabetical with numbers by value, "open-2" before "open-10", then "New action…"), snapshot bindings in light and dark.

### S-042 Delete an entry
As the user, I press Delete on a selected entry to remove it.
Screens: bindings
Depends: S-038, S-048
Mac: S-027
Acceptance:
- [self] Deleting a menu with children asks first in a dialog that says how many entries will go; Ctrl+Z restores them.
Status: done
Verified: self-test (a menu with entries asks "Delete “Commands”? The 2 entries under it go too." first; a binding goes at once and the next row is selected; Ctrl+Z restores the file), unit (ConfigEditsTests: a menu takes everything under it).

### S-043 Reorder a menu's items
As the user, I drag a binding up or down within its menu, or press Alt+Up and Alt+Down.
Screens: bindings
Depends: S-038, S-048
Mac: S-028
Acceptance:
- [self] The new order is written to the file, and the Guide lists the items in that order.
- [self] An item can't be moved into another menu this way, because that would change its key path.
Status: done
Verified: self-test (Move up writes the new order and the Guide lists it; a drop into another menu snaps back with the file untouched; undo restores), unit (ConfigEditsTests: a menu moves with its children and never leaves its parent). Dragging with the mouse is [manual].

### S-044 Create and edit actions
As the user, I add an action, pick its type, and fill in a form for that type.
Screens: actions, empty-actions
Depends: S-038, S-048
Mac: S-030, S-045
Acceptance:
- [snap] A list of actions, alphabetical, with their type and how many bindings use each, beside a form that changes with the type. The Type list is alphabetical.
- [self] For Use Application, Choose App lists installed apps from Start, packaged ones included, and fills in `exe` or `appId`; Browse picks an exe; either field can be typed.
- [self] Renaming an action id updates every binding that uses it; deleting an action in use is blocked and lists its users.
Status: done
Verified: self-test (the list is alphabetical with type and use count; Choose app lists the apps in Start, packaged ones included, and a packaged app fills appId; Browse fills an exe path; the exe can be typed; renaming updates every binding; deleting an action in use is blocked and names its bindings; undo restores the file), unit (ActionFormsTests: the Type list is alphabetical, every form builds its action back unchanged, the reader's message for a bad form, defaults left out), snapshot actions in light and dark. The file picker itself is [manual]. Place, Move and Resize Window and Context Aware say their form comes later (S-045 builds the window ones).

### S-045 Set up window actions on the Actions page and see them in the Guide
As the user, I pick a window action from the Type list, set its options with pickers instead of typing JSON, and see a glyph for it in the Guide.
Screens: actions-window, guide-window
Depends: S-044, S-022, S-023, S-024, S-025, S-026, S-027, S-028, S-029
Mac: S-070
Acceptance:
- [self] Each window type's form has the Mac's controls: Direction for Focus Window, a Preset list plus four fraction fields for Place Window, Spot or By Points for Move Window, the four ways for Resize Window, a Widths list for grow and shrink, and Press Again to Restore.
- [self] Saving each form writes the fields the spec defines, numbers are clamped as you type, and Ctrl+Z restores the file.
- [snap] Each window form, and the Guide on the seed config's Windows menu with each action's Segoe Fluent Icons glyph, in light and dark.
Status: done
Verified: self-test (Focus Window has a Direction list; Place Window a Preset list and four percent fields, and a preset writes its name; a width over 100 % is clamped to 100 %; Move Window by points writes "by"; Resize Window has Grow through widths, Shrink through widths, To a size and By points, with a Widths list; Maximize has Press again to restore; undo restores the file), unit (ActionFormsTests: every window form builds its action back unchanged, a preset shows its numbers and an edited number makes a custom frame, every window type has a form and a glyph), snapshots actions-window and guide-window in light and dark (the sample config now has a Windows menu with one binding of each window action). The Move form takes X and Y percents for a spot; the Mac's named spots aren't ported.

### S-046 Define contexts
As the user, I create a "browser" context and add Edge and Firefox to it.
Screens: contexts, empty-contexts
Depends: S-044
Mac: S-031
Acceptance:
- [snap] A list of contexts in order, each showing its apps with their icons and names.
- [self] Apps are added with the same Choose App and Browse as Use Application.
- [self] Contexts can be reordered by dragging or Alt+Up and Alt+Down, and the order is saved, because it decides which variant wins.
Status: done
Verified: self-test (Choose app and Browse add apps, saved as exe names and shown with their Start names; an app in Start shows its icon; Alt+Up reorders and the order is saved; undo restores), unit (ConfigEditsTests: contexts added, renamed with their variants, reordered; one a variant uses isn't deleted), snapshot contexts in light and dark. Dragging with the mouse is [manual].

### S-047 Change general settings
As the user, I open Settings and set the app theme, the Guide delay, timeout and position, the cancel and back keys, feedback, the gap between windows, and Run at startup.
Screens: settings
Depends: S-038, S-048
Mac: S-032, S-071
Acceptance:
- [snap] One page of SettingsCards in sections Appearance, Guide, Keys, Feedback, Windows, Startup and About, each card with a short description, in light and dark (DESIGN.md, Settings). No accent picker.
- [unit] A negative delay or gap can't be entered; Gap Between Windows runs 0 to 64 in steps of 2.
- [self] Every change is written to the config file at once, and Ctrl+Z undoes it.
Status: done
Verified: self-test (the sections are Appearance, Guide, Keys, Feedback, Windows and About with no accent picker; theme, delay, position, sound, cancel key and Back off are each written at once; a negative timeout becomes 0 and a gap of 9 becomes 10; a cancel key with modifiers is refused with its reason; Ctrl+Z undoes each change), unit (SettingsValuesTests: no negative delay, gap 0 to 64 in steps of 2), snapshots settings, settings-end and tray-menu (which now has Settings) in light and dark. The Startup card comes with S-054 (M6), which builds Run at startup.

### S-048 Save edits back to the config file
As the user, every change I make in the Bindings window or Settings is written to `config.json` right away, and a change I make in my editor shows up at once.
Depends: S-002
Mac: S-033, S-035
Acceptance:
- [unit] A config loaded and saved with no edits keeps the same entry order, action order and contexts; output is indented with a stable key order, so edits make small diffs.
- [self] The write is atomic (a temp file, then `File.Replace`). A write that would make an invalid config is refused and the file isn't touched.
- [self] A change on disk, including a save through a temp file and rename, reloads within 1 second (a debounced watcher on the folder), and the open window shows it.
Status: done
Verified: unit (ConfigWriterTests: every valid spec fixture and the sample config written back read the same with the same order, the starter is written byte for byte, one title is one line, defaults left out), self-test (a save writes one line and is used, no temp file stays, no reload of its own save, an invalid save is refused with the file untouched, a temp-file-and-rename save reloads within a second and the open Bindings page shows it).

### S-049 Fix validation errors from the Bindings window
As the user, I click an error in the InfoBar and the entry or action it names is selected.
Screens: bindings-error
Depends: S-038
Mac: S-034
Acceptance:
- [self] Clicking an error selects the right row and focuses the field with the problem.
Status: done
Verified: self-test (clicking "Choose an action" selects the new binding and focuses its Action list; clicking an error in the file selects the row it names with the tree focused), snapshot bindings-error. Decided by the agent: while the file itself has errors, editing in the window waits, so an error in the file selects its row but focuses no field, since the fix goes in the file; errors in the window's own changes focus the field.

### S-050 Find my keys and how sequences work on the Help page
As the user, when I'm stuck I open Help (F1) and see my leaders and direct bindings, the keys that work in every sequence, what the words mean, and where to read more.
Screens: help, empty-help
Depends: S-021, S-038
Mac: S-052
Acceptance:
- [self] The page lists every leader and direct binding, grouped as Home groups them, and names your Cancel and Back keys.
- [self] Help in the tray menu and F1 in the Bindings window open the page; clicking a row shows it selected on the Bindings page.
- [snap] Your Keys, In a Sequence, Words, and More (Open config file, the documentation, Report a Problem, the version) in light and dark.
Status: done
Verified: self-test (Help in the tray menu opens the page; it lists every leader and direct binding grouped as Home groups them; F1 is on the Help item; it names the cancel and back keys; clicking a row shows it selected on the Bindings page), snapshots help and empty-help in light and dark. The documentation and Report a problem links point at github.com/juyanith/unnamed-manager-windows, following the Mac app's links; that repo isn't confirmed (this checkout has no remote).

### S-051 Show Help from a key
As the user, I press a chord such as Hyper+H and the Help page opens in front.
Screens: actions
Depends: S-050
Mac: S-053
Acceptance:
- [unit] `{ "type": "help" }` loads as Show Help, takes no other fields, and is written back unchanged.
- [self] The starter config has Show Help on Hyper+H, and Show Help is in the Type list.
- [manual] Press Hyper+H in another app: the Help page comes forward with focus; closing it gives focus back to the app you were in.
Status: done
Verified: unit (ConfigTests: the starter has Show Help on Hyper+H; { "type": "help" } loads as Show Help, takes no other field, is written back unchanged, and is in the Type list), self-test (the starter's Hyper+H and Show Help in the Type list; running it brings the Help page forward). Focus going back to the app you were in on close is [manual].

### S-052 Use the Bindings window from the keyboard
As the user, I move through the tree with the arrow keys, press F2 to edit a title, Ctrl+K to record a key, and right-click or Shift+F10 for the same commands.
Screens: bindings
Depends: S-040, S-042, S-043
Mac: S-043, S-044
Acceptance:
- [manual] Every action in the Bindings window and Settings can be done without a mouse.
- [self] Every command has a keyboard shortcut shown in its tooltip and context menu item, with the same name everywhere.
- [self] Narrator reads each row as "key, title, action".
Status: done
Verified: self-test (on Bindings, Actions, Contexts, Help and Settings every command has a label and a shortcut, and a tooltip of its own names it; every context menu item has a shortcut with the command bar's name and shortcut; Narrator reads a row as "T, Terminal, open-terminal"). Doing everything without a mouse is [manual]. Duplicate (Ctrl+D in DESIGN.md's command map) isn't built.

### S-053 Empty states in the Bindings window
As the user, an empty page tells me what to add and how.
Screens: empty-bindings, empty-actions, empty-contexts, empty-help
Depends: S-038
Mac: S-042
Acceptance:
- [snap] Each empty page shows one line of explanation and a New Binding, New Action or New Context button.
Status: done
Verified: snapshots empty-bindings, empty-actions, empty-contexts and empty-help in light and dark: a glyph, one line, and a New binding, New action or New context button. The pages show a config through the editor's preview, which is never saved.

## M6 System

### S-054 Start at login
As the user, I turn on Run at startup in Settings.
Screens: settings
Depends: S-047
Mac: S-036
Acceptance:
- [self] The switch writes the app's path to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and removes it when off.
- [self] If you turn the app off in Task Manager's Startup apps (`StartupApproved\Run`), the switch shows off, with a line saying where it was turned off.
- [manual] After signing out and in, the tray icon is there and Hyper+Space works.
Status: done
Verified: self-test checks the switch writes the quoted exe path to Run and removes it, shows off with "Turned off in Startup apps…" and an Open startup apps link when StartupApproved's first byte is odd, and that turning it on again clears that (test keys under HKCU\Software\UnnamedManager-Test while APP_STORE is set); unit tests for StartupEntry; snapshot settings-end shows the Startup card in light and dark. A Run value naming an exe that's gone is rewritten at launch. Signing out and in is still to try by hand.

### S-055 Keep the keyboard hook working
As the user, I never lose my leader keys because Windows dropped the hook.
Depends: S-003
Mac: S-037
Acceptance:
- [self] The hook is installed again after the PC wakes from sleep, after the session is unlocked, and when a watchdog sees keys go by (`GetLastInputInfo`) with no hook calls; each time is logged.
- [self] A callback that takes over 50 ms is logged with what it was doing.
Status: done
Verified: self-test sends the hook's guard window a resume from sleep and a session unlock and sees the hook installed again each time; it removes the hook the way Windows does, types F24, and sees the watchdog's test key go missing and the hook come back; a 60 ms callback is logged as "Keyboard hook callback took 71 ms on key 87 down: Passed". Unit tests for HookWatchdog. Differs from the story: GetLastInputInfo counts the mouse too, so input with no hook call sends the mask key as a probe rather than counting as lost; raw input would avoid the probe, but registering it stopped the hook seeing keys (FIXLOG).

### S-056 Know when an app running as administrator blocks me
As the user, the app tells me when an app running as administrator is in front, so I know why the leader doesn't work there or a window won't move.
Screens: tray-menu-warning, guide-error
Depends: S-008, S-022
Mac: S-038
Acceptance:
- [unit] Window actions and Send Shortcut on an elevated window fail with "<App> runs as administrator, so Unnamed Manager can't change it."
- [snap] While an elevated window is in front, the tray menu says "An app running as administrator is in front" with one line on why.
- [manual] With Task Manager in front, find out whether the hook still sees keys, and write the answer in this story.
Status: done
Verified: unit tests (ElevationTests) show Maximize, Minimize and Place on an elevated window fail with "Task Manager runs as administrator, so Unnamed Manager can't change it." and leave it alone, and Send Shortcut says the same; Focus still works, since bringing another window forward is allowed. Self-test: this app's window and the taskbar don't count as elevated, and the menu starts with the two lines. Snapshot tray-menu-warning in light and dark. The tray remembers the app you came from, since clicking the tray puts the taskbar in front. The snapshot now takes only a popup that's new, clears tooltips and waits 1.8 s, because the dark capture caught a gray, undrawn popup. Still to try by hand: with Task Manager in front, whether the hook still sees keys.

### S-057 Stop Hyper from opening Office
As the user, the app tells me that Windows opens Office on Hyper and Hyper+W, T, X and other letters, and turns that off for me with one click.
Screens: tray-menu-warning, welcome
Depends: S-008
Acceptance:
- [unit] A leader or direct binding on a chord Windows gives to Office gets the warning the spec defines (S-001).
- [self] When the `ms-officeapp` handler is the default one, the tray menu offers Turn off Office shortcuts, which sets that handler, for this user only, to do nothing; Turn on Office shortcuts puts it back.
- [manual] After turning them off, Hyper alone and Hyper+W open nothing, and Hyper+W works as a binding.
Status: done
Verified: unit test: a leader on Hyper+W and a direct binding on Hyper+X get office-shortcut warnings, Hyper+Q doesn't (and the spec fixture). Self-test: Turn off Office shortcuts writes HKCU Classes\ms-officeapp\Shell\Open\Command = rundll32 (a test key under APP_STORE), Turn on removes it, and a handler of the user's own is left alone with neither item in the menu. Snapshot tray-menu shows "Turn off Office shortcuts" under Enabled. Still to try by hand: after turning them off, Hyper alone and Hyper+W open nothing.

## M7 Polish

### S-058 Welcome on first start
As the user, on first start I see what the app does, where the config file is, and what to try first.
Screens: welcome
Depends: S-002, S-057
Mac: S-040
Acceptance:
- [snap] A small window with three steps: where the config file is (with Open config file), the Office shortcuts (S-057), and try Hyper+Space. Each step gets a check mark once done. Under the steps, one line on keeping the tray icon on the taskbar (Settings › Personalization › Taskbar › Other system tray icons).
- [self] It shows until you've run one sequence, and Later closes it until the next start.
Status: done
Verified: snapshot welcome in light and dark: a small Mica window, as tall as its content, with the config file (and Open config file), Turn off Office shortcuts, and Try Hyper+Space, then the line on keeping the tray icon on the taskbar with Open taskbar settings. Self-test: it opens at start with all three to do and Later; opening the config file (from Welcome, the tray or Settings) and turning off Office shortcuts each check their step; Later closes it and it shows again at the next start; a real sequence through the hook checks the last step, the button says Done, and it no longer shows at start. Unit tests for WelcomeStatus.

### S-059 App icon and tray icon states
As the user, I can tell at a glance whether the app is ready, off, or needs attention.
Screens: tray-menu, tray-menu-warning, welcome
Mac: S-041
Acceptance:
- [snap] An app icon (multi-size `AppIcon.ico`) and a tray icon from its glyph with ready, off and warning states, each in versions for light and dark taskbars, sharp at 100, 150 and 200 %.
Status: done
Verified: tools/make-icon.ps1 -Review writes the tray review sheet (%TEMP%\UnnamedManager-icon-review-tray.png): the hand-drawn one-color glyph at 16, 20, 24 and 32 px in ready, off and warning on the light and dark taskbar, all sharp. Self-test: all six .ico files load at the four sizes, and WM_SETTINGCHANGE with ImmersiveColorSet redraws the icon in the taskbar's color (SystemUsesLightTheme). The app icon (AppIcon.ico, 16 to 256 px) was done earlier. Still to see by hand: the icon on the real taskbar at 150 and 200 %.

### S-060 See app, file and URL icons in the Guide
As the user, I see each app's icon next to its Use Application binding, and the icon of the file, folder or URL an Open binding opens.
Screens: guide, guide-home
Depends: S-005, S-011, S-021
Mac: S-054, S-055
Acceptance:
- [self] An exe's icon comes from the file, an `appId`'s from its Start entry, a file's or folder's from the shell, a URL's from the app that opens it; nothing found means no icon and no error.
- [self] Icons are loaded when the config loads and cached; opening the Guide never waits on one.
- [self] When any row in a column has an icon, every row in it keeps the same icon space, so titles line up.
- [snap] The Guide and Home show 24-point icons between the keycap and the title, in light and dark.
- [manual] Narrator reads a row the same as before: the icon is decorative.
Status: done
Verified: self-test: notepad.exe's icon from the file, Settings' from its Start entry, win.ini's and the Windows folder's from the shell, https's from the browser; a missing exe, file or scheme is no icon and no error; a config with charmap.exe loads its icon on reload before any Guide shows; Get for an icon not loaded yet returns at once (under 5 ms) and it's there next time; a column with one icon keeps the space on every row. Unit tests for GuideIcons. Snapshots guide, guide-columns and guide-home in light and dark show 24 epx icons between the keycap and the title (Edge for the websites; VS Code and Outlook aren't installed here, so no icon). Icons load on a background STA thread at 24 epx times the system DPI. Narrator: the image is Raw, so the row reads as before; still to hear by hand.

## M8 Spec v15

The window actions the Mac app added from spec v5 to v15. `spec/` moves from `spec-v4` to `spec-v15`, and every fixture for Windows passes. The Mac's S-085 (bindings inside a Settings window) doesn't carry over: the Bindings window already has every page in one window.

### S-061 Fill the screen, with the gap
As the user, I press a key and the window fills its screen, the same gap from the edges as my placed windows; I press it again and it goes back to where it was.
Screens: actions-window, settings
Depends: S-022, S-030
Mac: S-074
Acceptance:
- [unit] `window.fill` reads, writes and plans as Maximize did (spec v7); `window.maximize` still loads, runs as Fill, and is written back under its old name.
- [unit] The frame to fill is the work area, one gap in from every edge; a window within 2 points of it goes back.
- [self] With a gap of 8, Fill stands 8 pixels from every edge, and pressed again restores the first frame exactly, also under the old name.
- [self] The Type list offers Fill once, and not under its old name.
- [snap] The Fill form and the Gap card in Settings say Fill leaves the gap.
Status: done
Verified: unit tests (`FillTests`, `GapTests.FillLeavesTheGapToo`, `BuiltInActionTests.MaximizeStillLoadsAsFill…`, the spec-v7 fixtures); self-test (Fill with a gap of 8 and its restore under both names; the Type list); snapshots actions and settings-windows in light and dark. With a gap of 0, a window that can maximize still uses its own maximize, so Snap and its title bar button stay in step; with a gap, Fill sets the frame and remembers the one before. Not yet tried by hand on real windows.

### S-062 Focus the first, last or a numbered window, with no focused window too
As the user, I press a key and the top left window takes focus, another for the bottom right, and Window 2 is the one beside the first; with the desktop in front, any Focus Window key focuses the window on top.
Screens: actions-focus
Depends: S-028, S-029
Mac: S-072, S-073, S-079
Acceptance:
- [unit] `window.focus` reads `target`, with `first`, `last` and numbers from 1 (spec v6, v11); `direction`, its old name, still loads and is written back as the file had it; both is `conflicting-fields`.
- [self] First and Last go to the ends of the order and stay put on a window that's already there; Window 2 is the middle of three; all three work with no focused window.
- [unit] With no focused window, every other target focuses the window in front of the others (spec v5); with no window at all, "No window to focus."
- [unit] Past the last window: "No window 4. There are 3."
- [snap] Focus Window's form has Focus on, with Window number and a Number box.
Status: done
Verified: unit tests (`WindowTargetTests`, `FocusCycleTests`, `BuiltInActionTests`, the spec-v5, v6 and v11 fixtures); self-test (First, Last and Window 2 with no focused window; the form's Number box and saved number); snapshot actions-focus in light and dark.

### S-063 Keep Focus Window and Swap Window to one display, and hear when there's no window
As the user, I choose On the same display for a Focus Window or Swap Window key, and it never jumps to my other display; when there's no window that way, I hear the alert sound.
Screens: actions-focus, actions-swap, settings
Depends: S-062
Mac: S-078, S-077
Acceptance:
- [unit] `scope` (`all`, `display`) reads and writes on both actions, left out when it's `all`; a bad one is `bad-window-value` (spec v10).
- [unit] Focus right on the same display fails with "No window to the right."; on every display it crosses over; Last on the same display is the rightmost window there.
- [self] Finding no window plays the Default Beep through ActionRunner, with Play a sound on; no focused window fails without it.
- [snap] Both forms have a Windows list; the Play a sound card mentions Focus Window and Swap Window.
Status: done
Verified: unit tests (`WindowTargetTests.OnTheSameDisplayFocusStopsAtItsEdge`, the failures' `Alerts` flag, the spec-v10 fixtures); self-test (a number past the end beeps once, no focused window doesn't); snapshots actions-focus, actions-swap and settings in light and dark. The sound is `MessageBeep(MB_OK)`, as the spec names.

### S-064 Swap the window in front with another
As the user, I press a key and the window I'm in trades places with the one to its left, the next one, the first one, or window number 2, and I stay in it, so pressing again keeps moving it the same way.
Screens: actions-swap, guide-window
Depends: S-062, S-063
Mac: S-077
Acceptance:
- [unit] `window.swap` reads, writes and plans `target` and `scope` (spec v10); the schema part is the spec's and the spec-v10 fixtures pass.
- [unit] A direction picks the window Focus Window would focus; Next and Previous wrap; First, Last and numbers count in Focus Window's order from 1, so 1 is First; a target that is the focused window does nothing and isn't a failure.
- [self] Swap left trades frames with the window on the left and focus stays; Swap right puts them back.
- [unit] A window with a minimum size keeps the edge on the screen's edge.
- [snap] The Swap Window form has Swap with and Windows; the Guide shows its glyphs.
Status: done
Verified: unit tests (`WindowTargetTests` swaps, numbers, minimum size; `ActionFamilyTests` schema); self-test (swap and swap back through ActionRunner); snapshots actions-swap and guide-window in light and dark. Not yet tried by hand on real windows.

### S-065 Arrange the windows used last in a grid, within limits from Settings
As the user, I press a key and the window I'm using and the one I used before it stand side by side in halves, or more of them in columns or rows, with the gap; Settings says how many columns and rows it may take.
Screens: actions-arrange, settings-windows
Depends: S-024, S-030
Mac: S-075, S-076
Acceptance:
- [unit] `window.arrange` reads, writes and plans `count` (2 to 8, or `"all"`), `groups` and `layout` (spec v8); `windows.maxColumns` and `maxRows` are read, written only when they aren't 2, and outside 1 to 8 are `bad-window-limit`.
- [unit] The grid, the order and the spare cells follow the Mac's `ArrangeTests`, ported: the focused window keeps its column or row and takes the spare room there (spec v9); any count in any number of groups fills the work area and stays put when run again.
- [self] Two fake windows end in halves 8 apart, a third window stays, and running it again moves nothing.
- [unit] Windows on another display stay as they are; with no window, "No window to arrange."
- [snap] The form has Windows, Groups and Layout; Settings has Maximum columns and Maximum rows under the gap.
Status: done
Verified: unit tests (`ArrangeTests`, ported case by case from the Mac, and `WindowTargetTests.Arrange…`); self-test (halves with the gap, and again); snapshots actions-arrange and settings-windows in light and dark. Not yet tried by hand on real windows.

### S-066 Bind built-in actions without defining them, and override them
As the user, I write `"action": "window-place-left-half"` or `"window-focus-left"` in a binding and it works with nothing in `actions`; an action of mine with that name runs instead.
Screens: bindings-builtin
Depends: S-061 to S-065
Mac: S-079
Acceptance:
- [unit] Built-in names follow spec v11's rule; names are case-sensitive, numbers have no leading zero, Arrange Windows stops at 8, and `window-maximize` isn't one.
- [unit] An action in `actions` with a built-in name overrides it, with no warning; a name that's neither is `undefined-action`.
- [self] A built-in name stands for its action, and an action with that name overrides it.
Status: done
Verified: unit tests (`BuiltInActionTests`, the spec-v11 fixtures); self-test. Every place that turns a binding into its action (the runner, the Guide, Context Aware, the icons, the validator) goes through `Config.Resolve`.

### S-067 Focus and swap only the windows I can see, in reading order
As the user, I press Focus right or Swap down and get the window I see there, even when it's bigger or smaller than mine or partly behind it, and never one hidden behind other windows; Window 2 in a 3×2 grid is the top middle one.
Screens: settings-windows
Depends: S-062, S-064
Mac: S-082
Acceptance:
- [unit] A window counts only when its visible part holds a 20 by 20 square; the focused window always counts (spec v12).
- [unit] A direction picks among visible parts past the focused window's center: windows in line first, then the smallest gap, the most shared, the nearest center across, the one in front; with none in line, 45° on the centers of those parts.
- [unit] The order is by monitor, then rows of tops within 20, top to bottom, then left to right.
- [unit] `windows.minVisible`, `rowTolerance` and `tieTolerance` default to 20, 20 and 2, are written only when changed, scale with the monitor, and a negative one is `negative-window-distance`.
- [unit] A window hidden behind another is skipped by Next.
- [snap] Settings has a Focus and swap group with Smallest visible part, Same row within and Equally near within.
Status: done
Verified: unit tests (`SeenWindowTests`, ported from the Mac; `WindowTargetTests.AWindowHiddenBehindAnotherIsSkipped`; the spec-v12 fixtures); self-test (Smallest visible part saved from Settings); snapshot settings-windows in light and dark. The distances are effective pixels, turned into physical pixels at the focused window's monitor scale.

### S-068 Focus or swap with the window I used before
As the user, I press one key to go back to the window I used before, and again to come back, even when it's on my other display, on another virtual desktop, or behind the window I'm in; with another key, my window and that one trade places and I stay in mine.
Screens: actions-focus, actions-swap
Depends: S-062, S-064, S-067
Mac: S-083
Acceptance:
- [unit] `"target": "recent"` on both actions, and `window-focus-recent` and `window-swap-recent` (spec v13).
- [unit] The focus history puts a window first each time it gets focus, holds each once, and keeps 10.
- [unit] Recent goes back and forth, also to a covered window; after the previous window closes, it's the one before; with nothing in the history, the window in front after the focused one.
- [self] From a window on another virtual desktop, Focus recent goes back and the desktop switches; again, it comes back.
- [unit] Swap recent stays on this desktop, keeps focus and swaps back.
- [manual] Two real windows on two virtual desktops: Focus recent switches desktops both ways; Alt+Tab and clicks count as focus changes.
Status: done
Verified: unit tests (`WindowTargetTests` history, back and forth, closed window, desktops, swap); self-test against `FakeWindowSystem`, which now has a z-order, desktops and clicks. The history comes from the foreground WinEvent hook (`ForegroundWatcher`), plus what the app's own actions focus. A window on another desktop counts when DWM cloaks it only for that (`DWM_CLOAKED_SHELL`); focusing it is `SetForegroundWindow`, which switches desktops when Windows allows. Not yet tried by hand: the desktop switch on real windows.

### S-069 Choose for each app which window actions take part
As the user, I keep Calculator out of Arrange Windows, so it floats where I put it, while Focus Window and Swap Window still go to it; one list in Settings shows each app's choices.
Screens: settings-end
Depends: S-063, S-065, S-068
Mac: S-084
Acceptance:
- [unit] `windows.apps` maps exe names or AppUserModelIDs to `focus`, `swap` and `arrange`, each true when missing; every app is written with all three; another rule is `unknown-field`, a non-boolean `wrong-type`, a blank app `bad-app-id`; `windows.exclude` is `unknown-field` (spec v15).
- [unit] Focus Window never picks an app with `focus` false; Swap Window never moves one with `swap` false, as the one swapped with or the focused one ("Calculator is left out of Swap Window."); Arrange Windows never counts or moves one with `arrange` false and still arranges its display.
- [self] Swap Window says when the focused window's app is left out.
- [self] Settings adds an app left out of all three, a checked box saves its rule, and undo works.
- [snap] Settings has an Apps group: a card per app with Focus, Swap and Arrange boxes and Remove, then Add an app with Choose app and Browse.
Status: done
Verified: unit tests (`WindowTargetTests` rules, the spec-v15 fixtures); self-test (the swap failure; adding Calculator and checking Focus in Settings, then undo); snapshot settings-end in light and dark. App ids match as contexts do (`FrontApp.Is`): an exe by file name or path, an AppUserModelID exactly.

### S-070 Choose built-in actions in the Bindings window, and see overrides
As the user, I pick Place Window › Left half from a binding's Built in menu and the binding works with nothing to set up; on the Actions page I can tell which of my actions override a built-in one, and deleting an override gives me the built-in one back. A new config starts with a Windows menu of built-in actions.
Screens: bindings-builtin, actions
Depends: S-066
Mac: S-080
Acceptance:
- [self] The Built in menu has Place Window, Focus Window, Swap Window, Arrange Windows and Resize Window with their values, then Fill, Minimize, Show Help and Do Nothing.
- [self] Choosing one saves its name in the binding and nothing in `actions`; the line under the Action list says "Swap Window: Left, built in".
- [self] An action with a built-in name says "override" in the Actions list, and deleting it while bindings use it is allowed and gives them the built-in action back.
- [unit] The listed built-in actions each stand for their own action, none twice (`window-arrange-2` loads but isn't listed).
- [unit] The starter config has a Windows menu of built-in actions and Help as the built-in `help`, and loads with no warnings.
Status: done
Verified: self-test (the menu's groups, choosing one, the override mark, deleting an override in use, undo); unit tests (`BuiltInActionTests`, `ConfigTests.TheStartersWindowsMenu…`); snapshot bindings-builtin in light and dark. The open menu isn't in a snapshot.

### S-071 See every built-in action on the Help page
As the user, I open Help and find the built-in actions, kind by kind, with the name to write in my config next to what each one does, and I can tell which ones my own actions override.
Screens: help-builtin
Depends: S-066, S-070
Mac: S-081
Acceptance:
- [self] Help lists every built-in action in the Built in menu's kinds, with a row for Focus Window's and Swap Window's numbered windows.
- [self] A built-in action the config overrides says "Overridden by your action".
- [snap] Built-in actions sits after In a sequence: a list to open for each kind with its count, then one card for Fill, Minimize, Show Help and Do Nothing; names are monospaced and can be selected.
Status: done
Verified: self-test (the kinds and counts, the first row, an overridden Fill); snapshot help-builtin in light and dark. Which kinds are open is kept while the app runs.

## M9 Spec v20

Held menus, Recent Windows and virtual desktops, which the Mac app added from spec v16 to v20. `spec/` moves from `spec-v15` to `spec-v20`, and every fixture for Windows passes. Spec v16's Quit on close is for the Mac only: Windows apps quit when their last window closes, so `windows.quitOnClose`, `windows.quitOnCloseDelayMs` and the `quit` rule are `other-platform-field` here. The Mac's S-087 (the Spaces settings in macOS) has no Windows counterpart.

### S-072 Switch to the desktop on the left or right
As the user, I press a key and the virtual desktop beside the one I'm on shows at once, with no slide.
Screens: none
Mac: S-088
Acceptance:
- [unit] `space.switch` reads `target` left or right; another value is `bad-window-value`, none `missing-field`. Built-in names `space-switch-left` and `space-switch-right`.
- [unit] With no focused window, at the first or last desktop, or with only one, nothing happens and nothing fails.
- [self] space-switch-right goes to the desktop on the right; Windows lists its desktops and says which is current.
Status: done
Verified: unit tests (SpaceTests) and self-test. Switching uses the shell's own virtual desktop interface, known from Windows 11 24H2 on; without it, the app presses Ctrl+Win and an arrow, which slides. Tried by hand in 0.5.0 (build 26300): space-switch-right switches desktops.

### S-073 Hold a leader and let go to choose, as Alt+Tab does
As the user, I give a leader Hold to choose, hold its modifiers, press its key again to move a highlight, and let go to run the highlighted item. A held leader can be Alt+Tab.
Screens: guide-held, bindings-list
Mac: S-090, S-091
Acceptance:
- [unit] The repeat key, Shift with it, Down and Up move the highlight through Cancel; a binding's key moves it there; a menu's key opens that menu held; a key repeat moves it once; letting go of any of the leader's modifiers chooses, in any order; Cancel and Back work as in the spec; no timeout while held.
- [unit] `hold` only on a leader with modifiers (`hold-not-leader`, `hold-no-modifiers`), and a key under it can't be its menu's repeat key (`step-is-repeat-key`).
- [self] Alt+Tab is taken from Windows, the Guide shows the menu held with Cancel first, Tab moves the highlight, letting go of Alt chooses, and a key-up the hook missed still chooses within 200 ms.
- [snap] The highlighted row is filled with the accent, Cancel has no keycap, and the footer says "Let go to choose".
Status: done
Verified: unit tests (HeldMenuTests, the tracker with Alt+Tab) and self-test through the hook; snapshot guide-held in light and dark.

### S-074 Go back to any recent window with Recent Windows
As the user, I hold Alt+Tab and see the windows I used last, on any desktop and minimized ones too, with the window I'm in as Cancel; I pick one by its number or by letting go.
Screens: guide-recent
Depends: S-073
Mac: S-092, S-093
Acceptance:
- [unit] A `list` reads and writes with its defaults left out, and its rules are checked (`list-has-children`, `unknown-list-type`, `bad-list-value`, `conflicting-fields` with an action, `hidden` is `other-platform-field`).
- [unit] Items are the focus history first, then the other windows front to back, without the focused window and the apps with `"focus": false`, for `count`, `minimized`, `spaces` and `scope`; marked minimized, on another desktop or on another display.
- [unit] Keys pressed before the items come are replayed; pages of 9 with More on 0; the repeat key goes across pages; a window that closes leaves the list and the highlight moves on.
- [self] Choosing an item focuses its window, from minimized or another desktop, and a window that closes while the list is up leaves it.
- [snap] Each item shows its window's title, its app's icon and where it is.
Status: done
Verified: unit tests (ListTests, RecentWindowsTests) and self-test; snapshot guide-recent in light and dark.

### S-075 Turn on Hold to choose and make a menu Recent Windows in the Bindings window
As the user, I select a leader and turn on Hold to choose, or select an empty menu and set its Contents to Recent windows, with its options, and read how held menus work in Help.
Screens: bindings-list, help
Depends: S-073, S-074
Mac: S-094
Acceptance:
- [self] Hold to choose saves `hold` on the leader, and the leader's key is refused as a key under it.
- [self] Contents makes a menu a list, its options save, and Ctrl+Z undoes both.
- [self] Help explains held menus: hold, the repeat key, Shift, letting go and Cancel.
Status: done
Verified: self-test; snapshots bindings-list and help in light and dark.

### S-076 Swap with a window from Recent Windows
As the user, I give a list When chosen Swap, so the window I pick trades places with the one I'm in, and focus stays.
Screens: bindings-list
Depends: S-074
Mac: S-095
Acceptance:
- [unit] `choose` reads and writes with focus left out; `minimized` or `spaces` with swap is `conflicting-fields`; a swap list holds only windows shown on this desktop, without apps with `"swap": false`, and none with no focused window.
- [unit] Choosing trades frames, keeps focus and leaves the focus history as it was; a focused app left out of Swap Window fails with the alert sound.
- [self] A list with When chosen set to Swap trades places with the chosen window, and focus stays.
Status: done
Verified: unit tests and self-test.

### S-077 Send a window to another desktop
As the user, I press a key and the window I'm in goes to the desktop on the left or right, or one by number, and I stay, or with Go with the window, go with it. (Spec v23 moves going with it to Follow to Desktop, S-113.)
Screens: actions
Depends: S-072
Mac: S-096
Acceptance:
- [unit] `space.send` reads `target` left, right or a number from 1, and `follow`; built-in names `space-send-left`, `space-send-right` and `space-send-2` and on.
- [unit] Without follow, focus goes to the window used before on this desktop, or none; with follow, the desktop goes there and the window keeps focus. Already there, or at the end, does nothing; a number past the last desktop, a window on every desktop, a full-screen one, or Windows refusing fails with the alert sound.
- [self] space-send-3 puts the window on desktop 3 and focuses the window used before; a desktop past the last says so with the alert sound.
Status: done
Verified: unit tests (SpaceTests) and self-test on fake windows. On real windows it uses the shell's own interface, known from Windows 11 24H2 on; the self-test checks that Windows lists its desktops, and in 0.5.0 (build 26300) space-send-2 moved a real window, tried by hand.

## M10 The WPF app catches up

M10 to M14 bring Windows to parity with the Mac app 0.5.3 (`a157cb0`), from a comparison of the Mac and WinUI apps' code on 2026-11-21, and of the WinUI and WPF apps' after it. WPF is the app for PCs that block the WinUI build: a Guide, a tray menu and the config file. After M10 it behaves as 0.5.1 does, has checks of its own, and can ship. M10 comes first, so the shared and Guide changes after it land in both apps once.

What's wrong on `wpf-app` (`49dd661`), all found by reading its code:
- It's 13 commits behind `main` and has never run in CI. A trial merge with `234a4b5` was clean, built, and passed `--check`.
- `App.xaml.cs` is a copy from before 0.5.1, with no `CloseEndedSequence`: reloading the config mid-sequence leaves the Guide up. Merging `main` doesn't fix a copy.
- Cancel's glyph (U+E711) in held menus was lost in the port.
- Both apps use the Run value `UnnamedManager`, so each shows Run at startup as on when the value starts the other.
- A sequence run in WPF never sets `Prefs.RanSequence`.
- Releases build only the WinUI app.

Gotchas to respect throughout:
- A linked file stays free of WinUI.
- A fix to the WinUI `App`, Guide or tray menu isn't in WPF until its copy has it too.
- On purpose, WPF has a solid Guide, opens the tray menu on a left click, and opens the README for Help.

### S-105 Build the WPF app from main
As someone on a PC that blocks the WinUI build, I build the WPF app from `main` and get the shared code of the current version.
Screens: none (build)
Depends: S-010
Apps: WPF
Mac: none
Acceptance:
- [unit] `wpf-app` is merged up to `main` and then into it through a PR, with CI building both apps and running WPF's `--check` on x64 and Arm64.
- [unit] Both apps get their version from one place, so WPF says 0.5.1 or later and never falls behind.
- [self] The WinUI self-test still passes with the icon and layout files that moved for sharing.
Status: done
Verified: merged locally (parity-build); tools/ci.ps1 passes on Arm64 and emulated x64, with both apps at 0.5.1 from Directory.Build.props. GitHub's CI passed on parity-build (run 45046652039, x64 and Arm64). Merged into main through PR 7, with CI on both apps.

### S-106 Check the WPF app's behavior
As the maintainer, I run one command and see that WPF's keys, Guide, tray and actions work, as I do for WinUI.
Screens: wpf-guide, wpf-guide-held, wpf-guide-home, wpf-guide-error, wpf-tray
Depends: S-105
Apps: WPF
Mac: none
Acceptance:
- [self] A WPF self-test on a `%TEMP%` config runs a sequence, a held menu, a list on fake windows, a failure and a reload, and exits 1 when one fails. It refuses a config outside `%TEMP%`.
- [self] CI runs it on x64 and Arm64.
- [snap] It saves each planned WPF page in light and dark.
Status: done
Verified: tools/dev.ps1 selftest -App wpf passes on Arm64 and as emulated x64, and the five pages were looked at in light and dark. GitHub's CI ran it on x64 and Arm64 (run 45046652039).

### S-107 Close the WPF Guide when a reload ends its sequence
As the user, I save the config while the Guide is up and it closes, so it never shows a sequence that has ended.
Screens: none (no new UI)
Depends: S-106
Apps: WPF
Mac: S-025
Acceptance:
- [self] A reload during a sequence hides the Guide and drops a waiting delay or timeout, also for keys still queued at the swap.
- [self] A sequence started after the reload keeps its Guide, and a failure's message still closes by itself.
Status: done
Verified: the WPF self-test reproduced the stuck Guide before the fix and passes after it.

### S-108 See Cancel's glyph in WPF held menus
As the user, I see the same Cancel row in a held menu in both apps.
Screens: wpf-guide-held
Depends: S-106
Apps: WPF
Mac: none
Acceptance:
- [self] Cancel has U+E711, except in a held list with a focused window, where it has that app's icon.
- [snap] The glyph is in the icon space and the titles line up, in light and dark.
Status: done
Verified: the WPF self-test failed before the fix and passes after it; wpf-guide-held in light and dark matches WinUI's guide-held, with row glyphs now the same 20 epx.

### S-109 Know which app runs at startup
As the user with both apps on a PC, I see Run at startup as on only in the one Windows starts.
Screens: settings, wpf-tray
Depends: S-054, S-106
Apps: WinUI, WPF (shared code)
Mac: none
Acceptance:
- [unit] The Run value is this app's when its exe has this app's file name, so a dev build still shows the installed copy's switch as on.
- [self] With the other app's exe in the value, the switch is off. Turning it on replaces the value, and nothing else changes or removes it.
- [unit] The repair at launch never takes over a value that names the other app.
Status: done
Verified: unit tests, and both self-tests on the test's own registry keys. WPF's failed three checks before the fix.

### S-110 Finish Welcome by running a sequence in WPF
As the user who started on WPF, I open the WinUI app later and don't get Welcome again.
Screens: none (no new UI)
Depends: S-058, S-106
Apps: WPF
Mac: none
Acceptance:
- [self] The first binding a sequence runs saves `RanSequence`, as in WinUI. A canceled sequence doesn't.
Status: done
Verified: the WPF self-test failed before the fix and passes after it.

### S-111 Download the WPF app from a release
As someone who can't build the app, I download the WPF version from the same release as the WinUI one.
Screens: none (release)
Depends: S-106 to S-110
Apps: WPF
Mac: none
Acceptance:
- [self] A version tag also publishes `UnnamedManager.Wpf-<version>-win-x64.zip` and `-win-arm64.zip`, each after WPF's self-test passed on the published exe.
- [manual] Proposed: the zips are self-contained, as WinUI's are. One starts on a PC with no .NET installed.
- [manual] The release notes and README say which zip to take, and that only one of the apps runs at a time.
Status: done
Verified: tools/publish.ps1 made all four zips here, each published exe passed its self-test (x64 under emulation), and the extracted x64 WPF zip passed on a PC with no x64 .NET. The v0.6.0-rc.1 tag published all four (run 45047284411), each after its self-test, with notes that say which to take and that only one runs at a time.

## M11 Parity: reach the right windows

After M11 you can live on several virtual desktops, and Recent Windows lists exactly the windows you can go to.

What's wrong in 0.5.1:
- A suspended Store app's window is shell-cloaked just like a window on another desktop, and `Listable` and `Find` take it for one. On a PC with one desktop, Recent Windows listed two windows "on another desktop" (seen 2026-11-21).
- `WindowPicker` skips every cloaked window, so Use Application starts a second copy of an app that's on another desktop.
- Owned windows are never candidates, so an app's settings dialog can't be focused or listed. The Mac takes dialogs since 0.5.2 (`658638a`, `90e3cde`), but the spec still says candidates are standard windows.
- `AppSwitcher.Focus`'s result is ignored.
- A list with `"scope": "display"` and no focused window lists every display's windows (reproduced with fake windows).
- Starting the key recorder resets the engine and leaves the Guide up (reproduced).

Gotchas to respect throughout:
- Cloaked by the shell doesn't mean "on another desktop". It takes `IVirtualDesktopManager` saying so too, and "Windows doesn't say" isn't a yes.
- The fake window system can't show any of this by itself: give it suspended, owned and unfocusable windows first.
- Owned windows also include tooltips, menus and palettes. Those never become candidates.

### S-078 See only real windows in Recent Windows and Window used before
As the user, I open Recent Windows and every item is a window I can go to, with "on another desktop" only on the ones that are.
Screens: guide-recent
Depends: S-068, S-074, S-106
Apps: WinUI, WPF (shared code)
Mac: S-083, S-092
Acceptance:
- [unit] A shell-cloaked window counts only when Windows says it's on another desktop. One that's cloaked on this desktop, or whose desktop Windows doesn't give, is no item and is never the window used before.
- [unit] A window on another desktop is still listed and marked, and Focus Window's `recent` still goes to it.
- [self] With one virtual desktop, no window the real window system lists is marked on another desktop.
- [manual] Open Settings or Calculator, close it, and open Recent Windows: it isn't listed. With a window on a second desktop, that one is, and choosing it goes there.
Status: done
Verified: unit tests on fake windows, and both self-tests on the real window list: WPF's found 2 phantom windows on this one-desktop PC before the fix and none after. And by hand on 2026-11-22, the [manual] line passed in both apps.

### S-079 Close the Guide when I start recording a key
As the user, I click a key field while a sequence is up and the Guide closes, so it never shows a sequence that has ended.
Screens: none (no new UI)
Depends: S-040
Apps: WinUI
Mac: S-025
Acceptance:
- [self] With the Guide up, starting the recorder on the Bindings page or on a Settings key card closes it, and a Guide waiting for its delay never appears.
- [self] A key still queued when the recorder starts leaves no Guide behind, as after a reload.
Status: done
Verified: the self-test reproduced the stuck Guide on both pages before the fix and passes after it.

### S-080 Switch to an app on another virtual desktop
As the user, I press an app's key and land in the window of it I used last, whichever desktop that's on, and no second copy starts.
Screens: none (no new UI)
Depends: S-078, S-106
Apps: WinUI, WPF (shared code)
Mac: S-006, S-047
Acceptance:
- [unit] The app's window used last wins, on this desktop or another; a minimized one only when it has no other; a suspended one never.
- [self] Focus or start, and Only focus, both focus an app whose only window is on another desktop; neither starts it or says it isn't running.
- [self] Always start a new one still starts one, and waits for a window that wasn't there before.
- [manual] With File Explorer only on desktop 2, its key from desktop 1 goes to desktop 2 and opens no window.
Status: done
Verified: unit tests, and both self-tests with a window that pretends to be on another desktop. And by hand on 2026-11-22, the [manual] line passed in both apps.

### S-081 Settle dialogs in the shared spec
As the maintainer, I write in the spec repo which dialogs are candidates and for which actions, so the rule the Mac app already follows is one both apps share.
Screens: none (no UI)
Apps: WinUI, WPF (shared code)
Mac: S-068, S-077, S-092
Acceptance:
- [unit] `CONFIG-SPEC.md` says dialogs are candidates for Focus Window and Recent Windows, and never for Swap Window, a list that swaps, or Arrange Windows, with what happens when the focused window is a dialog.
- [unit] It defines a dialog on Windows. Proposed: an owned top-level window that is visible, enabled, has a caption and isn't a tool window, with an owner that a modal dialog disabled standing aside for that dialog.
- [unit] The spec repo tags the change (the next free number; v21 is only proposed, for modifiers on step keys), `spec/` is pinned to it, `TheSpecIsPinnedToV20` moves with it, and every Windows fixture passes.
Status: done
Verified: spec-v21 is committed and tagged, `spec/` is pinned to it, and every Windows fixture passes. The proposal for modifiers on step keys moves to v22. The maintainer approved the wording on 2026-11-22. Its tag is on GitHub, and CI fetched it there (run 45046652039). The spec repo's main doesn't have it yet.

### S-082 Focus an app's dialog
As the user, I reach an app's settings dialog with Focus Window, Recent Windows or the app's own key, as I reach any window.
Screens: guide-recent
Depends: S-078, S-080, S-081, S-106
Apps: WinUI, WPF (shared code)
Mac: S-068, S-083, S-092
Acceptance:
- [unit] A dialog, as the spec defines it, is a candidate for every Focus Window target and an item in Recent Windows, and goes into the focus history when it takes focus.
- [unit] Tooltips, menus, tool windows and owned windows with no caption are never candidates or items.
- [self] Against the app's own windows, a captioned owned window counts and an owned popup with no caption doesn't.
- [self] Use Application on an app with a modal dialog up gives the dialog focus, not the disabled window behind it.
- [manual] Open Notepad's Page setup or another app's options dialog, go to another app, and come back to the dialog with Recent Windows and with Focus Window.
Status: done
Verified: unit tests on fake windows, and both self-tests on real windows of their own (a dialog, a popup, a tool window, a disabled owner). And by hand on 2026-11-22, the [manual] line passed in both apps.

### S-083 Keep dialogs out of Swap Window and Arrange Windows
As the user, I swap and arrange my windows and a dialog stays the size and place its app gave it.
Screens: none (no new UI)
Depends: S-081, S-082
Apps: WinUI, WPF (shared code)
Mac: S-075, S-077, S-095
Acceptance:
- [unit] No Swap Window target and no list that swaps picks a dialog.
- [unit] With a dialog focused, Swap Window and a list that swaps move nothing and fail with "A dialog is left out of Swap Window." and the alert sound.
- [unit] Arrange Windows never counts or moves a dialog. With one focused, its display is arranged and the window in front there counts as focused.
- [unit] Place, Move and Resize Window still work on a focused dialog.
Status: done
Verified: unit tests for every Swap target, a list that swaps, Arrange Windows and Place on a focused dialog.

### S-084 See when a window couldn't take focus
As the user, when Windows won't bring a window forward, the Guide tells me, so I don't type into the wrong one.
Screens: guide-error
Depends: S-016, S-106
Apps: WinUI, WPF (shared code)
Mac: S-068, S-092
Acceptance:
- [unit] When focus doesn't arrive, Focus Window and a chosen list item fail with "Couldn't focus <App>.", and the focus history doesn't change.
- [self] Use Application says the same, and the failure is in the log.
- [self] A window that takes the foreground up to half a second late, as on a desktop switch, isn't a failure.
Status: done
Verified: unit tests on fake windows, and both self-tests with focus that never arrives and focus 450 ms late.

### S-085 Keep Recent Windows to this display with nothing focused
As the user, with the desktop in front, a list set to This display shows only the windows on the display of the window on top.
Screens: none (no new UI)
Depends: S-074
Apps: WinUI, WPF (shared code)
Mac: S-092
Acceptance:
- [unit] With no focused window, `"scope": "display"` uses the display of the candidate in front of the others, as the spec says for Focus Window.
- [unit] With no candidate at all, the list is empty and nothing fails.
Status: done
Verified: unit tests on two fake displays, which failed before the fix and pass after it.

## M12 Parity: edit everything in the window

After M12 nothing the Mac app can edit needs the config file in the WinUI app. WPF has no editor (Later); only the Guide icons here (S-093, S-094) reach it. The small commands come first, then icons, then Context Aware, the largest.

What's missing in 0.5.1: `icon` is checked and written back but never drawn or editable; Context Aware has no form; Duplicate is in DESIGN.md's command map but isn't built; Sort, named spots and a few of the Mac editor's explanations are absent.

Gotchas to respect throughout:
- A form never writes back what it only displayed (FIXLOG, 0.5.1). Each new field gets a check that tabbing through changes nothing.
- A key the app picks passes the same rules as one you record: siblings, cancel and back, a held menu's repeat key.
- The TreeView raises selection events late. Show from the selection, and note the row shown before touching a control.

### S-086 See whether an entry is a leader, a menu or a direct binding
As the user, I select a row and the line under its key says what that key is.
Screens: bindings, bindings-direct
Depends: S-041
Apps: WinUI
Mac: S-024, S-048
Acceptance:
- [snap] A leader says "A leader is a chord that opens its menu from anywhere."
- [snap] A direct binding says "A direct binding is a chord that runs its action from anywhere, with no menu."
- [snap] An entry inside a menu says "A single key, pressed after the keys above it."
Status: done
Verified: unit test, and bindings, bindings-direct and bindings-list looked at in light and dark.

### S-087 Duplicate an entry
As the user, I press Ctrl+D on an entry to get a copy of it, with everything under it, next to the original.
Screens: bindings
Depends: S-039, S-052
Apps: WinUI
Mac: S-043
Acceptance:
- [unit] The copy and everything under it go right after the original's last entry, in the same order, with the copy's title ending " copy" and its children's titles, actions, icons, `hold` and `list` unchanged.
- [unit] The copy's key is free and valid: in a held menu it's neither that menu's repeat key nor the key of one of the copy's own children, and a list there gets no digit.
- [self] Duplicate is in the context menu and the overflow with Ctrl+D, selects the copy, and Ctrl+Z restores the file exactly.
Status: done
Verified: unit tests for order, kept fields and free keys in held menus, and the self-test for the command, its shortcut and undo.

### S-088 Sort a menu alphabetically
As the user, I choose Sort alphabetically on a menu and its keys are in order in the window and in the Guide.
Screens: bindings
Depends: S-043
Apps: WinUI
Mac: S-029
Acceptance:
- [unit] Children sort in three groups: named keys by name with numbers by value (F2 before F10), then digits, then letters. What's under each child moves with it.
- [self] The command is in a menu's context menu and the overflow with a shortcut, is off for a binding or a list, writes the new order, and Ctrl+Z restores it.
Status: done
Verified: unit test for the order, and the self-test for the command, its shortcut (Ctrl+Shift+L), when it's on, and undo.

### S-089 Move a window to a named spot
As the user, I pick Center or Top left for Move Window instead of working out percentages.
Screens: actions-move
Depends: S-045
Apps: WinUI
Mac: S-070
Acceptance:
- [unit] Spot has the nine spots (Top left, Top, Top right, Left, Center, Right, Bottom left, Bottom, Bottom right) and Custom. A spot fills both percentages, and percentages that match a spot show its name.
- [unit] Editing a percentage shows Custom and keeps the other number exact.
- [snap] The Move Window form with Center chosen, in light and dark.
Status: done
Verified: unit tests for all nine spots and Custom, and actions-move looked at in light and dark.

### S-090 Go straight to a new action's form
As the user, I choose New action… for a binding and land on that action, ready to set it up.
Screens: actions
Depends: S-041, S-044
Apps: WinUI
Mac: S-026
Acceptance:
- [self] New action… creates the action, gives it to the binding, and opens the Actions page with it selected and its Id focused.
- [self] One Ctrl+Z takes out both the action and the binding's use of it.
Status: done
Verified: the self-test for the page, the focused Id and the one undo.

### S-091 Fix a repeat-key clash from its error
As the user, I click Change the key on a repeat-key error and press the new key.
Screens: bindings-repeat
Depends: S-049, S-075
Apps: WinUI
Mac: S-094
Acceptance:
- [self] Turning on Hold to choose over a clashing key leaves the file untouched and shows the error with Change the key.
- [self] Change the key selects that row with its recorder on; the recorded key clears the error and saves both changes.
- [snap] The InfoBar with Change the key, in light and dark.
Status: done
Verified: the self-test for the untouched file, the recorder and the save, and bindings-repeat looked at in light and dark.

### S-092 See why a menu can't be Recent windows
As the user, I see Contents on every menu, and on one with keys under it I read what to remove first.
Screens: bindings-contents
Depends: S-075
Apps: WinUI
Mac: S-094
Acceptance:
- [self] On a menu with keys under it, Contents shows Keys you add, and Recent windows can't be chosen.
- [snap] Under it: "Remove the keys under this menu to make it Recent windows."
- [self] Once the last key is removed, Recent windows can be chosen.
Status: done
Verified: the self-test, and bindings-contents looked at in light and dark.

### S-093 See an entry's own icon in the Guide
As the user, the `icon` I gave an entry shows next to it in the Guide and Home.
Screens: guide-icons, guide-home
Depends: S-060, S-106
Apps: WinUI, WPF (own copies)
Mac: S-056
Acceptance:
- [unit] An entry's `icon` wins over its action's icon and glyph: `U+` and 4 to 6 hex digits is a Segoe Fluent Icons glyph, anything else a file, with a leading `~` expanded.
- [self] A code point the font doesn't have, or a file that's gone, shows no icon and no error. Icons load with the config, never when the Guide shows.
- [snap] A glyph is in secondary text color, centered in the 24 epx icon space, and in the highlight's text color on a highlighted row.
- [manual] Narrator reads the row the same as before.
Status: done
Verified: unit tests, both self-tests, and guide-icons and guide-held looked at in light and dark in both apps. And by hand on 2026-11-22, the [manual] line passed with Narrator.

### S-094 Tell menus apart by the app's glyph
As the user, a menu with no icon of its own shows the app's two keycaps in the Guide, so menus stand out from bindings.
Screens: guide, guide-home
Depends: S-093, S-106
Apps: WinUI, WPF (own copies)
Mac: S-054, S-056
Acceptance:
- [unit] A menu or list with no `icon` gets the app's glyph; a binding never does.
- [snap] The glyph is one color, 16 epx in the 24 epx icon space, in secondary text color, and titles still line up.
Status: done
Verified: unit test, and guide-icons and Home looked at in light and dark in both apps.

### S-095 Choose an icon for an entry
As the user, I give a menu or binding an icon in the Bindings window and see it before I use it.
Screens: bindings-icon
Depends: S-093
Apps: WinUI
Mac: S-056
Acceptance:
- [self] The Icon field takes a code point or a path, and a menu beside it offers common glyphs, Browse and Automatic. Each choice saves and undoes, and Automatic leaves `icon` out of the file.
- [self] A bad value says why under the field and isn't saved. A glyph or file that isn't there says so.
- [self] Tabbing through the field changes nothing.
- [snap] The field with its preview, and its help, in light and dark.
- [manual] Narrator names the preview by its code point or file name.
Status: done
Verified: the self-test for saving, undo, the errors and the menu, and bindings-icon looked at in light and dark. And by hand on 2026-11-22, the [manual] line passed with Narrator.

### S-096 Set what a Context Aware action does in each context
As the user, I make a Context Aware action and choose what it does in each of my contexts, without editing JSON.
Screens: actions-contextual
Depends: S-044, S-046
Apps: WinUI
Mac: S-030
Acceptance:
- [unit] The Type list has Context Aware in alphabetical place. Inside a variant, the list has every type but Context Aware.
- [self] There's a switch for each context, in Contexts order. On shows a Type list and that type's whole form, with Choose app and Browse for Use Application; off removes the variant, and Ctrl+Z restores it.
- [self] A variant that isn't valid yet says why in its own section, and the file isn't touched until it is.
- [self] Editing one variant leaves the others, and the fallback, exactly as they were. Tabbing through every variant changes nothing.
- [self] Changing the type away from Context Aware is one step that Ctrl+Z restores in full. This replaces the read-only note from 0.5.1.
- [snap] Two contexts, one with a variant, and the line "The first context, in Contexts order, that has the app in front wins.", in light and dark. With no contexts it says to add one on the Contexts page first.
- [manual] The whole form works with Tab and the arrow keys, and Narrator says which context each field belongs to.
Status: done
Verified: unit tests, the self-test for every [self] line and the fields' spoken names, and actions-contextual looked at in light and dark. By hand on 2026-11-22, the keyboard part of the [manual] line passed. And by hand on 2026-11-22, the [manual] line passed with Narrator.

### S-097 Set what a Context Aware action does everywhere else
As the user, I turn on Otherwise, do something else and choose the action for every other app.
Screens: actions-contextual-fallback
Depends: S-096
Apps: WinUI
Mac: S-030
Acceptance:
- [self] On shows the same Type list and forms as a variant and writes `fallback`; off removes it, and Ctrl+Z restores it.
- [self] With a fallback the binding is available in an app outside every context; without one it's dimmed in the Guide.
- [snap] The fallback section, in light and dark.
Status: done
Verified: the self-test for saving, undo and availability, and actions-contextual-fallback looked at in light and dark.

## M13 Parity: the Guide

After M13 both apps' Guides lay out, read aloud and move as the Mac's does. WPF has its own Guide, so each story here is built twice.

What differs in 0.5.1: Home gives every group its own column (eight groups made eight), Narrator doesn't count in held menus, and `Motion.GuideFade` is unused.

### S-098 Read a big Home in a few columns
As the user, with many groups of chords I still see all of Home, my current group first.
Screens: guide-home-wide
Depends: S-021, S-106
Apps: WinUI, WPF (own copies)
Mac: S-050
Acceptance:
- [unit] With more than 12 rows, the current group has the first column. The others are packed in order into columns of up to 12 rows, and a group is never split.
- [snap] A vertical divider follows the first column, in light and dark.
- [self] With eight groups, every row is inside the Guide at its width limit of 80 % of the work area.
Status: done
Verified: unit tests, both self-tests with eight groups of long titles, and guide-home-wide looked at in light and dark in both apps.

### S-099 Hear where the highlight is
As the user of Narrator, I hear which item is highlighted and how many there are.
Screens: none (no visual change)
Depends: S-073, S-074, S-106
Apps: WinUI, WPF (own copies)
Mac: S-091, S-093
Acceptance:
- [unit] Opening a held menu says "Apps, Terminal, 1 of 4"; a move says only "Browser, 2 of 4". Cancel isn't counted and says "Cancel", or "Cancel, <title>" in a list.
- [unit] A list counts across its pages: "Window 10, 10 of 20".
- [manual] With Narrator on, each move reads once and Narrator's focus stays in the app in front.
Status: done
Verified: unit tests for every phrase, both self-tests for a peer to announce through, and a UI Automation listener that received both apps' announcements during their self-tests. And by hand on 2026-11-22, the [manual] line passed with Narrator.

### S-100 See that a list is empty
As the user, I open Recent Windows with nothing to list and the Guide says so.
Screens: guide-recent-empty
Depends: S-074, S-106
Apps: WinUI, WPF (own copies)
Mac: S-092
Acceptance:
- [snap] A list that isn't held says "No recent windows" in secondary text, not as a group header.
- [unit] Narrator hears "<menu>, No recent windows" instead of "0 keys". A held list with no items still shows only Cancel.
Status: done
Verified: unit tests, both self-tests, and guide-recent-empty looked at in light and dark in both apps.

### S-101 See that More opens another page
As the user, I see a chevron on More, so I know 0 shows more windows and doesn't pick one.
Screens: guide-recent-more
Depends: S-074, S-106
Apps: WinUI, WPF (own copies)
Mac: S-093
Acceptance:
- [snap] More has the › that menus have, and the path shows "Page 1 of 3".
- [unit] Narrator reads More as a menu, and the highlight still never rests on it.
Status: done
Verified: unit test for the highlight, both self-tests for the row, and guide-recent-more looked at in light and dark in both apps.

### S-102 See the Guide fade in and out
As the user, the Guide comes and goes with a short fade, and at once with animation effects off.
Screens: none (motion)
Depends: S-005, S-106
Apps: WinUI, WPF (own copies)
Mac: S-005
Acceptance:
- [self] Showing and hiding take `Motion.GuideFade`, and no time with animation effects off. A menu change is instant.
- [self] The hide finishes by the clock, not the animation, and a Guide shown again during a fade out stays up.
- [self] The fade never changes the foreground window, and a chosen binding runs without waiting for it.
Status: done
Verified: both self-tests, and the Guide's acrylic looked at in light and dark with the fade's layered window.

## M14 Parity: check it

After M14 parity is something a person has seen, and the docs say what each app does.

### S-103 Read what the app does in the README
As someone new to the app, I read the README and learn every feature it has.
Screens: none (docs)
Depends: S-078 to S-102, S-105 to S-111
Apps: WinUI, WPF
Mac: none
Acceptance:
- [manual] The README says what WPF has and leaves out, with how to build, check and download it.
- [manual] The README's features cover built-in actions, Fill, Swap Window, Arrange Windows, held menus, Recent Windows, virtual desktops, icons and Context Aware editing.
- [manual] BRIEF.md no longer lists virtual desktops as out of scope, and CLAUDE.md and ARCHITECTURE.md match what's built.
- [manual] The Help page's words and held menu help match the same list.
Status: done
Verified: README, BRIEF, ARCHITECTURE, CLAUDE and Help were read against what's built in M10 to M13.

### S-104 Use every Mac feature by hand on Windows
As the user, I use the app for a day on real windows and nothing the Mac app does is missing.
Screens: none (by hand)
Depends: S-078 to S-103, S-105 to S-111
Apps: WinUI, WPF
Mac: every story not under Dropped
Acceptance:
- [manual] On two virtual desktops: Use Application, Window used before, Recent Windows, Switch Desktop and Send to Desktop, with and without Go with the window.
- [manual] On two monitors with different scales: Place, Arrange, Swap, This display, and the Guide's position.
- [manual] Alt+Tab as a held leader: a quick tap goes back, holding and tapping moves along, Esc cancels.
- [manual] With an app running as administrator in front, each failure says why.
- [manual] The Bindings window and Settings with the keyboard alone, then with Narrator on, and the Guide with Narrator on.
- [manual] The same day in WPF, editing the config file, for everything but the Bindings window, Settings and Welcome.
- [manual] Each Mac story from the version then current is either done here or under Dropped with a reason; anything newer than 0.5.3 becomes a new story.
Status: done
Verified: a day of use by hand on 0.6.0-rc.2 (3da9946), in WinUI and WPF, reported done on 2026-11-24.

## M15 Spec v22

Spec v22 lets Switch Desktop go to a desktop by number, as Send to Desktop with Go with the window already could.

### S-112 Switch to a desktop by number
As the user, I press a key and go straight to desktop 1, or any desktop by its number, even from an empty desktop.
Screens: actions, help
Depends: S-072, S-077
Apps: WinUI, WPF
Mac: none yet (spec v22 is newer than 0.5.3)
Acceptance:
- [unit] `space.switch` reads `target` left, right or a number from 1; built-in names `space-switch-1` and on; `0`, `"2"` and `space-switch-0` are errors. `spec/` is pinned to `spec-v22` and its fixtures pass.
- [unit] A number switches with no focused window; the desktop you're on does nothing; one past the last fails with the alert sound. Left and right are unchanged.
- [unit] The Switch Desktop form has Desktop number, as Send to Desktop's does, and builds what the file has.
- [self] space-switch-1 goes to desktop 1 from an empty desktop; space-switch-4 with three desktops says so with the alert sound.
- [snap] Help lists Desktop 1, 2, 3… under Switch Desktop.
- [manual] On real desktops, a key bound to space-switch-1 goes to desktop 1 in both apps.
Status: done
Verified: unit tests (SpaceTests, the v22 fixtures), WinUI self-test 342 and WPF 69 checks, which share Core's Switch Desktop. Help's built-in list is below the snapshot's fold; the self-test counts its three Switch Desktop rows. space-switch-1 on real desktops in both apps, tried by hand on 0.6.0.

## M16 Spec v23

Spec v23 makes going with a sent window its own action, Follow to Desktop, in place of Send to Desktop's `follow`.

### S-113 Follow a window to another desktop
As the user, I press a key and the window I'm in goes to another desktop, and I go there with it; Send to Desktop always leaves me where I am.
Screens: actions, help
Depends: S-077, S-112
Apps: WinUI, WPF
Mac: Follow to Space (0.6.0, spec v23)
Acceptance:
- [unit] `space.follow` reads `target` left, right or a number from 1; built-in names `space-follow-left`, `space-follow-right` and `space-follow-1` and on; `0`, `"2"` and `space-follow-0` are errors. `spec/` is pinned to `spec-v23` and its fixtures pass.
- [unit] `space.send` with `follow`, true or false, is `unknown-field`, and the message says to use Follow to Desktop.
- [unit] Follow to Desktop moves the window, goes to its desktop, and the window keeps focus; already there or at the end does nothing, with no sound; a number past the last fails with the alert sound.
- [unit] Send to Desktop's form has no Go with the window; Follow to Desktop has its own form with Go to and Desktop number, and both build what the file has.
- [self] space-follow-2 puts the window on desktop 2 and goes there with it. The Built in menu and Help list Follow to Desktop, with Desktop 1, 2, 3….
- [manual] On real desktops, a key bound to space-follow-2 takes the window to desktop 2 and you with it, in both apps.
Status: doing
Left: the check by hand on real desktops.

## Later
- An editor, Settings and Welcome for the WPF app. Until then you edit the config file there, and S-079 and the editor stories in M12 are WinUI only.
- Make the highlighted row's keycap readable in dark mode: in both apps its letter is white on the light accent (seen in `guide-held`).
- Keep the self-test's timing checks from failing CI and releases on a slow runner: the key recorder's check waits a fixed 200 ms for each key (it failed x64 CI on main at `21ccab1`), as many others do, and a few have hard limits (100 ms to switch apps, 5 ms to open the Guide without its icon, 1 ms in the hook, a second to reload).
- Make `tools/publish.ps1 -Aot` work again: `VirtualDesktops` uses `[ComImport]`, which NativeAOT doesn't support and the trim analyzer flags.
- Import and export one menu as a JSON snippet.
- A basic automatic tiling mode.
- Move a window to another monitor; save and restore window layouts.
- A list of desktops, and switching to the desktop of the window used before (spec Planned).
- New action families on the registry: volume, display resolution.
- Double-tap modifier leaders.
- A command line or MCP server so agents can change bindings (`agent-cli`).

## Dropped
The brief's main jobs map to M1 and S-019 (job 1, run bindings), M3 (job 2, manage windows), M5 (job 3, Bindings window), and S-008, S-009 and S-056 (job 4, tray status). The action registry the brief asks for is built with S-006.

Mac stories that don't carry over:
- **Grant Accessibility and Input Monitoring** (Mac S-002): Windows needs no permission for a keyboard hook or for moving windows. S-056 covers what does block the app: apps running as administrator.
- **Secure Event Input** (Mac S-038): Windows has no such switch; S-056 is the nearest case.
- **Event tap recovery** (Mac S-037): replaced by S-055 for the hook.
- **Copy the Unnamed Launcher config, and the rename** (Mac S-057, S-058): there was never a launcher on Windows.
- **The sysdiagnose chords** (Mac S-053's Hyper+Comma, Period and Slash): Mac only; Windows reserves its own (S-001).
- **Liquid Glass and SF Symbols**: acrylic, Mica and Segoe Fluent Icons instead.
- **The Homebrew cask**: shipping is the `distribute` step (WinGet later).
- **Bindings inside Settings** (Mac S-085): the Bindings window already has every page, Settings too.
- **Quit on close** (Mac S-086): Mac only in spec v16; Windows apps quit when their last window closes.
- **The Spaces settings and Reduce Motion tip** (Mac S-087): macOS settings with no Windows counterpart.
- **Hidden apps in Recent Windows** (part of Mac S-092, S-094): macOS only; `hidden` is `other-platform-field` here.
- **Space to record a key** (part of Mac S-044): Space invokes a tree row on Windows; Ctrl+K records.
- **Following a sent window without a slide** (Mac S-096's 0.5.3 fix): Windows already switches straight to that desktop.
- **Accounts, sync, analytics, upgrade prompts:** a personal app. One config per platform; at most, a dotfiles repo holds both.
