# Unnamed Manager

A native Windows 11 keyboard manager for one keyboard-first user. A hotkey or a leader sequence starts an app, opens a file, runs a command, sends a shortcut, or focuses, places, moves, resizes, maximizes or minimizes the window you're using. Actions are modules, so later ones (volume, display resolution) add a module and change nothing else.

App name `UnnamedManager`, display name "Unnamed Manager", repo `unnamed-manager-windows`. The Windows counterpart of `unnamed-manager-mac`. The config format is shared and defined in [spec/CONFIG-SPEC.md](../spec/CONFIG-SPEC.md) (the `unnamed-manager` repo as a git submodule at `spec/`). Words come from [spec/GLOSSARY.md](../spec/GLOSSARY.md).

## Replaces
- **Unnamed Launcher on the Mac**, for the same habits on a Windows PC: Hyper+Space → E opens the editor.
- **FancyZones and Win+arrow snapping** for one-shot window commands: focus by direction, size presets, maximize.

## Main jobs
1. **Run a binding by key sequence or direct chord.** The Guide appears only if you pause; the app in front keeps focus.
2. **Manage the window in front from the keyboard.** Focus the nearest window left, right, up or down, or the next or previous one. Place it on a half, third, quarter or corner. Move or resize it by a step or to an exact fraction. Maximize it (again restores) or minimize it.
3. **See and edit bindings in the Bindings window**, including the window actions, each with its own editor. Every change is written back to the config file.
4. **Check status from the tray icon:** on or off, reload, last error, open the Bindings window, quit.

## Data
One JSON file, `%USERPROFILE%\.config\unnamed-manager\config.json`, in the shared format. `APP_STORE` overrides it for tests and snapshots. The app watches the file and reloads; a reload with errors keeps the last valid config.
- **One config per platform** (spec v2): same format and rules as the Mac, not the same file.
- **Entry, Action, Context, Settings:** as in the spec, including `windows.gap` (spec v3).
- **App identity** (open in the spec, decided by the agent as the proposal to take there): `application` actions and contexts name an app by `exe` (file name, such as `Code.exe`, or a full path) or by `appId` (AppUserModelID, for Store and packaged apps). Settled in the spec repo with fixtures before it's built.
- **Window memory:** the frame each window had before Maximize, in memory only.

## Shape
A tray app with no taskbar button. The Guide (a topmost, non-activating panel that never takes focus), the tray menu, the Bindings window (NavigationView: Bindings, Actions, Contexts, Help), and Settings. Windows 11 (Windows 10 19041 or later runs). C# 14, .NET 10, WinUI 3, unpackaged. Local data, works offline, no accounts.

## System hooks
- **Low-level keyboard hook** (`WH_KEYBOARD_LL`) for leaders, direct chords and sequence keys, swallowing the keys it handles. It can't see keys while an elevated window is in front unless the app also runs elevated; the Guide says so instead of failing silently.
- **An action registry.** Each action family is one module: it decodes and validates its config, describes itself for the Guide and the editor, and runs.
- **Window actions with Win32:** `SetWindowPos`, `ShowWindow`, DWM extended frame bounds so placed windows line up without the invisible border, each monitor's work area from `GetMonitorInfo`, per-monitor DPI v2. Focus works within the foreground-lock rules (the hook makes the app eligible to call `SetForegroundWindow`).
- **Send Shortcut** with `SendInput`. **Run Command** and **Open** with `Process.Start` and `ShellExecute`.
- **Tray icon**, **single instance**, **Open at Login** through the `HKCU\...\Run` key (decided by the agent: unpackaged, no package identity needed).
- **Reserved chords:** `Win+L` and `Ctrl+Alt+Delete` can't be caught. Hyper alone and Hyper+W, T, X, D, O, P, L, N, Y open Office; the app warns and points to the registry fix (decided by the agent, to propose in the spec).

## Reference
None to clone. Behavior follows the Mac app and the spec; window commands follow Rectangle, sequences follow which-key style leader menus.

## Look
Native Windows 11 with the Windows accent color: Mica on the Bindings window, acrylic on the Guide, Segoe Fluent Icons for window actions in the Guide (decided by the agent; `icon` on Windows names a Segoe Fluent Icons glyph or a file path, to settle in the spec).

## Not in version one
- **Automatic tiling.** A basic version may come later.
- **Moving windows between displays or virtual desktops**, and remembering layouts.
- **Volume, display resolution and other new families.** The registry makes room for them.
- **Sharing one config with the Mac.** Later, at most, a dotfiles repo holds both files.
