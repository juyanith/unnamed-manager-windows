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
    }

    public Dictionary<nint, Window> Windows { get; } = [];

    /// The windows front to back, minimized ones too.
    public List<nint> ZOrder { get; } = [];

    public nint? Front { get; set; }
    /// What's in front when no window is: "The desktop".
    public string FrontAppName { get; set; } = "Notepad";

    /// The virtual desktop shown; focusing a window on another one switches to it.
    public int CurrentDesktop { get; set; }

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
    public void Click(nint handle) => Focus(new WindowRef(handle));

    private void Raise(nint handle)
    {
        ZOrder.Remove(handle);
        ZOrder.Insert(0, handle);
    }

    public ForegroundWindow? Foreground() =>
        Front is { } h && Windows.TryGetValue(h, out var w) ? new ForegroundWindow(w.App, new WindowRef(h), w.Elevated) : new ForegroundWindow(FrontAppName, null);

    public WindowFacts? Facts(WindowRef window) =>
        Windows.TryGetValue(window.Handle, out var w) ? new WindowFacts(w.Frame, w.IsMaximized, w.CanMaximize, w.IsFullScreen) : null;

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

    private WindowInfo Info(nint handle) => new(new WindowRef(handle), Windows[handle].Frame, Windows[handle].Owner);

    public IReadOnlyList<WindowInfo> Focusable() =>
        [.. ZOrder.Where(h => Windows.ContainsKey(h) && !Windows[h].IsMinimized && Windows[h].Desktop == CurrentDesktop).Select(Info)];

    public void Focus(WindowRef window)
    {
        if (!Windows.TryGetValue(window.Handle, out var w)) return;
        w.IsMinimized = false;
        CurrentDesktop = w.Desktop;
        Raise(window.Handle);
        Front = window.Handle;
        foreach (var watch in _watchers) watch(window);
    }

    public WindowInfo? Find(WindowRef window) =>
        Windows.TryGetValue(window.Handle, out var w) && !w.IsMinimized ? Info(window.Handle) : null;

    /// A fake app's exe is its name: "Notepad" is Notepad.exe.
    public Config.FrontApp? App(WindowRef window) =>
        Windows.TryGetValue(window.Handle, out var w) ? new Config.FrontApp($@"C:\Apps\{w.App}.exe", null, w.App) : null;

    public void WatchFocus(Action<WindowRef> changed) => _watchers.Add(changed);
}
