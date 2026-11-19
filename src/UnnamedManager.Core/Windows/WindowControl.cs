using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Windows;

/// Carries out window commands on the window in front, and keeps what Fill remembers
/// (S-022) and which windows had focus (spec v13). Every rule is here, in Core, so unit
/// tests reach it through `FakeWindowSystem`. `settings` are the config's window
/// settings (the gap, Arrange Windows' limits, the distances, the apps' rules), read
/// each time an action runs; the defaults with no gap unless given.
public sealed class WindowControl
{
    private readonly Func<WindowSettings> _settings;
    private readonly FillMemory _filled = new();
    private readonly FocusHistory _history = new();

    public WindowControl(IWindowSystem system, Func<WindowSettings>? settings = null)
    {
        System = system;
        _settings = settings ?? (() => new WindowSettings { Gap = 0 });
        system.WatchFocus(_history.Record);
    }

    public IWindowSystem System { get; }

    /// The windows used last, for the self-test.
    public IReadOnlyList<WindowRef> History => _history.Windows;

    private double Gap => Math.Max(0, Math.Round(_settings().Gap, MidpointRounding.AwayFromZero));

    /// Returns why the command failed, or null.
    public WindowFailure? Run(WindowCommand command)
    {
        var front = System.Foreground();
        WindowRef? focused = front?.Window is { } w && System.Facts(w) is not null ? w : null;
        // In case the focus change wasn't reported, as for a window in front before the app started.
        if (focused is { } f) _history.Record(f);
        // Focus Window and Arrange Windows also work with no window in front. Focus only
        // brings another window forward, which Windows allows from an elevated one.
        switch (command)
        {
            case WindowCommand.Focus focus:
                return FocusOn(focus.Target, focus.Scope, focused);
            case WindowCommand.Arrange arrange:
                if (front is { Elevated: true }) return Elevation.Message(front.AppName);
                return Arrange(arrange.Action, focused);
            case WindowCommand.SwitchDesktop { Side: var side }:
                return SwitchDesktop(side, focused);
            case WindowCommand.Choose { Swaps: false, Window: var chosen }:
                if (System.Facts(chosen) is null) return Alert("That window has closed.");
                if (chosen != focused) Go(chosen);
                return null;
            case WindowCommand.SendToDesktop when front is null || focused is null:
                return Alert($"No window is in front to {command.Verb}.");
        }
        if (front is null) return $"No window is in front to {command.Verb}.";
        if (front.Elevated) return Elevation.Message(front.AppName);
        if (front.Window is not { } window || System.Facts(window) is not { } facts) return $"{front.AppName} has no window to {command.Verb}.";
        if (facts.IsFullScreen)
            return new WindowFailure($"{front.AppName} is full screen. Leave full screen first.", Alerts: command is WindowCommand.SendToDesktop);
        switch (command)
        {
            case WindowCommand.SendToDesktop send:
                return SendToDesktop(window, front.AppName, send.Target, send.Follow);
            case WindowCommand.Choose { Window: var partner }:
                // Swap from a list (spec v19): the focus history doesn't change.
                if (IsLeftOut(window, _settings().LeftOut(r => r.Swap)))
                    return new WindowFailure($"{front.AppName} is left out of Swap Window.", Alerts: true);
                if (System.Facts(partner) is not { } other) return Alert("That window has closed.");
                if (partner != window) Trade(window, facts.Frame, partner, other.Frame);
                return null;
        }
        if (WindowGeometry.ScreenFor(facts.Frame, System.Screens()) is not { } screen) return null;

        switch (command)
        {
            case WindowCommand.Fill { Toggle: var toggle }:
                Fill(window, facts, screen, toggle);
                return null;
            case WindowCommand.Minimize:
                System.Minimize(window);
                return null;
            case WindowCommand.Place { Frame: var fractions }:
                Place(window, WindowGeometry.Rect(fractions, screen.Work), screen);
                return null;
            case WindowCommand.Resize { Action: { Change: "step" } step }:
                if (WindowGeometry.Step(facts.Frame, screen, step.Widths ?? ResizeAction.DefaultWidths, step.Step == "grow") is { } next)
                    System.SetFrame(window, next);
                return null;
            case WindowCommand.Resize { Action: var resize }:
                var size = WindowGeometry.Size(facts.Frame, screen, resize);
                System.SetFrame(window, WindowGeometry.Resized(facts.Frame, size, resize.Anchor, screen.Work));
                // A window with a fixed, minimum or maximum size keeps its anchor at the size it took.
                if (System.Facts(window)?.Frame is { } took && (took.Width, took.Height) != size)
                    System.SetFrame(window, WindowGeometry.Resized(facts.Frame, (took.Width, took.Height), resize.Anchor, screen.Work));
                return null;
            case WindowCommand.Move { Action: var move }:
                System.SetFrame(window, WindowGeometry.Moved(facts.Frame, screen, move));
                return null;
            case WindowCommand.Swap swap:
                // Swap Window never moves a left-out app's window, the focused one included (spec v15).
                if (IsLeftOut(window, _settings().LeftOut(r => r.Swap)))
                    return new WindowFailure($"{front.AppName} is left out of Swap Window.", Alerts: true);
                return Swap(window, facts.Frame, swap.Target, swap.Scope);
        }
        return null;
    }

    /// Fill (spec v7): the work area less the gap on every side. With no gap, a window
    /// that can maximize uses its own maximize, which keeps Snap and its title bar
    /// button in step; otherwise the frame is set and the one before is remembered.
    private void Fill(WindowRef window, WindowFacts facts, Screen screen, bool toggle)
    {
        if (Gap == 0 && facts.CanMaximize)
        {
            if (!facts.IsMaximized) System.Maximize(window);
            else if (toggle) System.Restore(window);
            return;
        }
        var area = WindowGeometry.Inset(screen.Work, screen, Gap);
        switch (_filled.Step(window, facts.Frame, area, screen, toggle))
        {
            case FillMemory.Fill:
                Place(window, screen.Work, screen);
                _filled.Record(window, facts.Frame, System.Facts(window)?.Frame ?? area);
                break;
            case FillMemory.Restore { Before: var before }:
                System.SetFrame(window, before);
                break;
        }
    }

    /// Puts a window in `frame` on a screen, with the gap around it. A window with a
    /// minimum or maximum size keeps the edge it was placed against, a gap in.
    private void Place(WindowRef window, PixelRect frame, Screen screen)
    {
        var target = WindowGeometry.WithGap(frame, screen, Gap);
        System.SetFrame(window, target);
        if (System.Facts(window)?.Frame is { } actual && !WindowGeometry.Matches(actual, target, screen))
            System.SetFrame(window, WindowGeometry.Anchored((actual.Width, actual.Height), target, WindowGeometry.Inset(screen.Work, screen, Gap), screen));
    }

    /// Whether a window's app is one of `apps` (exe names or AppUserModelIDs, spec v15).
    private bool IsLeftOut(WindowRef window, IReadOnlyList<string> apps) =>
        apps.Count > 0 && System.App(window) is { } app && apps.Any(app.Is);

    private SeenDistances Distances(PixelRect? anchor)
    {
        var screens = System.Screens();
        var screen = (anchor is { } a ? WindowGeometry.ScreenFor(a, screens) : null) ?? screens.FirstOrDefault() ?? new Screen(default, default);
        return SeenDistances.From(_settings(), screen);
    }

    /// A window Focus Window and Swap Window can pick, and what of it shows.
    private sealed record Candidate(WindowInfo Info, SeenWindow Seen);

    /// Focus Window's candidates (S-028), front to back: the windows you can see, and the
    /// focused one (spec v12), but none of an app whose `rule` in `windows.apps` is false
    /// (spec v15). Windows on every display hide the ones behind them, left-out ones too.
    /// With `"scope": "display"`, only those on the display of `anchor`, or of the window
    /// in front without one (spec v10).
    private List<Candidate> Candidates(string scope, PixelRect? anchor, WindowRef? focused, Func<AppRules, bool> rule)
    {
        var windows = System.Focusable();
        var parts = WindowGeometry.VisibleParts([.. windows.Select(w => w.Frame)]);
        var excluded = _settings().LeftOut(rule);
        var side = Distances(anchor).MinVisible;
        var seen = new List<Candidate>();
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            if (IsLeftOut(window.Window, excluded)) continue;
            if (window.Window != focused && !WindowGeometry.Shows(parts[i], side)) continue;
            seen.Add(new Candidate(window, new SeenWindow(window.Frame, parts[i], window.Owner)));
        }
        if (scope != WindowScope.Display || (anchor ?? seen.FirstOrDefault()?.Info.Frame) is not { } around) return seen;
        var screens = System.Screens();
        var display = WindowGeometry.ScreenFor(around, screens);
        return seen.Where(c => WindowGeometry.ScreenFor(c.Info.Frame, screens) == display).ToList();
    }

    /// The window used before (spec v13): the first in the history that still exists and
    /// isn't the focused one or a left-out app's, on the scope's display. Focus Window's
    /// can be on another virtual desktop, Swap Window's can't. With none, the front-most
    /// other window.
    private WindowInfo? Recent(string scope, PixelRect? anchor, WindowRef? focused, Func<AppRules, bool> rule, bool anyDesktop)
    {
        var here = System.Focusable();
        var screens = System.Screens();
        var excluded = _settings().LeftOut(rule);
        var display = scope == WindowScope.Display && (anchor ?? here.FirstOrDefault()?.Frame) is { } around ? WindowGeometry.ScreenFor(around, screens) : null;
        bool Counts(WindowInfo w) => w.Window != focused && !IsLeftOut(w.Window, excluded)
            && (display is null || WindowGeometry.ScreenFor(w.Frame, screens) == display);
        var onDesktop = here.Select(w => w.Window).ToHashSet();
        var used = _history.Recent(focused, r => (anyDesktop || onDesktop.Contains(r)) && System.Find(r) is { } info && Counts(info));
        return used is { } u ? System.Find(u) : here.FirstOrDefault(Counts);
    }

    /// Focuses a window and puts it first in the history.
    private void Go(WindowRef window)
    {
        System.Focus(window);
        _history.Record(window);
    }

    private static WindowFailure Alert(string message) => new(message, Alerts: true);

    /// Focus Window (S-028, S-029, spec v5, v6, v10 to v13). Finding no window plays the
    /// alert sound (spec v10).
    private WindowFailure? FocusOn(WindowTarget target, string scope, WindowRef? focused)
    {
        var frame = focused is { } f ? System.Facts(f)?.Frame : null;
        if (target.Word == "recent")
        {
            if (Recent(scope, frame, focused, r => r.Focus, anyDesktop: true) is not { } used)
                return Alert(focused is null ? "No window to focus." : target.Failure);
            Go(used.Window);
            return null;
        }
        var candidates = Candidates(scope, frame, focused, r => r.Focus);
        var order = WindowGeometry.CycleOrder([.. candidates.Select(c => c.Seen)], System.Screens(), Distances(frame));
        // First, Last and numbers don't need a focused window, and stay on a window that's already there.
        if (target.IsNumber || target.Word is "first" or "last")
        {
            int? place = target.Word switch
            {
                "first" => order.Count > 0 ? order[0] : null,
                "last" => order.Count > 0 ? order[^1] : null,
                _ => target.Number <= order.Count ? order[target.Number - 1] : null,
            };
            if (place is not { } p)
                return Alert(target.IsNumber && order.Count > 0 ? WindowTarget.PastTheEnd(target.Number, order.Count) : "No window to focus.");
            if (candidates[p].Info.Window != focused) Go(candidates[p].Info.Window);
            return null;
        }
        // With no focused window, any other target goes to the window in front of the others (spec v5).
        if (frame is not { } here)
        {
            if (candidates.Count == 0) return Alert("No window to focus.");
            Go(candidates[0].Info.Window);
            return null;
        }
        if (target.Word is "next" or "previous")
        {
            var at = candidates.FindIndex(c => c.Info.Window == focused);
            if (WindowGeometry.Step(at < 0 ? null : at, order, target.Word == "previous") is not { } next) return Alert(target.Failure);
            Go(candidates[next].Info.Window);
            return null;
        }
        var others = candidates.Where(c => c.Info.Window != focused).ToList();
        if (WindowGeometry.Neighbor(here, target.Word!, [.. others.Select(c => c.Seen)], Distances(frame)) is not { } index)
            return Alert(target.Failure);
        Go(others[index].Info.Window);
        return null;
    }

    /// Swap Window (spec v10): the focused window and the one `target` names trade frames,
    /// and focus stays. The window used before must be on this desktop (spec v13).
    private WindowFailure? Swap(WindowRef window, PixelRect frame, WindowTarget target, string scope)
    {
        if (target.Word == "recent")
        {
            if (Recent(scope, frame, window, r => r.Swap, anyDesktop: false) is not { } used) return Alert(target.Failure);
            Trade(window, frame, used.Window, used.Frame);
            return null;
        }
        var windows = Candidates(scope, frame, window, r => r.Swap);
        switch (WindowGeometry.SwapPartner(target, windows.FindIndex(c => c.Info.Window == window) is var i and >= 0 ? i : null, frame,
                    [.. windows.Select(c => c.Seen)], System.Screens(), Distances(frame)))
        {
            case SwapPick.None { Failure: var failure }:
                return Alert(failure);
            case SwapPick.With { Index: var index }:
                Trade(window, frame, windows[index].Info.Window, windows[index].Info.Frame);
                break;
        }
        return null;
    }

    /// Switch Desktop (spec v17): with no focused window, at the first or last desktop, or
    /// with only one, nothing happens, and that isn't a failure.
    private WindowFailure? SwitchDesktop(string side, WindowRef? focused)
    {
        if (focused is null || System.Desktops() is not var (current, count)) return null;
        var next = side == "left" ? current - 1 : current + 1;
        if (next < 1 || next > count) return null;
        return System.SwitchDesktop(next) ? null : new WindowFailure("Windows didn't switch desktops.");
    }

    /// Send to Desktop (spec v20): puts the focused window on another desktop. With
    /// `follow`, the desktop shown goes there too and the window keeps focus; without, focus
    /// goes to the window used before on this desktop, or to none.
    private WindowFailure? SendToDesktop(WindowRef window, string app, Config.SpaceTarget target, bool follow)
    {
        switch (DesktopPick(target, System.DesktopOf(window), app))
        {
            case DesktopChoice.None { Failure: var failure }:
                return Alert(failure);
            case DesktopChoice.Stay:
                return null;
            case DesktopChoice.To { Number: var number }:
                if (!System.SendToDesktop(window, number)) return Alert($"Windows didn't move {app}'s window to desktop {number}.");
                if (follow)
                {
                    System.SwitchDesktop(number);
                    Go(window);
                    return null;
                }
                var here = System.Focusable().Where(w => w.Window != window).ToList();
                var on = here.Select(w => w.Window).ToHashSet();
                if ((_history.Recent(window, on.Contains) ?? here.FirstOrDefault()?.Window) is { } used) Go(used);
                else System.FocusNone();
                return null;
        }
        return null;
    }

    /// What Send to Desktop does from `place` (spec v20). Left and right don't wrap; a
    /// number past the last desktop fails.
    public static DesktopChoice DesktopPick(Config.SpaceTarget target, DesktopPlace place, string app)
    {
        switch (place)
        {
            case DesktopPlace.Every:
                return new DesktopChoice.None($"{app}'s window is on every desktop.");
            case DesktopPlace.On(var current, var count):
                if (target.Side == "left") return current > 1 ? new DesktopChoice.To(current - 1) : new DesktopChoice.Stay();
                if (target.Side == "right") return current < count ? new DesktopChoice.To(current + 1) : new DesktopChoice.Stay();
                if (target.Number > count) return new DesktopChoice.None($"No desktop {target.Number}. There {(count == 1 ? "is" : "are")} {count}.");
                return target.Number == current ? new DesktopChoice.Stay() : new DesktopChoice.To(target.Number);
            default:
                return new DesktopChoice.None($"Unable to find the desktop of {app}'s window.");
        }
    }

    /// Recent Windows' items (spec v18, v19): the windows used last, most recent first,
    /// then the others front to back, without the focused window, for the list's fields.
    /// `Front` is the focused window, which Cancel shows in a held list.
    public (IReadOnlyList<ListItem> Items, ListItem? Front) RecentWindows(ListSpec list)
    {
        var front = System.Foreground();
        WindowRef? focused = front?.Window is { } w && System.Facts(w) is not null ? w : null;
        if (focused is { } f) _history.Record(f);
        var listed = System.Listable();
        var byWindow = listed.ToDictionary(l => l.Window);
        var screens = System.Screens();
        var focusedFrame = focused is { } ff ? System.Facts(ff)?.Frame : null;
        var display = focusedFrame is { } frame ? WindowGeometry.ScreenFor(frame, screens) : null;
        ListItem Item(ListedWindow l)
        {
            var marks = (l.Minimized ? ItemMarks.Minimized : ItemMarks.None) | (l.OtherDesktop ? ItemMarks.OtherDesktop : ItemMarks.None)
                | (display is not null && WindowGeometry.ScreenFor(l.Frame, screens) != display ? ItemMarks.OtherDisplay : ItemMarks.None);
            var title = l.Title.Length > 0 ? l.Title : System.App(l.Window)?.Name ?? "Window";
            return new ListItem(l.Window.Handle, title, l.App, marks);
        }
        var frontItem = focused is { } fw && byWindow.TryGetValue(fw, out var mine) ? Item(mine) : null;
        // With no focused window there's nothing to swap.
        if (list.Swaps && focused is null) return ([], null);
        var excluded = _settings().LeftOut(list.Swaps ? r => r.Swap : r => r.Focus);
        bool Keep(ListedWindow l) => l.Window != focused && !IsLeftOut(l.Window, excluded)
            && (list.IncludesMinimized || !l.Minimized)
            && (!list.CurrentDesktopOnly || !l.OtherDesktop)
            && (!list.DisplayOnly || display is null || WindowGeometry.ScreenFor(l.Frame, screens) == display);
        var order = _history.Windows.Where(byWindow.ContainsKey).Select(r => byWindow[r]).Concat(listed);
        var items = order.DistinctBy(l => l.Window).Where(Keep).Take(list.Count).Select(Item).ToList();
        return (items, frontItem);
    }

    private void Trade(WindowRef a, PixelRect aFrame, WindowRef b, PixelRect bFrame)
    {
        Put(a, bFrame);
        Put(b, aFrame);
    }

    /// Gives a window another's frame as it is. One that won't take the size keeps the
    /// edge on the work area's edge, a gap in, as with Place Window.
    private void Put(WindowRef window, PixelRect target)
    {
        System.SetFrame(window, target);
        if (WindowGeometry.ScreenFor(target, System.Screens()) is not { } screen) return;
        if (System.Facts(window)?.Frame is { } actual && !WindowGeometry.Matches(actual, target, screen))
            System.SetFrame(window, WindowGeometry.Anchored((actual.Width, actual.Height), target, WindowGeometry.Inset(screen.Work, screen, Gap), screen));
    }

    /// Arrange Windows (spec v8, v9): the windows on the focused window's display, the
    /// focused one first and then front to back, each in the cell `ArrangeGrid` gives it.
    /// With no focused window, the one in front counts as focused. Apps with `"arrange":
    /// false` are never counted or moved (spec v15); when the focused window is one of
    /// theirs, its display is still the one arranged, and the window in front there
    /// counts as focused.
    private WindowFailure? Arrange(ArrangeAction action, WindowRef? focused)
    {
        var settings = _settings();
        var excluded = settings.LeftOut(r => r.Arrange);
        var screens = System.Screens();
        var candidates = System.Focusable().Where(w => !IsLeftOut(w.Window, excluded)).ToList();
        if (((focused is { } f ? System.Facts(f)?.Frame : null) ?? candidates.FirstOrDefault()?.Frame) is not { } anchor
            || WindowGeometry.ScreenFor(anchor, screens) is not { } screen)
            return "No window to arrange.";
        var windows = candidates.Where(w => WindowGeometry.ScreenFor(w.Frame, screens) == screen).ToList();
        if (windows.FindIndex(w => w.Window == focused) is var at and > 0)
        {
            var first = windows[at];
            windows.RemoveAt(at);
            windows.Insert(0, first);
        }
        if (windows.Count == 0) return "No window to arrange.";
        var plan = ArrangeGrid.Plan(action, settings.MaxColumns, settings.MaxRows, screen.Work, [.. windows.Select(w => w.Frame)],
            screen.Pixels(WindowGeometry.TolerancePoints));
        foreach (var (index, cell) in plan)
        {
            var frame = cell.Rect(screen.Work);
            // A window that's in its cell already stays untouched, so the action can be repeated.
            if (!WindowGeometry.Matches(windows[index].Frame, WindowGeometry.WithGap(frame, screen, Gap), screen))
                Place(windows[index].Window, frame, screen);
        }
        return null;
    }
}

/// What Send to Desktop does (spec v20).
public abstract record DesktopChoice
{
    /// Put the window on this desktop, from 1.
    public sealed record To(int Number) : DesktopChoice;
    /// Already there, or at the end for left or right: nothing, and no failure.
    public sealed record Stay : DesktopChoice;
    /// Not possible, with what to say. Plays the alert sound.
    public sealed record None(string Failure) : DesktopChoice;
}

/// What Fill does next for a window it sets the frame of, and the frame it had before.
/// Kept per window, in memory only.
public sealed class FillMemory
{
    public abstract record Next;
    public sealed record Fill : Next;
    public sealed record Restore(PixelRect Before) : Next;
    public sealed record Stay : Next;

    private readonly Dictionary<WindowRef, (PixelRect Before, PixelRect After)> _saved = [];

    /// `area` is the frame Fill gives (the work area less the gap). A window that moved
    /// or resized since Fill forgets its old frame.
    public Next Step(WindowRef window, PixelRect frame, PixelRect area, Screen screen, bool toggle)
    {
        var filled = WindowGeometry.Matches(frame, area, screen);
        if (_saved.Remove(window, out var memory) && (filled || WindowGeometry.Matches(frame, memory.After, screen)))
        {
            if (toggle) return new Restore(memory.Before);
            _saved[window] = memory;
            return new Stay();
        }
        return filled ? new Stay() : new Fill();
    }

    /// The frame from before, and the one the window took, which can differ from the
    /// area for an app that sizes in steps (a terminal's character grid).
    public void Record(WindowRef window, PixelRect before, PixelRect after) => _saved[window] = (before, after);
}
