# Architecture

Two projects and their tests. Core holds every rule that can run without Windows' UI; the app holds Win32 and WinUI. Core never references `Microsoft.UI`.

```text
UnnamedManager (WinUI 3, net10.0-windows)  ──references──▶  UnnamedManager.Core (net10.0)
tests/UnnamedManager.Core.Tests            ──references──▶  UnnamedManager.Core
spec/ (git submodule: the shared config spec, fixtures, schema)  ◀── read by Core's tests, schema embedded in Core
```

## Core (`src/UnnamedManager.Core/`)

- `Config/`: the config file. `ConfigReader` reads JSON strictly (the first problem stops the read), `ConfigValidator` checks the rules that need the whole file, `ConfigLoader` ties them together and writes the starter config and the schema, `ConfigState` keeps the last valid config across reloads. `ConfigWriter` writes a config back in the Mac app's fixed layout (one line per entry, action and context, defaults left out), through each family's `Write`. `Keys.cs` has key names, modifiers, chords, key paths and scan codes. Every issue carries the spec's rule id.
- `Actions/`: the action families. `ActionRegistry.Standard` lists them; each family is one file that reads its kind of action (`ActionFields` gives every family the same checks and messages), and, once its story is built, writes it, gives its schema part, and plans it (`ActionPlan`). The engine and the Guide know no action type by name.
- `Windows/`: the window actions. `IWindowSystem` is the seam to real windows; `WindowControl` runs a `WindowCommand` on the window in front; `WindowGeometry` has the math; `FakeWindowSystem` keeps windows in memory for tests.
- `Keyboard/`: `SequenceEngine` is the state machine (key events in, consume or pass and an effect out; no Win32, no timers). `KeyboardTracker` sits in front of it for the hook: held modifiers, repeats, scan codes, the masking key; thread-safe; with `Recording` on (S-040) it matches no leader and hands the next key with its modifiers to the key recorder, consuming its key-up too. `GuideSchedule` decides when the Guide shows, updates and hides (effects and the time in, commands out; delay, timeout, flash, bell).
- `Config/ConfigEdits.cs` and `ConfigEditor.cs`: the Bindings window's edits as pure functions on `Config` (add, remove, change key, move, rename an action), and the draft every page edits: one undoable step per change, saved at once when it validates, kept on screen with its errors when it doesn't.
- `Config/ActionForms.cs`: the Actions page's forms: each type's fields, an action's values as text, and text back to an action through the family's own reader (`ConfigReader.ReadAction`), so a form checks what the file does.
- `AppVersion.cs`: the tray menu's version line.

## App (`src/UnnamedManager/`)

- `App.xaml.cs`: start-up and wiring. One copy per session (a named mutex), the config, the key service, the tray; the Guide's window is made the first time it shows (`App.Guide`), so with `showHud` false it never exists. `OnSequenceEffect` hands each effect to `GuideSchedule` and carries out its commands. No window at start.
- `Keyboard/`: `KeyboardHook` is `WH_KEYBOARD_LL` on its own thread; its callback only asks the tracker and posts effects. `KeyService` owns hook and tracker and raises effects on the UI thread.
- `Guide/`: `GuideWindow` (never activated, acrylic, sized to content, placed by Core's `GuidePlacement` on a `Screen` from `Win32WindowSystem.Screens`) and `GuideView` (XAML, theme brushes only).
- `Actions/`: `ActionRunner` turns a family's plan into what Windows does; `AppSwitcher` finds, focuses and starts apps.
- `ConfigStore`: the one owner of the file. It reads (keeping the last valid config), saves (validated first, refused with errors, then a temp file and `File.Replace`), and watches the folder with a 300 ms debounce, skipping text it read or wrote itself. The app restarts the keys only when the config in use changes.
- `MainWindow` and `Pages/`: the Bindings window (Mica, a NavigationView). Closing hides it; a left click on the tray icon or Edit bindings opens it (`MainWindow.Open`). `BindingsPage` shows the config in use as a `TreeView` in node mode (so a reload keeps collapsed menus), with the file's errors in an `InfoBar`; Core's `BindingLabels` says what each row reads.
- `ActionsPage` builds each type's form with `ActionFormView` from Core's `ActionForms`. `AppPicker` is Choose app (the apps in Start from `Actions/StartApps`, shell:AppsFolder through raw COM vtables, so it stays AOT-safe) and Browse (a file picker).
- `Tray/`: `TrayIcon` (Shell_NotifyIcon on a hidden window, which also gets TaskbarCreated and a second copy's message), `TrayMenu` (a WinUI MenuFlyout from a host window), `ConfigFiles`.
- `SelfTest.cs` (run order; one partial file per area: `SelfTest.Keys.cs`, `.Guide.cs`, `.Actions.cs`, `.Tray.cs`), `Snapshot.cs`: the agent modes (`--selftest`, `APP_SNAPSHOT`), only with `APP_STORE` in `%TEMP%`.

## Threads

The hook thread runs the tracker and nothing else; everything it decides reaches the UI thread through `DispatcherQueue.TryEnqueue`. The UI thread shows the Guide, runs actions, and owns the tray. Starting an app waits without blocking it.

## Seams

- `ActionRegistry`: a new family is a new file and one line. Tests can build their own registry.
- `ConfigLoader.Load(json)`: every rule is tested from JSON text.
- `IWindowSystem` (Core `Windows/`): the window actions go through it. `WindowControl` holds their rules and `WindowGeometry` their math, in physical pixels (spec points times the monitor scale). `Win32WindowSystem` (app `Actions/`) and `FakeWindowSystem` (Core, for unit tests and the self-test) are its two adapters. Use Application still enumerates windows in `AppSwitcher`.
