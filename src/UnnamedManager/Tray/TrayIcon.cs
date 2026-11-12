using System.Runtime.InteropServices;

namespace UnnamedManager.Tray;

public enum TrayState { Ready, Off, Warning }

/// The tray icon (S-008, S-009): Shell_NotifyIcon on a hidden window of its own,
/// which also gets the icon's clicks, TaskbarCreated when Explorer restarts, and
/// the message a second copy of the app sends (S-010). Test runs never add the
/// icon: Windows remembers every tray icon it sees, keyed by the exe's path.
public sealed unsafe partial class TrayIcon : IDisposable
{
    public const string WindowClass = "UnnamedManager.Tray";

    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private static readonly uint TaskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    public static uint TaskbarCreatedMessage => TaskbarCreated;

    /// What a second copy sends the first: open your tray menu (S-010).
    public static readonly uint ShowMenuMessage = RegisterWindowMessageW("UnnamedManager.ShowTrayMenu");

    private static TrayIcon? _instance;

    private readonly bool _addToShell;
    private nint _icon;

    /// `instanceKey` names the window, so a test run's copy only talks to test runs.
    public TrayIcon(string instanceKey, bool addToShell)
    {
        _instance = this;
        _addToShell = addToShell;
        var module = GetModuleHandleW(null);
        fixed (char* name = WindowClass)
        {
            var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = module, lpszClassName = name };
            RegisterClassExW(ref wc);
        }
        // A hidden top-level window, not a message-only one: those never get TaskbarCreated.
        Handle = CreateWindowExW(0, WindowClass, instanceKey, 0, 0, 0, 0, 0, 0, 0, module, 0);
    }

    public nint Handle { get; }

    public TrayState State { get; private set; } = TrayState.Ready;

    public string Tooltip { get; private set; } = "Unnamed Manager";

    /// How many times the icon was added: at start, and again after each TaskbarCreated.
    public int AddCount { get; private set; }

    public bool IsAdded { get; private set; }

    /// A right click, Shift+F10 or the Menu key on the icon, with the anchor point in
    /// screen pixels and whether the keyboard opened it: the tray menu.
    public event Action<int, int, bool>? Invoked;

    /// A left click, or Enter on the icon: the Bindings window (S-038).
    public event Action? Selected;

    /// Sends a notification through the icon's own window procedure, for the self-test.
    public void NotifyForTest(bool select) => UI.Win32.SendMessageW(Handle, CallbackMessage, 0, (nint)(select ? NIN_SELECT : WM_CONTEXTMENU));

    /// A second copy of the app asked for the tray menu.
    public event Action? ShowMenuRequested;

    public void Add()
    {
        AddCount++;
        if (!_addToShell) return;
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        IsAdded = Shell_NotifyIconW(NIM_ADD, ref data);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
        if (!IsAdded) AgentLog.Line("TRAY Windows didn't add the tray icon");
    }

    /// Shows a state: the icon and, always in words, the tooltip.
    public void Show(TrayState state)
    {
        State = state;
        Tooltip = state switch
        {
            TrayState.Off => "Unnamed Manager, off",
            TrayState.Warning => "Unnamed Manager, config error",
            _ => "Unnamed Manager",
        };
        if (!_addToShell || !IsAdded) return;
        var data = Data(NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Remove()
    {
        if (!IsAdded) return;
        var data = Data(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        IsAdded = false;
    }

    public void Dispose()
    {
        Remove();
        if (_icon != 0) DestroyIcon(_icon);
        DestroyWindow(Handle);
    }

    private NOTIFYICONDATAW Data(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = Handle,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
        };
        if ((flags & NIF_ICON) != 0) data.hIcon = LoadStateIcon();
        var tip = Tooltip.AsSpan(0, Math.Min(Tooltip.Length, 127));
        tip.CopyTo(new Span<char>(data.szTip, 128));
        return data;
    }

    /// The state's icon at the size the tray uses on this display.
    private nint LoadStateIcon()
    {
        if (_icon != 0) DestroyIcon(_icon);
        var size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForWindow(Handle) is var dpi and > 0 ? dpi : 96);
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", $"Tray{State}.ico");
        _icon = LoadImageW(0, path, IMAGE_ICON, size, size, LR_LOADFROMFILE);
        return _icon;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (_instance is { } tray && hwnd == tray.Handle)
        {
            if (message == CallbackMessage)
            {
                var anchorX = (short)(wParam & 0xFFFF);
                var anchorY = (short)((wParam >> 16) & 0xFFFF);
                switch ((uint)(lParam & 0xFFFF))
                {
                    case NIN_SELECT or NIN_KEYSELECT: tray.Selected?.Invoke(); break;
                    case WM_CONTEXTMENU: tray.Invoked?.Invoke(anchorX, anchorY, false); break;
                }
                return 0;
            }
            if (message == TaskbarCreated)
            {
                // Explorer restarted and forgot the icon: add it again.
                tray.IsAdded = false;
                tray.Add();
                tray.Show(tray.State);
                return 0;
            }
            if (message == ShowMenuMessage)
            {
                tray.ShowMenuRequested?.Invoke();
                return 0;
            }
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    /// Asks the copy already running (its tray window is named `instanceKey`) to show its menu.
    public static bool AskRunningCopy(string instanceKey)
    {
        var other = FindWindowW(WindowClass, instanceKey);
        return other != 0 && PostMessageW(other, ShowMenuMessage, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public delegate* unmanaged<nint, uint, nuint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState, dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80, NOTIFYICON_VERSION_4 = 4;
    private const uint NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401, WM_CONTEXTMENU = 0x7B;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    private const int SM_CXSMICON = 49;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint RegisterWindowMessageW(string name);
    [LibraryImport("user32.dll")] private static partial ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] private static partial nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint FindWindowW(string cls, string name);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostMessageW(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyIcon(nint icon);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetricsForDpi(int index, uint dpi);
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport("shell32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandleW(string? name);
}
