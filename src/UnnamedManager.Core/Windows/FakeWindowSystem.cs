namespace UnnamedManager.Core.Windows;

/// Windows and monitors kept in memory, for unit tests and the self-test (S-022).
public sealed class FakeWindowSystem : IWindowSystem
{
    public sealed class Window
    {
        public PixelRect Frame;
        public bool IsMaximized, IsMinimized;
        public bool CanMaximize = true, IsFullScreen;
        /// The frame before the window's own maximize, as Windows keeps it.
        public PixelRect Normal;
        /// A window that won't shrink or grow past these, as an app with a minimum size.
        public (int Width, int Height)? MinSize, MaxSize;
        /// A window that never changes size, such as a dialog.
        public bool IsFixedSize;
        public string App = "Notepad";
        /// Runs as administrator (S-056).
        public bool Elevated;
        /// The process that owns it, for Next and Previous.
        public int Owner = 1;
        /// The virtual desktop it's on (spec v13).
        public int Desktop;
        /// Shows on every desktop.
        public bool OnEveryDesktop;
        /// Windows won't bring it forward (S-084).
        public bool CantFocus;
        /// Focus gets there this many HasFocus checks late.
        public int FocusAfterChecks;
        /// A dialog (spec v21).
        public bool IsDialog;
        /// Disabled by a modal dialog of its own (spec v21).
        public bool Blocked;
        /// What DWM says cloaks it: 0, or 2 for the shell (S-078).
        public int Cloaked;
        /// Windows doesn't say which desktop it's on.
        public bool DesktopUnknown;
        /// Its title; "" uses the app's name.
        public string Title = "";
    }

    public Dictionary<nint, Window> Windows { get; } = [];

    /// The windows front to back, minimized ones too.
    public List<nint> ZOrder { get; } = [];

    public nint? Front { get; set; }
    /// What's in front when no window is: "The desktop".
    public string FrontAppName { get; set; } = "Notepad";

    /// The virtual desktop shown; focusing a window on another one switches to it.
    public int CurrentDesktop { get; set; }
    /// How many virtual desktops there are, from 0 to DesktopCount - 1 here, 1 to DesktopCount to the app.
    public int DesktopCount { get; set; } = 1;
    /// Windows refuses to move windows between desktops, or doesn't say where they are.
    public bool DesktopsUnavailable { get; set; }
    /// How many times the desktop was switched, for tests.
    public int Switches { get; private set; }

    public List<Screen> Monitors { get; } = [new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1032))];

    private readonly List<Action<WindowRef>> _watchers = [];

    /// A new window, in front of the others and focused.
    public Window Add(nint handle, PixelRect frame)
    {
        var window = new Window { Frame = frame, Normal = frame, Desktop = CurrentDesktop };
        Windows[handle] = window;
        Raise(handle);
        Front = handle;
        return window;
    }

    /// Closes a window.
    public void Remove(nint handle)
    {
        Windows.Remove(handle);
        ZOrder.Remove(handle);
        if (Front == handle) Front = null;
    }

    /// Focuses a window as a click or Alt+Tab would: in front, and reported (spec v13).
    public void Click(nint handle) => Arrive(handle);

    private void Raise(nint handle)
    {
        ZOrder.Remove(handle);
        ZOrder.Insert(0, handle);
    }

    public ForegroundWindow? Foreground() =>
        Front is { } h && Windows.TryGetValue(h, out var w) ? new ForegroundWindow(w.App, new WindowRef(h), w.Elevated) : new ForegroundWindow(FrontAppName, null);

    public WindowFacts? Facts(WindowRef window) =>
        Windows.TryGetValue(window.Handle, out var w) ? new WindowFacts(w.Frame, w.IsMaximized, w.CanMaximize, w.IsFullScreen, w.IsDialog) : null;

    public IReadOnlyList<Screen> Screens() => Monitors;

    public void SetFrame(WindowRef window, PixelRect frame)
    {
        if (!Windows.TryGetValue(window.Handle, out var w)) return;
        w.IsMaximized = false;
        var (width, height) = w.IsFixedSize ? (w.Frame.Width, w.Frame.Height) : (frame.Width, frame.Height);
        if (w.MaxSize is { } max) (width, height) = (Math.Min(width, max.Width), Math.Min(height, max.Height));
        if (w.MinSize is { } min) (width, height) = (Math.Max(width, min.Width), Math.Max(height, min.Height));
        w.Frame = w.Normal = new PixelRect(frame.X, frame.Y, width, height);
    }

    public void Maximize(WindowRef window)
    {
        if (!Windows.TryGetValue(window.Handle, out var w)) return;
        w.Normal = w.Frame;
        w.Frame = (WindowGeometry.ScreenFor(w.Frame, Monitors) ?? Monitors[0]).Work;
        w.IsMaximized = true;
    }

    public void Restore(WindowRef window)
    {
        if (!Windows.TryGetValue(window.Handle, out var w)) return;
        w.Frame = w.Normal;
        w.IsMaximized = w.IsMinimized = false;
    }

    public void Minimize(WindowRef window)
    {
        if (Windows.TryGetValue(window.Handle, out var w)) w.IsMinimized = true;
    }

    /// The real system's rule, on what the fake window has (S-078).
    private WindowPresence PresenceOf(Window w) =>
        Presence.Of(w.Cloaked, w.DesktopUnknown ? null : w.OnEveryDesktop || w.Desktop == CurrentDesktop);

    private WindowInfo Info(nint handle) => new(new WindowRef(handle), Windows[handle].Frame, Windows[handle].Owner, Windows[handle].IsDialog, Windows[handle].Blocked);

    public IReadOnlyList<WindowInfo> Focusable() =>
        [.. ZOrder.Where(h => Windows.ContainsKey(h) && !Windows[h].IsMinimized && Windows[h].Cloaked == 0 && (Windows[h].Desktop == CurrentDesktop || Windows[h].OnEveryDesktop)).Select(Info)];

    public void Focus(WindowRef window)
    {
        if (Windows.TryGetValue(window.Handle, out var w) && !w.CantFocus && w.FocusAfterChecks == 0) Arrive(window.Handle);
    }

    public bool HasFocus(WindowRef window)
    {
        if (Windows.TryGetValue(window.Handle, out var w) && w.FocusAfterChecks > 0 && --w.FocusAfterChecks == 0) Arrive(window.Handle);
        return Front == window.Handle;
    }

    private void Arrive(nint handle)
    {
        if (!Windows.TryGetValue(handle, out var w)) return;
        w.IsMinimized = false;
        CurrentDesktop = w.Desktop;
        Raise(handle);
        Front = handle;
        foreach (var watch in _watchers) watch(new WindowRef(handle));
    }

    public WindowInfo? Find(WindowRef window) =>
        Windows.TryGetValue(window.Handle, out var w) && !w.IsMinimized && PresenceOf(w) != WindowPresence.Nowhere ? Info(window.Handle) : null;

    /// A fake app's exe is its name: "Notepad" is Notepad.exe.
    public Config.FrontApp? App(WindowRef window) =>
        Windows.TryGetValue(window.Handle, out var w) ? new Config.FrontApp($@"C:\Apps\{w.App}.exe", null, w.App) : null;

    public void WatchFocus(Action<WindowRef> changed) => _watchers.Add(changed);

    public IReadOnlyList<ListedWindow> Listable() =>
        [.. ZOrder.Where(Windows.ContainsKey).Select(h => (h, w: Windows[h])).Where(p => PresenceOf(p.w) != WindowPresence.Nowhere).Select(p => new ListedWindow(new WindowRef(p.h), p.w.Frame, p.w.Owner,
            p.w.Title, $@"C:\Apps\{p.w.App}.exe", p.w.IsMinimized, PresenceOf(p.w) == WindowPresence.Elsewhere, p.w.IsDialog, p.w.Blocked))];

    public (int Current, int Count)? Desktops() => DesktopsUnavailable ? null : (CurrentDesktop + 1, DesktopCount);

    public DesktopPlace DesktopOf(WindowRef window) => !Windows.TryGetValue(window.Handle, out var w) || DesktopsUnavailable ? new DesktopPlace.Unknown()
        : w.OnEveryDesktop ? new DesktopPlace.Every() : new DesktopPlace.On(w.Desktop + 1, DesktopCount);

    public bool SwitchDesktop(int number)
    {
        if (DesktopsUnavailable || number < 1 || number > DesktopCount) return false;
        CurrentDesktop = number - 1;
        Switches++;
        // Windows focuses the window in front on that desktop, or none.
        Front = ZOrder.FirstOrDefault(h => Windows[h] is { IsMinimized: false } w && (w.Desktop == CurrentDesktop || w.OnEveryDesktop)) is var top and not 0 ? top : null;
        if (Front is { } f) foreach (var watch in _watchers) watch(new WindowRef(f));
        return true;
    }

    public void FocusNone() => Front = null;

    public bool SendToDesktop(WindowRef window, int number)
    {
        if (DesktopsUnavailable || !Windows.TryGetValue(window.Handle, out var w) || number < 1 || number > DesktopCount) return false;
        w.Desktop = number - 1;
        return true;
    }
}
