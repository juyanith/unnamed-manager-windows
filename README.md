# Unnamed Manager for Windows

A keyboard manager for Windows that lives in the system tray, the Windows counterpart of [Unnamed Manager for macOS](https://github.com/juyanith/unnamed-manager-mac). Press a leader chord such as Hyper+Space, then one key per menu level, to open apps, files and URLs, run commands, send shortcuts to the app in front, or focus, place, move, resize, swap and arrange windows.

If you finish the sequence quickly, nothing appears on screen. If you pause, the Guide shows where you are and which keys come next. The app in front never loses focus.

> [!WARNING]
> **Work in progress.** Unnamed Manager is still being built. Features can change or break between releases, and the config format can change too. It reads every key you press and can move, and type into, other apps' windows. If you choose to use it, you do so at your own risk: it comes as is, with no warranty of any kind (see the [license](LICENSE)).

## Features

- **Leader chords and menus.** Bindings form a tree with as many levels as you need. Back (Backspace) goes up a level and Cancel (Esc) ends the sequence. You can have as many leaders as you like.
- **Direct bindings.** A chord such as Hyper+T can run an action on its own, with no menu and no Guide.
- **Held menus.** With Hold to choose on a leader, keep its modifiers held, press its key again to move the highlight, and let go to choose, as Alt+Tab does.
- **Recent Windows.** A menu the app fills with the windows you used last, dialogs and windows on other virtual desktops included. Choosing one goes to it, or trades places with it.
- **Home.** Back from a leader's own menu shows every leader and direct binding, grouped by their modifiers.
- **Actions:**
  - **Use Application** focuses an app's window you used last, on any virtual desktop, starts it, or does whichever fits (the default). Apps are named by exe (`Code.exe`) or by AppUserModelID for Store apps.
  - **Open** opens a file, folder or URL.
  - **Run Command** runs a process directly, with no shell.
  - **Send Shortcut** sends key chords to the app in front.
  - **Focus Window** goes to the window in a direction, the next or previous one, one by its place in reading order, or the window you used before.
  - **Swap Window** trades places with another window, chosen the same ways.
  - **Arrange Windows** tiles the windows you used last on a display, or all of them, in a grid. Swap and Arrange never move a dialog.
  - **Fill** uses the whole screen less the gap, and again restores. **Minimize**, **Place**, **Move** and **Resize Window** do the rest: halves, thirds, quarters, named spots, steps or exact sizes.
  - **Switch Desktop** goes to the virtual desktop on the left or right, or to one by number, and **Send to Desktop** puts the window on another one, with you or without.
  - **Context Aware** picks a different action depending on which app is in front, with a fallback for every other app.
  - **Do Nothing** and **Show Help**.
- **Built-in actions.** A binding can name an action such as `window-place-left-half` with nothing to set up. The Help page lists them all.
- **Icons in the Guide:** any entry can have an icon of its own, a Segoe Fluent Icons code point or a file's icon. Otherwise apps, files and URLs show theirs, window actions a glyph, and menus the app's glyph.
- **A Bindings window** to edit all of it: menus, bindings, actions, Context Aware variants, contexts and icons. Record chords, duplicate, sort and reorder. Its Help page lists your leaders and direct bindings, held menus and the built-in actions.
- **A WPF version** for PCs that block the WinUI app: the keys, the Guide and the tray menu. It has no editor, Settings or Welcome, so you edit the config file ([The WPF version](#the-wpf-version)).
- **One JSON config file** at `%USERPROFILE%\.config\unnamed-manager\config.json`. The app watches it and reloads it when it changes, so you can keep it in your dotfiles. A config with errors is reported, and the last valid one stays in use.
- **A tray menu** to turn it on or off, reload the config, open the Bindings window, see the last error, and quit. It can also turn off the Office shortcuts that Windows puts on Hyper, and Settings can run it at startup.

## Requirements

- Windows 11, or Windows 10 version 2004 (build 19041) or later. x64 or Arm64.
- Nothing else for a release: it carries .NET and the Windows App SDK.
- Windows doesn't let an app type into, or read keys from, an app running as administrator unless it runs as administrator too. The tray menu says so when one is in front.

## Install

Download the zip for your PC (`UnnamedManager-<version>-win-x64.zip`, or `-win-arm64.zip` for Arm) from [Releases](https://github.com/juyanith/unnamed-manager-windows/releases), extract it to a folder such as `%LOCALAPPDATA%\Programs\UnnamedManager`, and run `UnnamedManager.exe`. To update, quit it from the tray menu and replace the folder.

On a PC that blocks the WinUI app, take the zip that starts `UnnamedManager.Wpf-` (in releases after 0.5.1), extract it to a folder of its own and run `UnnamedManager.Wpf.exe`. Only one of the two runs at a time.

Releases aren't code signed yet, so SmartScreen may say "Windows protected your PC". Choose More info, then Run anyway.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`). No Visual Studio.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Open
```

Windows blocks scripts by default, so the script commands here start with `powershell ... -ExecutionPolicy Bypass`. That applies to that one run and changes no setting.

This builds for your PC's architecture, installs to `%LOCALAPPDATA%\Programs\UnnamedManager` with a Start menu entry, and starts it. Build products go to `%LOCALAPPDATA%\Dev\UnnamedManager`, outside the repo.

## Config

On first launch the app writes a starter config. A small example:

```json
{
  "version": 1,
  "entries": [
    { "keys": "Hyper+Space", "title": "Commands" },
    { "keys": "Hyper+Space E", "title": "Editor", "action": "open-editor" },
    { "keys": "Hyper+Space F", "title": "Find", "action": "search" },
    { "keys": "Hyper+T", "title": "Terminal", "action": "open-terminal" }
  ],
  "actions": {
    "open-editor": { "type": "application", "exe": "Code.exe" },
    "open-terminal": { "type": "application", "exe": "wt.exe" },
    "search": {
      "type": "contextual",
      "variants": { "browser": { "type": "send", "keys": "Primary+L" } },
      "fallback": { "type": "send", "keys": "Primary+F" }
    }
  },
  "contexts": [
    { "id": "browser", "apps": ["chrome.exe", "msedge.exe"] }
  ]
}
```

Chords use `Ctrl`, `Alt`, `Shift` and `Cmd` (the Win key), or the groups `Hyper` (Ctrl+Alt+Shift+Win), `Meh` (Ctrl+Alt+Shift) and `CAG` (Ctrl+Alt+Win). `Primary` is Ctrl on Windows and Cmd on the Mac, so one Send Shortcut works on both. An entry with an `action` is a binding, and one without is a menu. A top-level entry without an action is a leader; with one, it's a direct binding. The format is shared with the Mac app and defined in [spec/CONFIG-SPEC.md](https://github.com/juyanith/unnamed-manager/blob/main/CONFIG-SPEC.md).

## Development

```powershell
# unit tests
dotnet test tests/UnnamedManager.Core.Tests
# the app's own checks, against a %TEMP% config
powershell -NoProfile -ExecutionPolicy Bypass -File tools/selftest.ps1
# light and dark snapshots of every page
powershell -NoProfile -ExecutionPolicy Bypass -File tools/snap.ps1
# the WPF app's checks
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dev.ps1 selftest -App wpf
# and its snapshots
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dev.ps1 snap -App wpf
# all of what CI runs, here, before you push
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ci.ps1
```

Clone with `--recurse-submodules`: the shared spec is a submodule at `spec/`.

- `src/UnnamedManager/` is the app. `src/UnnamedManager.Core/` holds the rules and config logic, with no WinUI, and has the unit tests.
- `src/UnnamedManager.Wpf/` is the same app in WPF, for PCs that block the WinUI build (see below).

### The WPF version

Some managed PCs won't let the WinUI build run its XAML compiler, which runs as a separate exe. WPF compiles its XAML inside the build itself, so `src/UnnamedManager.Wpf` builds there. It shares Core and every app file that doesn't use WinUI, so its keys, actions and config work the same. Its Guide is a port with the same rows and words, on a solid background. A left click on its tray icon opens the menu, and Help opens this README. It has no Bindings window, Settings or Welcome: edit the config file, and use the tray menu for the rest, including Run at startup. Only one of the two apps runs at a time.

```powershell
dotnet build src/UnnamedManager.Wpf -c Release
```

The exe is `UnnamedManager.Wpf.exe` under the build folder (`%LOCALAPPDATA%\Dev\UnnamedManager\<checkout>\bin\UnnamedManager.Wpf\release`). If that folder isn't allowed to run programs, build somewhere that is (see below). A build you make this way needs the .NET 10 Desktop Runtime; the zip from a release doesn't. A newer .NET SDK builds it too.

### Build somewhere else

Builds go to `%LOCALAPPDATA%\Dev\UnnamedManager\<checkout>` (`Directory.Build.props`). To use another folder on your PC, such as one a managed PC lets run programs, add a `Directory.Build.local.props` next to it. Git ignores that file, so it stays on your PC:

```xml
<Project>
  <PropertyGroup>
    <ArtifactsPath>C:\Dev\Builds\unnamed-manager-windows</ArtifactsPath>
  </PropertyGroup>
</Project>
```

Visual Studio, `dotnet build`, `build.ps1` and `tools/dev.ps1` all use it. In Visual Studio, close and reopen the solution after you add or change the file. The exe is then under `<folder>\bin\<project>\<configuration>`, such as `C:\Dev\Builds\unnamed-manager-windows\bin\UnnamedManager.Wpf\debug\UnnamedManager.Wpf.exe`.

An `ArtifactsPath` environment variable or `-p:ArtifactsPath=<folder>` on the command line does the same for one shell or one build. Visual Studio only sees an environment variable that was set before it started.

To check a build on a new PC, run it once against a test config. It loads the config, installs the hook, shows the Guide and opens the tray menu, then prints PASS or FAIL for each and exits:

```powershell
$env:APP_STORE = "$env:TEMP\um-check\config.json"; $env:APP_LOG = "$env:TEMP\um-check\log.txt"
.\UnnamedManager.Wpf.exe --check; Get-Content $env:APP_LOG
```

`--selftest` in place of `--check` runs all of its behavior checks.

### More

- [ARCHITECTURE.md](ARCHITECTURE.md) covers the projects, threads and seams.
- [docs/](docs/) has the brief, the stories, the glossary and the design notes.

## Releases

GitHub Actions builds the app and runs the unit tests and the self-test on x64 and Arm64 for every push and pull request (`.github/workflows/ci.yml`). To publish a release, push a version tag:

```powershell
git tag v0.2.0; git push origin v0.2.0
```

`.github/workflows/release.yml` publishes self-contained builds of both apps with that version for x64 and Arm64, runs each one's self-test, zips them, and attaches all four to a GitHub Release. Take `UnnamedManager-<version>-win-<arch>.zip`, or `UnnamedManager.Wpf-<version>-win-<arch>.zip` on a PC that blocks the WinUI app; `<arch>` is `x64` for most PCs and `arm64` for Arm (Snapdragon) ones. Only one of the two apps runs at a time. A tag with a hyphen, such as `v0.2.0-beta.1`, makes a pre-release.

To make the same builds locally: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/publish.ps1 -Version 0.2.0 -Zip`, with `-App wpf` for the WPF app and `-Arch x64` for x64 on an Arm64 PC.

## License

MIT. See [LICENSE](LICENSE).
