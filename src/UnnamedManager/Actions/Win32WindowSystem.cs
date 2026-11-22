using System.Runtime.InteropServices;
using UnnamedManager.Core.Windows;
using UnnamedManager.UI;

namespace UnnamedManager.Actions;

/// The real windows on screen (S-022). Frames are visible frames in physical pixels:
/// DWM's extended frame bounds, without the invisible resize borders, which are added
/// back for SetWindowPos so placed windows line up with no gap or overlap.
public sealed partial class Win32WindowSystem(Func<nint>? foreground = null) : IWindowSystem
{
    private readonly Func<nint> _foreground = foreground ?? Win32.GetForegroundWindow;

    public ForegroundWindow? Foreground()
    {
        var hwnd = _foreground();
        if (hwnd == 0) return null;
        hwnd = GetAncestor(hwnd, GA_ROOT);
        switch (ClassName(hwnd))
        {
            case "Progman" or "WorkerW": return new ForegroundWindow("The desktop", null);
            case "Shell_TrayWnd" or "Shell_SecondaryTrayWnd": return new ForegroundWindow("The taskbar", null);
        }
        var name = AppSwitcher.FrontAppOf(hwnd)?.Name ?? "The app in front";
        return new ForegroundWindow(name, Win32.IsWindowVisible(hwnd) && !IsIconic(hwnd) ? new WindowRef(hwnd) : null, Platform.ProcessElevation.Above(hwnd));
    }

    public WindowFacts? Facts(WindowRef window)
    {
        var hwnd = window.Handle;
        if (!IsWindow(hwnd) || VisibleFrame(hwnd) is not { } frame) return null;
        var style = GetWindowLongPtrW(hwnd, GWL_STYLE);
        var canMaximize = (style & WS_MAXIMIZEBOX) != 0 && (style & WS_THICKFRAME) != 0;
        return new WindowFacts(frame, IsZoomed(hwnd), canMaximize, IsFullScreen(hwnd, style), AppSwitcher.KindOf(hwnd) == WindowKind.Dialog);
    }

    /// Borderless and covering its whole monitor, as a game or a browser in F11.
    private static bool IsFullScreen(nint hwnd, nint style)
    {
        if ((style & WS_CAPTION) == WS_CAPTION || !Win32.GetWindowRect(hwnd, out var r)) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    public IReadOnlyList<Screen> Screens()
    {
        var screens = new List<(Screen Screen, bool Primary)>();
        var handle = GCHandle.Alloc(screens);
        try { unsafe { EnumDisplayMonitors(0, 0, &VisitMonitor, GCHandle.ToIntPtr(handle)); } }
        finally { handle.Free(); }
        return screens.OrderByDescending(s => s.Primary).Select(s => s.Screen).ToList();
    }

    [UnmanagedCallersOnly]
    private static int VisitMonitor(nint monitor, nint hdc, nint rect, nint state)
    {
        var handle = GCHandle.FromIntPtr(state);
        var screens = (List<(Screen, bool)>)handle.Target!;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfoW(monitor, ref info))
        {
            GetDpiForMonitor(monitor, 0, out var dpi, out _);
            screens.Add((new Screen(Rect(info.rcMonitor), Rect(info.rcWork), dpi / 96.0), (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
        }
        return 1;
    }

    public void SetFrame(WindowRef window, PixelRect frame)
    {
        var hwnd = window.Handle;
        if (IsZoomed(hwnd) || IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        // A window that lands on a monitor with another scale resizes itself on
        // WM_DPICHANGED: move it there first, then set its size with that monitor's borders.
        var before = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var target = MonitorFromRect(Win32Rect(frame), MONITOR_DEFAULTTONEAREST);
        if (before != target) Place(hwnd, frame, SWP_NOSIZE);
        Place(hwnd, frame, 0);
    }

    private static void Place(nint hwnd, PixelRect frame, uint flags)
    {
        if (!Win32.GetWindowRect(hwnd, out var outer) || VisibleFrame(hwnd) is not { } visible) return;
        int left = visible.X - outer.Left, top = visible.Y - outer.Top, right = outer.Right - visible.Right, bottom = outer.Bottom - visible.Bottom;
        SetWindowPos(hwnd, 0, frame.X - left, frame.Y - top, frame.Width + left + right, frame.Height + top + bottom,
            flags | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public IReadOnlyList<WindowInfo> Focusable()
    {
        var list = new List<WindowInfo>();
        var me = (uint)Environment.ProcessId;
        AppSwitcher.EnumTopLevel(hwnd =>
        {
            if (!AppSwitcher.IsCandidate(hwnd) || IsIconic(hwnd)) return true;
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == me || ClassName(hwnd) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (VisibleFrame(hwnd) is { Width: > 0, Height: > 0 } frame) list.Add(Info(hwnd, frame, (int)pid));
            return true;
        });
        return list;
    }

    /// Windows switches to the window's virtual desktop when it takes the foreground there.
    public void Focus(WindowRef window) => AppSwitcher.Focus(window.Handle);

    public bool HasFocus(WindowRef window) => AppSwitcher.HasFocus(window.Handle);

    /// A window seen before, on any virtual desktop (spec v13): it exists, shows, isn't
    /// minimized, and is on this desktop or one Windows says it's on (S-078).
    public WindowInfo? Find(WindowRef window)
    {
        var hwnd = window.Handle;
        if (!IsWindow(hwnd) || !Win32.IsWindowVisible(hwnd) || IsIconic(hwnd)) return null;
        if (AppSwitcher.PresenceOf(hwnd) == WindowPresence.Nowhere) return null;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId || VisibleFrame(hwnd) is not { Width: > 0, Height: > 0 } frame) return null;
        return AppSwitcher.IsSwitchable(hwnd) ? Info(hwnd, frame, (int)pid) : null;
    }

    private static WindowInfo Info(nint hwnd, PixelRect frame, int owner) =>
        new(new WindowRef(hwnd), frame, owner, AppSwitcher.KindOf(hwnd) == WindowKind.Dialog, AppSwitcher.IsBlocked(hwnd));

    public Core.Config.FrontApp? App(WindowRef window) => AppSwitcher.FrontAppOf(window.Handle);

    private static readonly List<Action<WindowRef>> Watchers = [];

    public void WatchFocus(Action<WindowRef> changed) => Watchers.Add(changed);

    /// The foreground changed (ForegroundWatcher, on the UI thread): tells the watchers
    /// about a window a window action could pick, not the desktop or the taskbar.
    public static void Focused(nint hwnd)
    {
        if (hwnd == 0) return;
        hwnd = GetAncestor(hwnd, GA_ROOT);
        if (!AppSwitcher.IsCandidate(hwnd) || ClassName(hwnd) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return;
        foreach (var watch in Watchers) watch(new WindowRef(hwnd));
    }

    /// Every window someone would switch to, on any virtual desktop, minimized ones too,
    /// front to back (spec v18): as Focusable, but a window on another desktop counts. A
    /// suspended Store app's, cloaked the same way, doesn't (S-078).
    public IReadOnlyList<ListedWindow> Listable()
    {
        var list = new List<ListedWindow>();
        var me = (uint)Environment.ProcessId;
        AppSwitcher.EnumTopLevel(hwnd =>
        {
            if (!Win32.IsWindowVisible(hwnd) || !AppSwitcher.IsSwitchable(hwnd)) return true;
            var presence = AppSwitcher.PresenceOf(hwnd);
            if (presence == WindowPresence.Nowhere) return true;
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == me || ClassName(hwnd) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            var minimized = IsIconic(hwnd);
            var frame = minimized ? NormalFrame(hwnd) : VisibleFrame(hwnd);
            if (frame is not { Width: > 0, Height: > 0 } f) return true;
            var otherDesktop = presence == WindowPresence.Elsewhere;
            list.Add(new ListedWindow(new WindowRef(hwnd), f, (int)pid, Title(hwnd), AppSwitcher.AppPath(hwnd), minimized, otherDesktop,
                AppSwitcher.KindOf(hwnd) == WindowKind.Dialog, AppSwitcher.IsBlocked(hwnd)));
            return true;
        });
        return list;
    }

    public (int Current, int Count)? Desktops() => VirtualDesktops.Desktops();

    public DesktopPlace DesktopOf(WindowRef window) => VirtualDesktops.DesktopOf(window.Handle);

    public bool SwitchDesktop(int number) => VirtualDesktops.Switch(number, Desktops()?.Current ?? 0);

    public bool SendToDesktop(WindowRef window, int number) => VirtualDesktops.Send(window.Handle, number);

    /// Clicking the desktop: the shell's desktop window takes the foreground.
    public void FocusNone()
    {
        if (GetShellWindow() is var shell and not 0) AppSwitcher.Focus(shell);
    }

    private static string Title(nint hwnd)
    {
        Span<char> buffer = stackalloc char[512];
        var length = GetWindowTextW(hwnd, buffer, buffer.Length);
        return new string(buffer[..Math.Max(0, length)]).Trim();
    }

    /// A minimized window's frame when restored, in screen pixels.
    private static PixelRect? NormalFrame(nint hwnd)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        return GetWindowPlacement(hwnd, ref placement) ? Rect(placement.rcNormalPosition) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public int minX, minY, maxX, maxY;
        public Win32.RECT rcNormalPosition;
    }

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);
    [LibraryImport("user32.dll")] private static partial nint GetShellWindow();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int GetWindowTextW(nint hwnd, Span<char> text, int size);

    public void Maximize(WindowRef window) => ShowWindow(window.Handle, SW_MAXIMIZE);
    public void Restore(WindowRef window) => ShowWindow(window.Handle, SW_RESTORE);
    public void Minimize(WindowRef window) => ShowWindow(window.Handle, SW_MINIMIZE);

    public static PixelRect? VisibleFrame(nint hwnd)
    {
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<Win32.RECT>()) == 0) return Rect(r);
        return Win32.GetWindowRect(hwnd, out r) ? Rect(r) : null;
    }

    private static PixelRect Rect(Win32.RECT r) => PixelRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
    private static Win32.RECT Win32Rect(PixelRect r) => new() { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };

    private static string ClassName(nint hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        var length = GetClassNameW(hwnd, buffer, buffer.Length);
        return new string(buffer[..Math.Max(0, length)]);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public Win32.RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    private const int GWL_STYLE = -16, SW_MAXIMIZE = 3, SW_MINIMIZE = 6, SW_RESTORE = 9;
    private const nint WS_CAPTION = 0xC00000, WS_THICKFRAME = 0x40000, WS_MAXIMIZEBOX = 0x10000;
    private const uint GA_ROOT = 2, MONITOR_DEFAULTTONEAREST = 2, MONITORINFOF_PRIMARY = 1;
    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;

    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsZoomed(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint hwnd, int command);
    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromRect(in Win32.RECT rect, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [LibraryImport("user32.dll")] private static unsafe partial int EnumDisplayMonitors(nint hdc, nint clip, delegate* unmanaged<nint, nint, nint, nint, int> callback, nint state);
    [LibraryImport("shcore.dll")] private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int GetClassNameW(nint hwnd, Span<char> name, int size);
}
