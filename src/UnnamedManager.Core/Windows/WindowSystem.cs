namespace UnnamedManager.Core.Windows;

/// One top-level window. Equal for the same window across calls, so Fill and the focus history can remember it.
public readonly record struct WindowRef(nint Handle);

/// What the window actions need to know about a window. `Frame` is the visible frame
/// (DWM's extended frame bounds), without Windows' invisible resize borders.
public sealed record WindowFacts(PixelRect Frame, bool IsMaximized = false, bool CanMaximize = true, bool IsFullScreen = false, bool IsDialog = false);

/// A window the actions can pick: its visible frame and the process that owns it. A dialog
/// is never swapped or arranged; one a dialog blocks is never focused (spec v21).
public sealed record WindowInfo(WindowRef Window, PixelRect Frame, int Owner, bool IsDialog = false, bool Blocked = false);

/// The window in front and the app it belongs to. `Window` is null when nothing a window
/// action can change is in front: the desktop, the taskbar, or no window at all.
/// `Elevated` when it runs as administrator and this app doesn't (S-056).
public sealed record ForegroundWindow(string AppName, WindowRef? Window, bool Elevated = false);

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

    /// The standard windows and dialogs (spec v21) on this virtual desktop that aren't
    /// minimized: visible, not cloaked, and not this app's own (S-028). Front to back.
    IReadOnlyList<WindowInfo> Focusable();

    /// Brings the window to the front with focus, switching to its virtual desktop when
    /// Windows lets it.
    void Focus(WindowRef window);

    /// A window seen before, when it still exists and isn't minimized, on any virtual
    /// desktop (spec v13). Null otherwise.
    WindowInfo? Find(WindowRef window);

    /// The app a window belongs to, which `windows.apps` names (spec v15).
    Config.FrontApp? App(WindowRef window);

    /// Calls `changed` with each window that gets focus, in any app, from now on (spec v13).
    void WatchFocus(Action<WindowRef> changed);

    /// Every window Recent Windows could list, on any virtual desktop, minimized ones
    /// included, front to back (spec v18). Not this app's own.
    IReadOnlyList<ListedWindow> Listable();

    /// The current virtual desktop and how many there are, or null when Windows doesn't say.
    (int Current, int Count)? Desktops();

    /// Which desktop a window is on (spec v20).
    DesktopPlace DesktopOf(WindowRef window);

    /// Shows desktop `number`, from 1, as quickly as Windows allows. False when it can't.
    bool SwitchDesktop(int number);

    /// Puts the window on desktop `number`, from 1. False when Windows refuses.
    bool SendToDesktop(WindowRef window, int number);

    /// Takes focus from every window, as clicking the desktop does, so typing goes nowhere (spec v20).
    void FocusNone();
}

/// A window Recent Windows can list (spec v18): its frame, its title, its app's exe
/// path, and whether it's minimized or on another virtual desktop.
public sealed record ListedWindow(WindowRef Window, PixelRect Frame, int Owner, string Title, string? App, bool Minimized, bool OtherDesktop,
    bool IsDialog = false, bool Blocked = false);

/// Why a window action failed, and whether the alert sound goes with it (spec v10:
/// only when Focus Window or Swap Window finds no window).
public sealed record WindowFailure(string Message, bool Alerts = false)
{
    public static implicit operator WindowFailure(string message) => new(message);
}

/// What a window action does to the window in front (spec v2). The window families plan
/// one, and `WindowControl` carries it out.
public abstract record WindowCommand
{
    /// The verb in "Notepad has no window to fill."
    public abstract string Verb { get; }

    public sealed record Fill(bool Toggle) : WindowCommand
    {
        public override string Verb => "fill";
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

    public sealed record Focus(WindowTarget Target, string Scope = WindowScope.All) : WindowCommand
    {
        public Focus(string word, string scope = WindowScope.All) : this(WindowTarget.Of(word), scope) { }

        public override string Verb => "focus from";
    }

    public sealed record Swap(WindowTarget Target, string Scope = WindowScope.All) : WindowCommand
    {
        public Swap(string word, string scope = WindowScope.All) : this(WindowTarget.Of(word), scope) { }

        public override string Verb => "swap";
    }

    public sealed record Arrange(Config.ArrangeAction Action) : WindowCommand
    {
        public override string Verb => "arrange";
    }

    /// Switch Desktop (spec v17): "left" or "right".
    public sealed record SwitchDesktop(string Side) : WindowCommand
    {
        public override string Verb => "switch desktops from";
    }

    /// Send to Desktop (spec v20).
    public sealed record SendToDesktop(Config.SpaceTarget Target, bool Follow) : WindowCommand
    {
        public override string Verb => "send to another desktop";
    }

    /// Choosing an item of Recent Windows (spec v18, v19): focus its window, or swap with it.
    public sealed record Choose(WindowRef Window, bool Swaps) : WindowCommand
    {
        public override string Verb => Swaps ? "swap" : "focus";
    }
}

/// Where a window is among the virtual desktops (spec v20).
public abstract record DesktopPlace
{
    /// On desktop `Number` of `Count`, from 1, in Task View's order.
    public sealed record On(int Number, int Count) : DesktopPlace;
    /// Set to show on every desktop.
    public sealed record Every : DesktopPlace;
    /// Windows didn't say.
    public sealed record Unknown : DesktopPlace;
}
