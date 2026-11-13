# Fix log

Bugs worth remembering, newest first. Each ends with the rule that prevents it.

## Layout probes measured cards that weren't on screen (M5, S-053)

- Symptom: `check-layout.ps1` failed on the empty Actions and Contexts pages: "id names area 'detail', which is not on screen".
- Cause: `GridProbes.Collect` skipped a collapsed element but still walked its children, and a collapsed card's children keep their last size.
- Fix: the walk stops at any collapsed element.
- Rule: anything that measures the visual tree skips the whole subtree of a collapsed element.

## A multi-line TextBox gave its lines back as one (M5, S-045)

- Symptom: Resize Window's Widths field saved nothing when it had two lines.
- Cause: WinUI's TextBox with `AcceptsReturn` stores line breaks as `\r`, and the form split on `\n`.
- Fix: forms split on both `\r` and `\n`.
- Rule: never split TextBox text on `\n` alone.

## An x:Bind path that matched an x:Name crashed the XAML compiler (M5, S-044)

- Symptom: `WMC9999` with no message (the compiler's message resources are missing), from `BindingPathListener.ResolveNameOnRoot` in `obj/.../output.json`.
- Cause: a DataTemplate bound `{x:Bind Detail}` on a page that also had an element named `Detail`.
- Fix: the card is `DetailCard`.
- Rule: a page's x:Names never repeat a property name its templates bind; read `output.json` when WMC9999 says nothing.

## Raw input stopped the keyboard hook (S-055)

Symptom: with keyboard raw input registered (`RIDEV_INPUTSINK`, on the hook's thread or on a thread of its own), the low-level hook saw no injected keys, and every self-test check that types through the hook failed.
Cause: not pinned down; registering raw input for the keyboard in the same process changed what reached the hook.
Fix: no raw input. The watchdog reads `GetLastInputInfo` and, when input went by with no hook call, sends the marked mask key as a probe.
Rule: never register keyboard raw input in the process that holds the low-level hook.
