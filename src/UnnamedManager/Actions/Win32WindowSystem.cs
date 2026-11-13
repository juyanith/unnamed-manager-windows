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
        return new WindowFacts(frame, IsZoomed(hwnd), canMaximize, IsFullScreen(hwnd, style));
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
            if (VisibleFrame(hwnd) is { Width: > 0, Height: > 0 } frame) list.Add(new WindowInfo(new WindowRef(hwnd), frame, (int)pid));
            return true;
        });
        return list;
    }

    public void Focus(WindowRef window) => AppSwitcher.Focus(window.Handle);

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
