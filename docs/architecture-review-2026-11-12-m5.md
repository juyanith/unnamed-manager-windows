# Architecture check-in, end of M5 (2026-11-12)

M5 added the Bindings window: five pages, a draft editor in Core with undo, the config writer and watcher, the key recorder, action and context forms, and the Guide's glyph column. Core grew `ConfigWriter`, `ConfigEdits`, `ConfigEditor`, `ActionForms`, `ActionGlyphs`, `SettingsValues`; the app grew `Pages/` (about 1,400 lines) and `SelfTest.Bindings.cs`.

Proof of behavior for any refactor below: 480 unit tests, 182 self-test checks, every snapshot page, 40 layout files PASS, grid-lint PASS.

## The Bindings window's self-test in one file  [Strong]

Files: src/UnnamedManager/SelfTest.Bindings.cs (586 lines)
Signal: a C# file near 600 lines; every M5 story added to it.
Problem: tree, save, recorder, actions, contexts, settings and help checks share one file, so finding a page's checks means scrolling past the others.
Proposal: one partial file per page, as the other areas already have: `SelfTest.Bindings.cs` (tree, save, new, key, delete, move, edit, errors, commands), `SelfTest.ActionsPage.cs` (actions, window forms), `SelfTest.Contexts.cs`, `SelfTest.Settings.cs` (settings, help).
Benefits: locality; each page's checks sit in one place.
Plan: one commit that moves methods only.

## "Can edit" lives on the Bindings page  [Strong]

Files: Pages/BindingsPage.xaml.cs, ActionsPage, ContextsPage, SettingsPage
Signal: three pages reach into another page's static for a rule that isn't about that page.
Problem: `BindingsPage.CanEdit` (edits wait while the file has errors) is the window's rule, not one page's.
Proposal: `App.CanEdit`, next to `App.Editor`.
Plan: one commit; callers move over.

## Editor pages repeat their scaffolding  [Worth exploring]

Files: ActionsPage.xaml.cs (212), ContextsPage.xaml.cs (206), BindingsPage partials
Problem: Undo and Redo handlers, the selected-id tracking across refreshes, the "in use" InfoBar, and Loaded and Unloaded wiring to `App.Editor.Changed` are written three times.
Proposal: a small `EditorPageSupport` (wire, unwire, undo, redo, keep a selection by id) the pages compose, not a base class, since WinUI pages are partial classes of `Page`.
Benefits: the next page (M6's Welcome or Startup) gets them for free.

## Window forms inside ActionForms  [Worth exploring]

Files: src/UnnamedManager.Core/Config/ActionForms.cs (241 lines)
Problem: generic field forms and the Place, Move and Resize converters share one file.
Proposal: `WindowForms.cs` with `WindowValues`, `WindowJson` and the presets; `ActionForms` asks it first.

## Snapshot pages as special cases  [Worth exploring]

Files: src/UnnamedManager/Snapshot.cs (321 lines)
Problem: the page loop grows an `if (page.StartsWith(...))` per page family (empty-, bindings-, actions-, settings-end).
Proposal: a table of page id to (window page, setup, teardown).

## Considered, not recommended

- A view model per page: the pages are thin over Core's `ConfigEditor`, which already holds the state and the rules; a model layer would forward calls.
- Moving `AppIcons` and `StartApps` to Core: they're Win32 shell calls, which Core must not have.

## Top recommendation

Do both Strong cards now: they move code without changing behavior. Leave the others for the user to pick.
