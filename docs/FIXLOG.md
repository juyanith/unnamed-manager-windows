# Fix log

Bugs worth remembering, newest first. Each ends with the rule that prevents it.

## The WPF Guide stayed up after a reload (S-107)

- Symptom: save the config during a sequence in the WPF app and the Guide stayed, showing a sequence that had ended.
- Cause: WPF's `App` is a copy of WinUI's from before 0.5.1, so it never got `CloseEndedSequence`. Merging main can't fix a copy.
- Fix: WPF's `App` closes an ended sequence the same way, queued after the keys already pressed.
- Rule: a fix to WinUI's `App`, Guide or tray menu goes into WPF's copy in the same story, with a check in both self-tests.

## A row got another row's title (0.5.1, S-041, S-052)

- Symptom: press Down in the Bindings tree, Tab through Title, and the row you moved to took the title of the one before. A title with a line break lost all but its first line, and Id boxes renamed the same way.
- Cause: the card followed `ItemInvoked`, which the arrow keys and screen readers don't raise, while edits went to `SelectedNode`. The boxes also saved on losing focus whether edited or not.
- Fix: the card follows `SelectionChanged`; a box saves only text you changed, and a title goes to the row it was typed for.
- Rule: show from `SelectionChanged`, never only `ItemInvoked`. The tree can raise it late, so note the row shown before touching a control. A LostFocus handler saves only an edit.

## An action's form deleted what it couldn't show (0.5.1, S-044, S-045)

- Symptom: tabbing through Run Command saved only the program, without its arguments; Resize Window kept one width; 1/3 became 0.3333 and window 150 became 99.
- Cause: the TextBox got its `Text` before `AcceptsReturn`, so it cut at the first line break. The form also rebuilt the whole action from what was on screen, rounded and clamped.
- Fix: `Text` is set last; `ActionForms.Build` takes the action the form was filled from and keeps every unchanged part as written; values that can't be shown are read-only.
- Rule: set `AcceptsReturn` before `Text`. A form never writes back what it only displayed: test that showing an action and leaving every field changes nothing.

## Picking a type replaced a Context Aware action (0.5.1, S-044)

- Symptom: a Context Aware action showed a blank form, and picking any type saved over its variants.
- Cause: the note saying to edit it in the file was cleared right after it was set, and the Type list stayed on.
- Fix: a note of its own, and the Type list is off for an action with no form.
- Rule: a type without a form is read-only in the editor, and a self-test opens one.

## The Guide outlived its sequence after a reload (0.5.1, S-048)

- Symptom: saving the config during a sequence left the Guide up with no sequence behind it.
- Cause: the new config reset the engine, which sends no effect, so `GuideSchedule` and the window never heard.
- Fix: `App.CloseEndedSequence`, queued behind the effects already on their way to the UI thread.
- Rule: whatever resets the engine from outside also resets the schedule and the Guide.

## New binding went inside a list (0.5.1, S-039, S-075)

- Symptom: New binding on a Recent windows menu made an invalid entry under it.
- Cause: a list has no action, so it counted as a menu to add to; the first free key could also be a held menu's repeat key.
- Fix: new entries go beside a list, and `FreePath` skips the repeat key.
- Rule: `IsMenu` doesn't mean "takes children"; check `List` too. A key the app picks passes the same rules as one you record.

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
