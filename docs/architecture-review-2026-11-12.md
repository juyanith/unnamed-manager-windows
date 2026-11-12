# Architecture review, end of M1 (2026-11-12)

The check-in before M2, which adds each action type as a family on the registry (S-011 to S-021). No file is over 400 lines; the largest is `SelfTest.cs` (312). Core has no WinUI references. The question asked: what change makes M2's stories easy?

## One file per action family  [Strong]

Files: src/UnnamedManager.Core/Config/ActionFamilies.cs (215 lines, six families), src/UnnamedManager.Core/Config/WindowActions.cs (181 lines, six families)
Signal: before a big feature. Every M2 story adds a writer, a schema part and a plan to one family.
Problem: all the non-window families share one file, so each M2 story edits the same file as the others, and a family's record, reader, writer, schema and plan sit among five other families'. The Mac app keeps one file per family (`Actions/OpenAction.swift`).
Proposal: `src/UnnamedManager.Core/Actions/` holds the registry and `ActionFields` (`ActionRegistry.cs`, `ActionFields.cs`, `ActionPlan.cs`), and one file per family: `UseApplication.cs`, `Open.cs`, `RunCommand.cs`, `SendShortcut.cs`, `Basic.cs`, and `Window/` with one file per window family plus `Fractions.cs`. Namespaces stay `UnnamedManager.Core.Config`, so no caller changes.
Benefits: locality. An M2 story touches its family's file, the runner and its tests. Adding a family is a new file plus one line in `ActionRegistry.Standard`.
Proof of same behavior: a move with no code changes. `dotnet test` (139) and the build pass; the self-test exits 0.
Plan: one commit, files moved, no code changed.

## A performer per plan  [Worth exploring]

Files: src/UnnamedManager/Actions/ActionRunner.cs
Signal: one feature touches more than 6 files. Each M2 type adds a case to `ActionRunner.PerformAsync`.
Problem: the switch grows by one case per type, five or six in M2. That's readable at this size.
Proposal: `interface IPlanPerformer { Task<string?> PerformAsync(ActionPlan plan); }`, registered by plan type, one class per family next to `AppSwitcher`.
Benefits: the runner stops growing, and the self-test can swap in a recording performer.
Why not now: a switch of eight cases is still one screen. Revisit when the self-test needs to record plans instead of running them (Send Shortcut, S-012), which gives the seam a second adapter.

## Split the self-test by area  [Worth exploring]

Files: src/UnnamedManager/SelfTest.cs (312 lines)
Signal: close to the 300-line mark for code that grows each story.
Proposal: `SelfTest` keeps `Check`, `Find` and the run order; each area (`SelfTest.Config.cs`, `.Keys.cs`, `.Guide.cs`, `.Apps.cs`, `.Tray.cs`) is a partial class file.
Why not now: it's test code with one caller. Do it when the next story would take it past 400.

## Top recommendation

One file per action family: Strong, mechanical, and it pays off in every M2 story. Done in this check-in.

## Considered, not recommended

- An interface for `AppSwitcher` (window enumeration): only one adapter until the window actions in M3 need a fake window system, as the Mac app's `FakeWindowSystem`. Add it with S-022.
- Moving `KeyboardHook` into Core: it's Win32 and threads; the tracker it calls is already in Core and unit-tested.
- Replacing `ConfigStore` with `ConfigState`: `ConfigStore` adds the file, the starter and the change event; deleting it would spread those to `App` and the self-test.
