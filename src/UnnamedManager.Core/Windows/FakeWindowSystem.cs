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
        /// The process that owns it, for Next and Previous.
        public int Owner = 1;
    }

    public Dictionary<nint, Window> Windows { get; } = [];
    public nint? Front { get; set; }
    /// What's in front when no window is: "The desktop".
    public string FrontAppName { get; set; } = "Notepad";

    public List<Screen> Monitors { get; } = [new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1032))];

    public Window Add(nint handle, PixelRect frame)
    {
        var window = new Window { Frame = frame, Normal = frame };
        Windows[handle] = window;
        Front = handle;
        return window;
    }

    public ForegroundWindow? Foreground() =>
        Front is { } h && Windows.TryGetValue(h, out var w) ? new ForegroundWindow(w.App, new WindowRef(h)) : new ForegroundWindow(FrontAppName, null);

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

    public IReadOnlyList<WindowInfo> Focusable() =>
        [.. Windows.Where(p => !p.Value.IsMinimized).Select(p => new WindowInfo(new WindowRef(p.Key), p.Value.Frame, p.Value.Owner))];

    public void Focus(WindowRef window) => Front = window.Handle;
}
