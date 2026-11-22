# Fix log

Bugs worth remembering, newest first. Each ends with the rule that prevents it.

## The WPF Guide never spoke to Narrator (S-099)

- Symptom: with Narrator on, the WPF Guide announced nothing: no menu, highlight, Home, failure or Cancelled.
- Cause: `Announce` took the root Grid's automation peer. A WPF Grid has none, and the `?.` after it hid that. The tests checked the words, not that they were sent.
- Fix: it announces through the GuideView's own peer, and both self-tests check there is one.
- Rule: test an announcement as a UI Automation client receives it. Right words prove nothing about delivery.

## Focus that Windows refused looked like success (S-084)

- Symptom: when Windows wouldn't bring a window forward, nothing said so, and typing went to the window still in front.
- Cause: `AppSwitcher.Focus`'s result was ignored, and it's read too soon to trust anyway.
- Fix: the runner waits up to 600 ms for focus to arrive, then fails with "Couldn't focus <App>." and takes the window back out of the history.
- Rule: after asking Windows for the foreground, check that it arrived, with time for a desktop switch.

## A key error outlived the recorder (S-040)

- Symptom: record a key that's taken, then press Esc or pick another row: "E is already used by ..." stayed under that row's key.
- Cause: the line was cleared only when recording started or succeeded.
- Fix: stopping the recorder clears it.
- Rule: an error about what you're typing goes when you stop typing it. Look at every snapshot page at a milestone's end, not only the new ones.

## A list for this display showed every display's windows (S-085)

- Symptom: with the desktop in front, a Recent Windows list set to This display listed the windows of all displays.
- Cause: the display came only from the focused window, and none meant no filter.
- Fix: with none focused, the candidate in front says which display; with no candidate, the list is empty.
- Rule: a scope that can't be resolved never widens to everything.

## A key started a second copy of an app on another desktop (S-080)

- Symptom: with an app's only window on another virtual desktop, its key started the app again, and Only focus said it wasn't running.
- Cause: `WindowPicker` skipped every cloaked window, and the windows there before a start left them out too.
- Fix: both take `Presence`: a window on another desktop counts, a suspended one never. The window used last wins.
- Rule: use `AppSwitcher.PresenceOf`, never DWMWA_CLOAKED alone, to decide whether a window counts.

## The app skipped its own windows as hung (S-080)

- Symptom: about one run in three, Use Application on Unnamed Manager itself didn't find its window and started a second copy.
- Cause: `FindWindow` asks `IsHungAppWindow` on the thread that owns those windows, and Windows can answer hung while that thread runs the query.
- Fix: this process's windows never count as hung.
- Rule: don't ask Windows whether the calling thread is responding. Repeat a new timing-sensitive check before trusting one pass.

## The Guide stayed up under the key recorder (S-079)

- Symptom: click a key field during a sequence and the Guide stayed, showing a sequence that had ended.
- Cause: turning the recorder on resets the engine, which has no effect to hide the Guide.
- Fix: both recorders start through `App.BeginRecording`, which closes an ended sequence as a reload does.
- Rule: whatever resets the engine also closes the Guide.

## Recent Windows listed windows that aren't there (S-078)

- Symptom: on a PC with one virtual desktop, Recent Windows listed windows "on another desktop", and the window used before could be one of them.
- Cause: the shell cloaks a suspended Store app's window just as it does one on another desktop, and cloaked by the shell was taken to mean another desktop.
- Fix: `Presence.Of`: a shell-cloaked window is on another desktop only when IVirtualDesktopManager says so; otherwise it's nowhere you can go.
- Rule: cloaked never says where a window is. Ask the desktop manager, and "doesn't say" isn't a yes.

## Welcome came back after using the WPF app (S-110)

- Symptom: run sequences in WPF, then open the WinUI app, and Welcome shows again.
- Cause: WPF's copy of `App` never saved `RanSequence`, in the settings file both apps share.
- Fix: the first binding a sequence runs saves it in WPF too.
- Rule: when both apps share a setting, both write it.

## Each app showed the other's Run at startup as its own (S-109)

- Symptom: with both apps on a PC, Run at startup was on in the one Windows doesn't start. Turning it off there removed the other's, and a moved exe's value was repaired to whichever app ran next.
- Cause: both apps use the Run value `UnnamedManager`, and only its presence was read.
- Fix: a value naming the other app's exe is off here and never repaired. Only turning this app's switch on replaces it.
- Rule: where the two apps share a setting, check whose it is before showing or changing it.

## Cancel had no glyph in the WPF Guide (S-108)

- Symptom: in a WPF held menu, Cancel's icon space was empty, and every row's glyph was smaller than in WinUI.
- Cause: the port lost Cancel's glyph, an invisible private-use character in the source, and drew glyphs at 16 epx where WinUI's FontIcon uses 20.
- Fix: the glyph is back, written "", and Guide rows have a 20 epx glyph style.
- Rule: write glyphs as escapes, and compare a port's snapshots with the original's side by side.

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
