namespace UnnamedManager.Core.Windows;

/// One top-level window. Equal for the same window across calls, so Maximize can remember it.
public readonly record struct WindowRef(nint Handle);

/// What the window actions need to know about a window. `Frame` is the visible frame
/// (DWM's extended frame bounds), without Windows' invisible resize borders.
public sealed record WindowFacts(PixelRect Frame, bool IsMaximized = false, bool CanMaximize = true, bool IsFullScreen = false);

/// A window Focus Window can go to: its visible frame and the process that owns it.
public sealed record WindowInfo(WindowRef Window, PixelRect Frame, int Owner);

/// The window in front and the app it belongs to. `Window` is null when nothing a window
/// action can change is in front: the desktop, the taskbar, or no window at all.
public sealed record ForegroundWindow(string AppName, WindowRef? Window);

/// The seam between the window actions and the windows on screen (S-022). The app's
/// `Win32WindowSystem` changes real windows; `FakeWindowSystem` keeps them in memory
/// for unit tests and the self-test.
public interface IWindowSystem
{
    /// The window in front, never the Guide. Null when no window is in front.
    ForegroundWindow? Foreground();

    /// Null for a window that's gone.
    WindowFacts? Facts(WindowRef window);

    /// Every monitor, the primary first.
    IReadOnlyList<Screen> Screens();

    /// Puts the window's visible frame at `frame`, restoring it first when it's maximized.
    /// Best effort: a window with a minimum or maximum size ends as close as it allows.
    void SetFrame(WindowRef window, PixelRect frame);

    /// The window's own maximize and restore, as its title bar buttons do them.
    void Maximize(WindowRef window);
    void Restore(WindowRef window);

    /// Sends the window to the taskbar; Windows activates the next window.
    void Minimize(WindowRef window);

    /// The windows Alt+Tab shows on this virtual desktop that aren't minimized: visible,
    /// not cloaked, not tool windows, and not this app's own (S-028).
    IReadOnlyList<WindowInfo> Focusable();

    /// Brings the window to the front with focus.
    void Focus(WindowRef window);
}

/// What a window action does to the window in front (spec v2). The window families plan
/// one, and `WindowControl` carries it out.
public abstract record WindowCommand
{
    /// The verb in "Notepad has no window to maximize."
    public abstract string Verb { get; }

    public sealed record Maximize(bool Toggle) : WindowCommand
    {
        public override string Verb => "maximize";
    }

    public sealed record Minimize : WindowCommand
    {
        public override string Verb => "minimize";
    }

    public sealed record Place(Config.FractionFrame Frame) : WindowCommand
    {
        public override string Verb => "place";
    }

    public sealed record Resize(Config.ResizeAction Action) : WindowCommand
    {
        public override string Verb => "resize";
    }

    public sealed record Move(Config.MoveAction Action) : WindowCommand
    {
        public override string Verb => "move";
    }

    public sealed record Focus(string Direction) : WindowCommand
    {
        public override string Verb => "focus from";

        /// How Focus Window says there's nothing that way.
        public string Failure => Direction switch
        {
            "left" => "No window to the left.",
            "right" => "No window to the right.",
            "up" => "No window above.",
            "down" => "No window below.",
            _ => "No other window to focus.",
        };
    }
}
