# Unnamed Manager for Windows

A keyboard manager for Windows that lives in the system tray, the Windows counterpart of [Unnamed Manager for macOS](https://github.com/juyanith/unnamed-manager-mac). Press a leader chord such as Hyper+Space, then one key per menu level, to open apps, files and URLs, run commands, send shortcuts to the app in front, or focus, place, move and resize its window.

If you finish the sequence quickly, nothing appears on screen. If you pause, the Guide shows where you are and which keys come next. The app in front never loses focus.

> [!WARNING]
> **Work in progress.** Unnamed Manager is still being built. Features can change or break between releases, and the config format can change too. It reads every key you press and can move, and type into, other apps' windows. If you choose to use it, you do so at your own risk: it comes as is, with no warranty of any kind (see the [license](LICENSE)).

## Features

- **Leader chords and menus.** Bindings form a tree with as many levels as you need. Back (Backspace) goes up a level and Cancel (Esc) ends the sequence. You can have as many leaders as you like.
- **Direct bindings.** A chord such as Hyper+T can run an action on its own, with no menu and no Guide.
- **Actions:**
  - **Use Application** focuses an app, starts it, or does whichever fits (the default). Apps are named by exe (`Code.exe`) or by AppUserModelID for Store apps.
  - **Open** opens a file, folder or URL.
  - **Run Command** runs a process directly, with no shell.
  - **Send Shortcut** sends key chords to the app in front.
  - **Window actions** focus the nearest window in a direction, place the window on a half, third, quarter or corner, move or resize it, maximize it or minimize it.
  - **Context Aware** picks a different action depending on which app is in front.
  - **Do Nothing** and **Show Help**.
- **Icons in the Guide:** apps, files and URLs show their own icon, and window actions show a Segoe Fluent Icons glyph.
- **A Bindings window** to edit menus, bindings, actions and contexts, record chords, and reorder items. Its Help page lists your leaders and direct bindings.
- **One JSON config file** at `%USERPROFILE%\.config\unnamed-manager\config.json`. The app watches it and reloads it when it changes, so you can keep it in your dotfiles. A config with errors is reported, and the last valid one stays in use.
- **A tray menu** to turn it on or off, reload the config, open the Bindings window, see the last error, and quit. It can also turn off the Office shortcuts that Windows puts on Hyper, and Settings can run it at startup.

## Requirements

- Windows 11, or Windows 10 version 2004 (build 19041) or later. x64 or Arm64.
- Nothing else for a release: it carries .NET and the Windows App SDK.
- Windows doesn't let an app type into, or read keys from, an app running as administrator unless it runs as administrator too. The tray menu says so when one is in front.

## Install

Download the zip for your PC (`win-x64` or `win-arm64`) from [Releases](https://github.com/juyanith/unnamed-manager-windows/releases), extract it to a folder such as `%LOCALAPPDATA%\Programs\UnnamedManager`, and run `UnnamedManager.exe`. To update, quit it from the tray menu and replace the folder.

Releases aren't code signed yet, so SmartScreen may say "Windows protected your PC". Choose More info, then Run anyway.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`). No Visual Studio.

```powershell
./build.ps1 -Open
```

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
dotnet test tests/UnnamedManager.Core.Tests   # unit tests
tools/selftest.ps1                            # the app's own checks, against a %TEMP% config
tools/snap.ps1                                # light and dark snapshots of every page
```

Clone with `--recurse-submodules`: the shared spec is a submodule at `spec/`.

- `src/UnnamedManager/` is the app. `src/UnnamedManager.Core/` holds the rules and config logic, with no WinUI, and has the unit tests.
- `src/UnnamedManager.Wpf/` is the same app in WPF, for PCs that block the WinUI build (see below).

### The WPF version

Some managed PCs won't let the WinUI build run its XAML compiler, which runs as a separate exe. WPF compiles its XAML inside the build itself, so `src/UnnamedManager.Wpf` builds there. It shares Core and every app file that doesn't use WinUI, so its keys, actions, config and Guide work the same. It has no Bindings window, Welcome or self-test yet: edit the config file, and use the tray menu for the rest, including Run at startup. Only one of the two apps runs at a time.

```powershell
dotnet build src/UnnamedManager.Wpf -c Release
```

The exe is `UnnamedManager.Wpf.exe` under the build folder (`%LOCALAPPDATA%\Dev\UnnamedManager\<checkout>\bin\UnnamedManager.Wpf\release`). If that folder isn't allowed to run programs, build somewhere that is: add `-p:ArtifactsPath=<folder>`. It needs the .NET 10 Desktop Runtime. A newer .NET SDK builds it too.

To check a build on a new PC, run it once against a test config. It loads the config, installs the hook, shows the Guide and opens the tray menu, then prints PASS or FAIL for each and exits:

```powershell
$env:APP_STORE = "$env:TEMP\um-check\config.json"; $env:APP_LOG = "$env:TEMP\um-check\log.txt"
.\UnnamedManager.Wpf.exe --check; Get-Content $env:APP_LOG
```
- [ARCHITECTURE.md](ARCHITECTURE.md) covers the projects, threads and seams.
- [docs/](docs/) has the brief, the stories, the glossary and the design notes.

## Releases

GitHub Actions builds the app and runs the unit tests and the self-test on x64 and Arm64 for every push and pull request (`.github/workflows/ci.yml`). To publish a release, push a version tag:

```powershell
git tag v0.2.0; git push origin v0.2.0
```

`.github/workflows/release.yml` publishes a self-contained build with that version for x64 and Arm64, runs the self-test on each, zips them, and attaches both to a GitHub Release. A tag with a hyphen, such as `v0.2.0-beta.1`, makes a pre-release.

To make the same build locally: `tools/publish.ps1 -Version 0.2.0 -Zip`.

## License

MIT. See [LICENSE](LICENSE).
