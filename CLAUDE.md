# UnnamedManager

A native Windows keyboard manager in WinUI 3, the Windows counterpart of
`unnamed-manager-mac`: leader keys and direct bindings for apps, files,
commands, shortcuts and windows. A tray app with no taskbar button. Its only
data is the JSON config file `%USERPROFILE%\.config\unnamed-manager\config.json`
(`APP_STORE` overrides it). What it does and how: `docs/BRIEF.md`,
`docs/STORIES.md`, `docs/GLOSSARY.md` (Windows terms; the shared words are in
`spec/GLOSSARY.md`). How each surface looks and where every command lives:
`docs/DESIGN.md`.

M1 is built: the config is read and checked in Core (`Config/`), the sequence
engine and the hook tracker are in Core (`Keyboard/`), and the app has the hook
(`Keyboard/`), the Guide (`Guide/`), Use Application (`Actions/`) and the tray
(`Tray/`). It starts with no window; the template's window holds Settings only
until M5 turns it into the Bindings window. Remove the template parts as each
story replaces them.

## Shared spec

- The config format is shared with the Mac app. Its contract lives in `spec/`,
  a git submodule of the `unnamed-manager` repo (`git submodule update --init`
  after cloning; a local clone needs `-c protocol.file.allow=always`):
  `spec/CONFIG-SPEC.md`, `spec/config.schema.json`, `spec/fixtures/`.
- Change the format in the spec repo first, tag it, then move the submodule
  and make the fixture tests pass. Never edit files inside `spec/` from here.
- Each story's `Mac:` line names the Mac story it ports; read it in
  `../unnamed-manager-mac/docs/STORIES.md` for behavior already worked out.

## This PC

- It's ARM64, and `dotnet` builds for ARM64. The scripts read the machine's
  architecture from the registry, since a shell can run as x64 under emulation.
- From Git Bash, run scripts as
  `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1`. In
  PowerShell 5.1, `dotnet` writing to stderr can look like a failure; trust
  the script's own last line.

## Layout

- `src/UnnamedManager/` the app. `App.xaml.cs` lists the agent flags and starts the window; `MainWindow.xaml` has the title bar and the navigation.
- `src/UnnamedManager.Core/` rules and data logic with no WinUI: the config model, `ConfigReader` and `ConfigValidator` (every spec rule, with its id), and the action families (`ActionRegistry`, one per module). Tests in `tests/UnnamedManager.Core.Tests/`.
- `UnnamedManager.slnx` the solution. There is no Visual Studio project to open: everything builds with the .NET SDK.
- `build.ps1` builds and installs `%LOCALAPPDATA%\Programs\UnnamedManager\UnnamedManager.exe` with a Start menu entry.
- `Icon/AppIcon.svg` the app icon. `build.ps1` renders it with `tools/make-icon.ps1`; `tools/make-icon.ps1 -Review` writes every size on light and dark into one PNG.
- `docs/` the brief, the screen index, and the user stories, when they exist.

## Build and check

- Build and install: `./build.ps1`. Add `-Open` to start the new build, `-Dev` for a faster debug build. The build shows warnings; fix them.
- Unit tests: `dotnet test tests/UnnamedManager.Core.Tests`.
- Self-test (the app's real functions on a `%TEMP%` config, starting with no file): `tools/selftest.ps1`. Exit 0 when every check passes. Add a check for every story with logic in `SelfTest.cs`.
- After every UI change, take snapshots and look at them: `tools/snap.ps1`.
  It writes `%TEMP%\UnnamedManager-shots-<checkout>\<page>-light.png` and `-dark.png` for every page, then quits. `tools/snap.ps1 -Pages settings` limits the pages.
  For a dialog, flyout, or second window, add a capture to `Snapshot.cs` like the page loop.
- In a git worktree, or with other agents building at the same time: `tools/dev.ps1 build|selftest|snap` builds this checkout without installing anything. Each checkout has its own build, test store, and snapshot folders.
- The app writes agent output (PASS lines, snapshot paths, crashes) to the file in `APP_LOG`; the tools print it. A WinUI app has no dependable stdout.
- Snapshots and the self-test refuse to run without `APP_STORE` in `%TEMP%`.
- Never test against the real config in `%USERPROFILE%\.config\unnamed-manager\`.
- Then check the grid: `tools/grid-lint.ps1` and `tools/check-layout.ps1`. Both print PASS.
- A change is done when the build has no errors or warnings, the grid checks pass, and you looked at the light and dark snapshots.
- A build for other PCs: `tools/publish.ps1` (self-contained) or `tools/publish.ps1 -Aot` (NativeAOT, starts about 2.5x faster). It fails on trim warnings and runs the self-test on the result. JSON goes through `AppJson` (source generated), never reflection.

## Design rules

- Design rules: `design/DESIGN-SYSTEM.md`. Use its tokens; never raw colors, radii, or durations.

- Use WinUI's controls and theme resources. The accent always follows Windows (`UI/Theme.cs`, `Theme.DefaultId = "system"`, no picker). The app theme is the one setting in Settings › Appearance (later the config's `theme`, S-035).
- Colors come from theme brushes: `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush`, `CardBackgroundFillColorDefaultBrush`, `AccentFillColorDefaultBrush`, `AccentTextFillColorPrimaryBrush`. Never a hex color, a fixed gray, or white for text.
- Spacing comes from `UI/Space.xaml` (`SpaceSm`, `SpaceLg`, `PageMargin`) in XAML and `Space` in code, on a 4 epx grid. Mark custom pages and cards with `ui:GridArea` and `ui:GridProbe`.
- The window: Mica backdrop, the `TitleBar` control with the pane toggle, a left `NavigationView` with Settings at the bottom. Pages open with a title in `TitleTextBlockStyle` and their commands in a `CommandBar` on the right.
- Settings is one page of `SettingsCard`s under group headers, like Windows Settings.
- Every command has a `KeyboardAccelerator` and a label; the self-test checks it. Delete and arrow keys are scoped to the list (`ScopeOwner`), so they never fire in a text box.
- Labels and buttons use sentence case: "New item", not "New Item".
- `FontIcon` with Segoe Fluent Icons glyphs for icons.
- Empty states: a glyph, one line on what the page is for, and the first action.
- Windows open calm: no text box has focus at launch. A new item focuses its title field.

## Skills

The bawa plugin has a skill for each step: `windows-design` (design rules),
`design-system` (accent and tokens), `layout-grid`, `ux-design`,
`csharp-winui`, `os-integrations`, `performance`, `build-and-verify`,
`test-windows-app`, `app-icon`, `user-stories`, and `improve-loop`. Use them.

## Git

- Commit after each working change, with a message that says what the user can now do.
