# Architecture

Three projects and their tests. Core holds every rule that can run without Windows' UI; the WinUI app holds Win32 and WinUI, and the WPF app is the same app in WPF. Core never references `Microsoft.UI`.

```text
UnnamedManager (WinUI 3, net10.0-windows)  ──references──▶  UnnamedManager.Core (net10.0)
UnnamedManager.Wpf (WPF, net10.0-windows)  ──references──▶  UnnamedManager.Core, and links the app's files with no WinUI
tests/UnnamedManager.Core.Tests            ──references──▶  UnnamedManager.Core
spec/ (git submodule: the shared config spec, fixtures, schema)  ◀── read by Core's tests, schema embedded in Core
```

## Core (`src/UnnamedManager.Core/`)

- `Config/`: the config file. `ConfigReader` reads JSON strictly (the first problem stops the read), `ConfigValidator` checks the rules that need the whole file, `ConfigLoader` ties them together and writes the starter config and the schema, `ConfigState` keeps the last valid config across reloads. `ConfigWriter` writes a config back in the Mac app's fixed layout (one line per entry, action and context, defaults left out), through each family's `Write`. `Keys.cs` has key names, modifiers, chords, key paths and scan codes. Every issue carries the spec's rule id.
- `Actions/`: the action families. `ActionRegistry.Standard` lists them; each family is one file that reads its kind of action (`ActionFields` gives every family the same checks and messages), and, once its story is built, writes it, gives its schema part, and plans it (`ActionPlan`). The engine and the Guide know no action type by name.
- `Windows/`: the window actions. `IWindowSystem` is the seam to real windows; `WindowControl` runs a `WindowCommand` on the window in front and keeps the focus history (`FocusHistory`, spec v13) and Fill's memory; `WindowGeometry` has the math, `SeenWindows.cs` the visible parts, directions by edges, the reading order and the swap partner (spec v12), and `ArrangeGrid` Arrange Windows' cells (spec v8, v9); `WindowTarget` is Focus Window's and Swap Window's target; `FakeWindowSystem` keeps windows, their z-order and virtual desktops in memory for tests.
- `Config/BuiltInActions.cs`: built-in action names (spec v11). Every place that turns a binding into its action calls `Config.Resolve`, which looks in `actions` first, so an action there overrides a built-in one.
- `Keyboard/`: `SequenceEngine` is the state machine (key events in, consume or pass and an effect out; no Win32, no timers). `KeyboardTracker` sits in front of it for the hook: held modifiers, repeats, scan codes, the masking key; thread-safe; with `Recording` on (S-040) it matches no leader and hands the next key with its modifiers to the key recorder, consuming its key-up too. Held menus (spec v18) are in the engine too: a highlight, letting go of the leader's modifiers chooses (the tracker hands modifier changes to it), and a list waits for its items (`FillList`, then `Fill`) while its keys queue up. `GuideSchedule` decides when the Guide shows, updates and hides (effects and the time in, commands out; delay, timeout, flash, bell).
- `Config/ConfigEdits.cs` and `ConfigEditor.cs`: the Bindings window's edits as pure functions on `Config` (add, remove, change key, move, rename an action), and the draft every page edits: one undoable step per change, saved at once when it validates, kept on screen with its errors when it doesn't.
- `Config/ActionForms.cs`: the Actions page's forms: each type's fields, an action's values as text, and text back to an action through the family's own reader (`ConfigReader.ReadAction`), so a form checks what the file does.
- `Windows/WindowPresence.cs` and `WindowKinds.cs`: whether a window is here, on another virtual desktop or nowhere you can go (cloaked alone never says), and whether it's a standard window, a dialog (spec v21) or neither.
- `Keyboard/HomeLayout.cs` and `GuideSpeech.cs`: a big Home's columns and what Narrator hears, for both apps' Guides.
- `Config/GuideIcons.cs`: a row's icon: the entry's own, else its action's, and which rows show the app's glyph.
- `Config/ActionGlyphs.cs`: the Segoe Fluent Icons glyph the Guide shows for each window action (S-045).
- `AppVersion.cs`: the tray menu's version line.

## App (`src/UnnamedManager/`)

- `App.xaml.cs`: start-up and wiring. One copy per session (a named mutex), the config, the key service, the tray; the Guide's window is made the first time it shows (`App.Guide`), so with `showHud` false it never exists. `OnSequenceEffect` hands each effect to `GuideSchedule` and carries out its commands. No window at start.
- `Keyboard/`: `KeyboardHook` is `WH_KEYBOARD_LL` on its own thread; its callback only asks the tracker and posts effects. `KeyboardHook.Guard` (S-055) is a message-only window on a second thread: resume from sleep, session unlock and a 2 s watchdog (Core's `HookWatchdog`: input by `GetLastInputInfo` with no hook call sends the mask key as a probe) ask the hook thread, by thread message, to install the hook again. `KeyService` owns hook and tracker and raises effects on the UI thread. While a held menu or a list is up, `App` checks every 100 ms for a key-up the hook missed and for an item's window that closed; a `FillList` effect is filled there from `WindowControl.RecentWindows`. `ForegroundWatcher` also notes whether the app in front runs as administrator.
- `Platform/`: Windows settings the app changes for the user, each with test keys under `HKCU\Software\UnnamedManager-Test` while `APP_STORE` is set: `Startup` (the Run value and Task Manager's `StartupApproved`, rules in Core's `StartupEntry`), `OfficeShortcuts` (the `ms-officeapp` handler), and `ProcessElevation` (whether a window's process is elevated; Core's `Elevation` holds the words and the rule).
- `Guide/`: `GuideWindow` (never activated, acrylic, faded by `UI/WindowFade` through the layered window's alpha, sized to content, placed by Core's `GuidePlacement` on a `Screen` from `Win32WindowSystem.Screens`) and `GuideView` (XAML, theme brushes only).
- `Actions/`: `ActionRunner` turns a family's plan into what Windows does; `AppSwitcher` finds, focuses and starts apps, and says when focus didn't arrive (as `WindowControl.RunAsync` does for Focus Window). A list's item runs as a `WindowCommand.Choose`. `VirtualDesktops` reads and changes virtual desktops for Switch Desktop and Send to Desktop: the public `IVirtualDesktopManager`, and the shell's undocumented `IVirtualDesktopManagerInternal` in its Windows 11 24H2 shape.
- `ConfigStore`: the one owner of the file. It reads (keeping the last valid config), saves (validated first, refused with errors, then a temp file and `File.Replace`), and watches the folder with a 300 ms debounce, skipping text it read or wrote itself. The app restarts the keys only when the config in use changes.
- `MainWindow` and `Pages/`: the Bindings window (Mica, a NavigationView). Closing hides it; a left click on the tray icon or Edit bindings opens it (`MainWindow.Open`). `BindingsPage` shows the config in use as a `TreeView` in node mode (so a reload keeps collapsed menus), with the file's errors in an `InfoBar`; Core's `BindingLabels` says what each row reads.
- `ActionsPage` builds each type's form with `ActionFormView` from Core's `ActionForms`. `ContextAwareForm` has a section for each context and one for the fallback, each with a form view of its own; a section is rebuilt only when its value changed from outside it. `AppPicker` is Choose app (the apps in Start from `Actions/StartApps`, shell:AppsFolder through raw COM vtables, so it stays AOT-safe) and Browse (a file picker).
- `ContextsPage` lists contexts in order with their apps; `Actions/AppIcons` gives an app's name (from Start, else the exe's description) and its icon (`IShellItemImageFactory`, cached).
- `Tray/`: `TrayIcon` (Shell_NotifyIcon on a hidden window, which also gets TaskbarCreated and a second copy's message), `TrayMenu` (a WinUI MenuFlyout from a host window), `ConfigFiles`.
- `SelfTest.cs` (run order; one partial file per area: `SelfTest.Keys.cs`, `.Guide.cs`, `.Actions.cs`, `.Tray.cs`, and one per Bindings window page: `.Bindings.cs`, `.ActionsPage.cs`, `.Contexts.cs`, `.Settings.cs`), `Snapshot.cs`: the agent modes (`--selftest`, `APP_SNAPSHOT`), only with `APP_STORE` in `%TEMP%`.

## WPF app (`src/UnnamedManager.Wpf/`)

The same app in WPF, for PCs that block the WinUI build's XAML compiler. It references Core and links every app file with no WinUI in it (its csproj lists them under `Shared\`): the hook and its guard, `KeyService`, `ConfigStore`, the actions, `Platform/`, `TrayIcon`, `GuideIconCache`, `GlyphFont`, `WindowFade`, and `SelfTest.Shared.cs`, the checks both apps run. A file that moves into that list must not use WinUI.

- `Compat/WinUiShims.cs`: WinUI's `DispatcherQueue` and `DispatcherQueueTimer` on WPF's `Dispatcher`, and the `ImageSource` name, so `KeyService`, `ConfigStore` and `GuideIconCache` compile unchanged. Only what those files call.
- `SelfTest.cs`, `Snapshot.cs`: its own agent modes (`--selftest`, `APP_SNAPSHOT`), only with `APP_STORE` in `%TEMP%`.
- `App.xaml.cs`: the WinUI `App` without the Bindings window and Welcome. Same instance key, so only one of the two apps runs. `--check` (with `APP_STORE` in `%TEMP%`) loads the config, installs the hook, shows the Guide, saves it as `guide-light.png` and `guide-dark.png` beside the config, opens the tray menu, and exits 0 or 1.
- `Guide/`: `GuideWindow` (never activated, solid, DWM's rounded corners, placed by Core's `GuidePlacement` in physical pixels) and `GuideView`, ports of the WinUI ones. Keep them in step.
- `Tray/TrayMenu.cs`: a WPF `ContextMenu` from a 1 px host window, with Run at startup in place of the Settings page.
- `Actions/AppIcons.cs`: the shell's pixels (`ShellIcons`, shared) as a WPF image.
- Theme: WPF's Fluent theme (`Application.ThemeMode`), from the config's `theme`. Colors are its brushes, which have WinUI's names.

## Threads

The hook thread runs the tracker and nothing else; the guard thread watches it and never touches the tracker; everything it decides reaches the UI thread through `DispatcherQueue.TryEnqueue`. The UI thread shows the Guide, runs actions, and owns the tray. Starting an app waits without blocking it.

## Seams

- `ActionRegistry`: a new family is a new file and one line. Tests can build their own registry.
- `ConfigLoader.Load(json)`: every rule is tested from JSON text.
- `IWindowSystem` (Core `Windows/`): the window actions go through it. `WindowControl` holds their rules and `WindowGeometry` their math, in physical pixels (spec points times the monitor scale). `Win32WindowSystem` (app `Actions/`) and `FakeWindowSystem` (Core, for unit tests and the self-test) are its two adapters. The foreground WinEvent hook (`ForegroundWatcher`) reports each focus change to `Win32WindowSystem.Focused`, which feeds the focus history. Use Application still enumerates windows in `AppSwitcher`.
