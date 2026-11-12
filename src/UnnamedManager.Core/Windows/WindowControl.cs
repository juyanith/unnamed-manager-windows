namespace UnnamedManager.Core.Windows;

/// Carries out window commands on the window in front, and keeps what Maximize
/// remembers (S-022). Every rule is here, in Core, so unit tests reach it through
/// `FakeWindowSystem`. `gap` is the points Place Window leaves around windows (spec v3),
/// read each time an action runs; none unless given.
public sealed class WindowControl(IWindowSystem system, Func<double>? gap = null)
{
    private readonly Func<double> _gap = gap ?? (() => 0);

    private readonly MaximizeMemory _maximized = new();

    public IWindowSystem System { get; } = system;

    /// Returns why the command failed, or null.
    public string? Run(WindowCommand command)
    {
        // Next and Previous start from the first window when none is in front.
        if (command is WindowCommand.Focus { Direction: "next" or "previous" } cycle)
            return Cycle(System.Foreground()?.Window, cycle);
        if (System.Foreground() is not { } front) return $"No window is in front to {command.Verb}.";
        if (front.Window is not { } window || System.Facts(window) is not { } facts) return $"{front.AppName} has no window to {command.Verb}.";
        if (facts.IsFullScreen && command is not WindowCommand.Focus) return $"{front.AppName} is full screen. Leave full screen first.";
        if (WindowGeometry.ScreenFor(facts.Frame, System.Screens()) is not { } screen) return null;

        switch (command)
        {
            case WindowCommand.Maximize { Toggle: var toggle }:
                // The window's own maximize keeps Snap and its title bar button in step.
                if (facts.CanMaximize)
                {
                    if (!facts.IsMaximized) System.Maximize(window);
                    else if (toggle) System.Restore(window);
                    return null;
                }
                switch (_maximized.Step(window, facts.Frame, screen, toggle))
                {
                    case MaximizeMemory.Fill:
                        System.SetFrame(window, screen.Work);
                        _maximized.Record(window, facts.Frame, System.Facts(window)?.Frame ?? screen.Work);
                        break;
                    case MaximizeMemory.Restore { Before: var before }:
                        System.SetFrame(window, before);
                        break;
                }
                return null;
            case WindowCommand.Minimize:
                System.Minimize(window);
                return null;
            case WindowCommand.Place { Frame: var fractions }:
                var gapPoints = _gap();
                var target = WindowGeometry.WithGap(WindowGeometry.Rect(fractions, screen.Work), screen, gapPoints);
                System.SetFrame(window, target);
                // A window with a minimum or maximum size keeps the edge it was placed against.
                if (System.Facts(window)?.Frame is { } actual && !WindowGeometry.Matches(actual, target, screen))
                    System.SetFrame(window, WindowGeometry.Anchored((actual.Width, actual.Height), target, WindowGeometry.Inset(screen.Work, screen, gapPoints), screen));
                return null;
            case WindowCommand.Resize { Action: { Change: "step" } step }:
                if (WindowGeometry.Step(facts.Frame, screen, step.Widths ?? Config.ResizeAction.DefaultWidths, step.Step == "grow") is { } next)
                    System.SetFrame(window, next);
                return null;
            case WindowCommand.Resize { Action: var resize }:
                var size = WindowGeometry.Size(facts.Frame, screen, resize);
                System.SetFrame(window, WindowGeometry.Resized(facts.Frame, size, resize.Anchor, screen.Work));
                // A window with a fixed, minimum or maximum size keeps its anchor at the size it took.
                if (System.Facts(window)?.Frame is { } took && (took.Width, took.Height) != size)
                    System.SetFrame(window, WindowGeometry.Resized(facts.Frame, (took.Width, took.Height), resize.Anchor, screen.Work));
                return null;
            case WindowCommand.Focus { Direction: "left" or "right" or "up" or "down" } focus:
                var others = System.Focusable().Where(w => w.Window != window).ToList();
                if (WindowGeometry.Neighbor(facts.Frame, focus.Direction, [.. others.Select(w => w.Frame)]) is not { } index) return focus.Failure;
                System.Focus(others[index].Window);
                return null;
            case WindowCommand.Move { Action: var move }:
                System.SetFrame(window, WindowGeometry.Moved(facts.Frame, screen, move));
                return null;
        }
        return null;
    }

    private string? Cycle(WindowRef? current, WindowCommand.Focus cycle)
    {
        var all = System.Focusable();
        var at = all.Select(w => w.Window).ToList().IndexOf(current ?? default);
        if (WindowGeometry.Step(at < 0 ? null : at, WindowGeometry.CycleOrder(all, System.Screens()), cycle.Direction == "previous") is not { } next)
            return cycle.Failure;
        System.Focus(all[next].Window);
        return null;
    }
}

/// What Maximize does next for a window that can't maximize itself, and the frame it
/// had before. Kept per window, in memory only.
public sealed class MaximizeMemory
{
    public abstract record Next;
    public sealed record Fill : Next;
    public sealed record Restore(PixelRect Before) : Next;
    public sealed record Stay : Next;

    private readonly Dictionary<WindowRef, (PixelRect Before, PixelRect After)> _saved = [];

    /// A window that moved or resized since Maximize forgets its old frame.
    public Next Step(WindowRef window, PixelRect frame, Screen screen, bool toggle)
    {
        var filled = WindowGeometry.Matches(frame, screen.Work, screen);
        if (_saved.Remove(window, out var memory) && (filled || WindowGeometry.Matches(frame, memory.After, screen)))
        {
            if (toggle) return new Restore(memory.Before);
            _saved[window] = memory;
            return new Stay();
        }
        return filled ? new Stay() : new Fill();
    }

    /// The frame from before, and the one the window took, which can differ from the
    /// work area for an app that sizes in steps (a terminal's character grid).
    public void Record(WindowRef window, PixelRect before, PixelRect after) => _saved[window] = (before, after);
}
