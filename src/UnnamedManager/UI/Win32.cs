using System.Runtime.InteropServices;

namespace UnnamedManager.UI;

/// The few Win32 calls WinUI has no API for. One place, so they are easy to find and review.
internal static partial class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint PW_RENDERFULLCONTENT = 2;
    public const uint ES_CONTINUOUS = 0x80000000, ES_DISPLAY_REQUIRED = 0x00000002, ES_SYSTEM_REQUIRED = 0x00000001;

    [LibraryImport("user32.dll")] public static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport("user32.dll")] public static partial uint GetDpiForSystem();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetWindowRect(nint hwnd, out RECT rect);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool PrintWindow(nint hwnd, nint hdc, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BringWindowToTop(nint hwnd);
    [LibraryImport("user32.dll")] public static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint FindWindowW(string? className, string? windowName);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyIcon(nint icon);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT point);
    [LibraryImport("user32.dll")] public static partial nint SendMessageW(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool AttachThreadInput(uint from, uint to, [MarshalAs(UnmanagedType.Bool)] bool attach);
    [LibraryImport("kernel32.dll")] public static partial uint GetCurrentThreadId();
    [LibraryImport("kernel32.dll")] public static partial uint SetThreadExecutionState(uint flags);
    [LibraryImport("user32.dll")] public static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int ReleaseDC(nint hwnd, nint hdc);
    [LibraryImport("gdi32.dll")] public static partial nint CreateCompatibleDC(nint hdc);
    [LibraryImport("gdi32.dll")] public static partial nint SelectObject(nint hdc, nint obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteDC(nint hdc);
    public const uint SRCCOPY = 0x00CC0020;
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, uint rop);
    [LibraryImport("gdi32.dll")] public static partial nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);
    [LibraryImport("dwmapi.dll")] public static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out RECT value, int size);

    /// Windows only lets the foreground app hand out focus. Joining the input of the
    /// foreground thread for a moment lets an agent-started run bring its window forward,
    /// so snapshots show an active window (bright title bar, full Mica).
    public static void ForceForeground(nint hwnd)
    {
        var front = GetForegroundWindow();
        if (front == hwnd) return;
        var frontThread = GetWindowThreadProcessId(front, out _);
        var thisThread = GetCurrentThreadId();
        AttachThreadInput(thisThread, frontThread, true);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        AttachThreadInput(thisThread, frontThread, false);
    }
}
