# Architecture check-in, end of M6 (2026-11-12)

M6 added Windows' system hooks: Run at startup (`Platform/Startup`), the hook's guard
thread and watchdog (`Keyboard/KeyboardHook.Guard`, Core `HookWatchdog`), apps running as
administrator (`Platform/ProcessElevation`, Core `Elevation`), and the Office shortcuts
switch (`Platform/OfficeShortcuts`). 31 files, about 970 lines. The largest file is
`SelfTest.Bindings.cs` at 367 lines; no page or class is near a limit.

Proof for any refactor below: 495 unit tests, 195 self-test checks, every snapshot page,
layout files PASS, grid-lint PASS.

## One rule for the test registry keys  [Worth exploring]

Files: Platform/Startup.cs, Platform/OfficeShortcuts.cs
Signal: two adapters repeat "with APP_STORE, use a key under HKCU\Software\UnnamedManager-Test".
Proposal: `UserRegistry.Path(string real)` in `Platform/` that maps a real HKCU path to its
test twin, and one `UserRegistry.ClearTest()` the self-test calls at the start and end.
Benefits: the next registry switch (a jump list, a URL protocol) can't touch the real keys by mistake.

## Hook test helpers on the production class  [Speculative]

Files: Keyboard/KeyboardHook.Guard.cs
Problem: `SimulateWake`, `DropForTest`, `SlowForTest` sit on `KeyboardHook`. They're used only
by `--selftest`, which is an agreed agent mode, so this is within the rules.
Proposal: none now; revisit if the guard grows.

## Considered, not recommended

- Moving `ProcessElevation` into `Win32WindowSystem`: the tray's foreground watcher needs it
  too, and it has nothing to do with moving windows.
- Raw input for the watchdog: registering it stopped the hook seeing keys (FIXLOG).

## Top recommendation

None is Strong: leave both for the user.
