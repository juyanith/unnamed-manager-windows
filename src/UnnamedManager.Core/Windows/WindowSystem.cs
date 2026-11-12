namespace UnnamedManager.Core.Windows;

/// One top-level window. Equal for the same window across calls, so Maximize can remember it.
public readonly record struct WindowRef(nint Handle);

/// What the window actions need to know about a window. `Frame` is the visible frame
/// (DWM's extended frame bounds), without Windows' invisible resize borders.
public sealed record WindowFacts(PixelRect Frame, bool IsMaximized = false, bool CanMaximize = true, bool IsFullScreen = false);

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
}
