# Architecture review, end of M2 (2026-11-12)

The check-in before M3, which makes the window families do what they say (S-022 to S-030). Core has no WinUI references. The largest file was `SelfTest.cs` (420 lines); every other file is under 330. The question asked: what change makes M3's stories easy?

## Split the self-test by area  [Strong]

Files: src/UnnamedManager/SelfTest.cs (420 lines)
Signal: past the 400 lines the end-of-M1 review set as the trigger; M3 adds window checks to it.
Problem: every story adds its checks to the same file, among nine other areas.
Proposal: `SelfTest` becomes a `static partial class`. `SelfTest.cs` keeps the starter checks, the run order, `Check` and `Find`; `SelfTest.Keys.cs`, `.Guide.cs`, `.Actions.cs` and `.Tray.cs` hold their areas. M3 adds `SelfTest.Windows.cs`.
Benefits: locality. A story touches its area's file.
Proof of same behavior: a move with no code changes. The self-test's PASS lines are identical before and after (82 checks).
Plan: one commit. Done in this check-in.

## A window system seam in Core  [Strong, built with S-022]

Files: new; src/UnnamedManager/Actions/AppSwitcher.cs (323 lines) holds today's window enumeration.
Signal: a dependency can't be replaced in a test. Every M3 story needs windows and monitors that a test controls.
Problem: placing, moving and focusing windows needs real windows, monitors at several scales and sides, and minimum sizes, which the self-test can't arrange on this PC.
Proposal: as the Mac's `WindowSystem` and `WindowControl`, but with the control in Core so `dotnet test` reaches it:

```csharp
public interface IWindowSystem
{
    WindowRef? Foreground();                 // the window in front, never the Guide
    WindowFacts? Facts(WindowRef w);         // visible frame, state, can maximize, full screen, elevated
    IReadOnlyList<Screen> Screens();         // work area and scale, primary first
    void SetFrame(WindowRef w, PixelRect visible);   // restores first; adds the invisible border back
    void Maximize(WindowRef w); void Restore(WindowRef w); void Minimize(WindowRef w);
    IReadOnlyList<WindowInfo> Focusable();   // Alt+Tab windows on this desktop, not cloaked
    void Focus(WindowRef w);
}
```

`WindowControl.Run(WindowCommand)` holds every rule and returns a failure or null; `WindowGeometry` holds the math. `Win32WindowSystem` in the app and `FakeWindowSystem` in Core (for unit tests and the self-test) are the two adapters.
Benefits: leverage. Every M3 acceptance line on geometry, mixed scales, negative coordinates and minimum sizes is a unit test. The runner gets one case for every window family.
Proof of same behavior: new code; nothing existing changes.
Plan: the first commit of S-022.

## A performer per plan  [Worth exploring]

Files: src/UnnamedManager/Actions/ActionRunner.cs
Signal: the runner's switch grows per type.
Why not now: M3's six window families share one plan (`ActionPlan.Window`) and one case. Still under a screen.

## Top recommendation

Split the self-test: Strong and mechanical, done in this check-in. The window system seam is the base of S-022 and lands with it.

## Considered, not recommended

- Moving `AppSwitcher`'s enumeration onto the window system now: Use Application works and is tested; move it when Focus Window (S-028) needs the same Alt+Tab filter, so both share one list.
