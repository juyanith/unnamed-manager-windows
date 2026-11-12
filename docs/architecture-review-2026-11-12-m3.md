# Architecture review, end of M3 (2026-11-12)

Survey: no C# file over 330 lines (`AppSwitcher.cs` 323, `Snapshot.cs` 272); no
code-behind over 172; Core has no `Microsoft.UI`. M3's window rules all sit in
Core behind `IWindowSystem`, with two adapters (Win32 and the fake). The one
drifted copy is `MONITORINFO`, in `Win32WindowSystem.cs` and `GuideWindow.cs`.
No fix log yet.

M4 (timing and feedback) is next, so the question is: what change makes it easy?

## Guide schedule in Core  [Strong]

Files: src/UnnamedManager/App.xaml.cs (`OnSequenceEffect`), src/UnnamedManager/Guide/GuideWindow.cs
Signal: before a big feature; logic `dotnet test` can't reach.
Problem: the app project decides when the Guide shows and hides. S-031, S-032,
S-033 and S-036 add a delay, a timeout, a flash and a bell to that decision, and
every one of their acceptance lines is `[unit]`. Today they could only be
checked with real timers in the self-test.
Proposal: a pure `GuideSchedule` in Core/Keyboard, as the Mac has:

```csharp
public abstract record GuideCommand { Show, ShowHome, Hide, Flash, Bell, EndSequence }
public sealed class GuideSchedule(RuntimeSettings runtime, UISettings ui)
{
    public bool IsVisible { get; }
    public long? NextDeadline { get; }                 // ms; the app arms one timer
    public IReadOnlyList<GuideCommand> Handle(SequenceEffect effect, long now);
    public IReadOnlyList<GuideCommand> Tick(long now);
}
```

`App.OnSequenceEffect` becomes an adapter: effect in, commands out, each
command one call on `GuideWindow`.
Benefits: locality (every Guide timing rule in one file), leverage (each M4
story is a few lines plus unit tests with a made-up clock).
Proof of same behavior: unit tests for show, hide and `showHud` false; the
self-test passes the same 97 checks.
Plan: one commit, the schedule with today's behavior only (no delay, no
timeout, no errors); each M4 story then adds its rule.

## One monitor lookup  [Worth exploring]

Files: src/UnnamedManager/Guide/GuideWindow.cs (`Monitor`, `MONITORINFO`), src/UnnamedManager/Actions/Win32WindowSystem.cs
Signal: drifted copy.
Problem: the Guide finds its monitor with its own Win32 calls, and its
center/top/bottom math lives in the window, so S-034 can't unit-test it.
Proposal: `GuidePlacement.Origin(size, Screen screen, string position, double edgePoints)`
in Core, and the Guide takes its `Screen` from `Win32WindowSystem.Screens()`
with `WindowGeometry.ScreenFor`.
Plan: fold it into S-034, which is about exactly this.

## Considered, not recommended

- Splitting `App.xaml.cs` (199 lines): it's the composition root; it shrinks once `OnSequenceEffect` is an adapter.
- Splitting `AppSwitcher.cs` (323 lines): one job (find and focus an app's windows); below the 400-line class signal.

Top recommendation: the Guide schedule in Core, done now.
