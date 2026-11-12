namespace UnnamedManager.Core.Windows;

/// Carries out window commands on the window in front, and keeps what Maximize
/// remembers (S-022). Every rule is here, in Core, so unit tests reach it through
/// `FakeWindowSystem`.
public sealed class WindowControl(IWindowSystem system)
{
    private readonly MaximizeMemory _maximized = new();

    public IWindowSystem System { get; } = system;

    /// Returns why the command failed, or null.
    public string? Run(WindowCommand command)
    {
        if (System.Foreground() is not { } front) return $"No window is in front to {command.Verb}.";
        if (front.Window is not { } window || System.Facts(window) is not { } facts) return $"{front.AppName} has no window to {command.Verb}.";
        if (facts.IsFullScreen) return $"{front.AppName} is full screen. Leave full screen first.";
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
                var target = WindowGeometry.Rect(fractions, screen.Work);
                System.SetFrame(window, target);
                // A window with a minimum or maximum size keeps the edge it was placed against.
                if (System.Facts(window)?.Frame is { } actual && !WindowGeometry.Matches(actual, target, screen))
                    System.SetFrame(window, WindowGeometry.Anchored((actual.Width, actual.Height), target, screen.Work, screen));
                return null;
            case WindowCommand.Resize { Action: { Change: "step" } step }:
                if (WindowGeometry.Step(facts.Frame, screen, step.Widths ?? Config.ResizeAction.DefaultWidths, step.Step == "grow") is { } next)
                    System.SetFrame(window, next);
                return null;
            case WindowCommand.Move { Action: var move }:
                System.SetFrame(window, WindowGeometry.Moved(facts.Frame, screen, move));
                return null;
        }
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
