using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using UnnamedManager.Core.Config;
using UnnamedManager.Keyboard;

namespace UnnamedManager.Actions;

/// Use Application on Windows (S-006): find an app's window by its exe or its
/// AppUserModelID, bring it to the front, or start the app.
public static partial class AppSwitcher
{
    /// The app's most recently used top-level window, or 0 when it isn't running.
    /// EnumWindows goes front to back; WindowPicker takes the one used last (S-018), on
    /// any virtual desktop (S-080).
    public static nint FindWindow(UseApplicationAction app)
    {
        app = WithRealPath(app);
        var paths = new Dictionary<uint, string?>();
        var self = (uint)Environment.ProcessId;
        // Unnamed Manager's own windows count only when the app asked for is Unnamed Manager.
        var targetIsUs = app.Exe is not null && Environment.ProcessPath is { } us && app.MatchesExe(RealPath(us) ?? us);
        var facts = new List<WindowFacts>();
        EnumTopLevel(hwnd =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            facts.Add(new WindowFacts(
                hwnd,
                BelongsToApp: Matches(app, hwnd, paths),
                Visible: true,
                Minimized: IsIconic(hwnd),
                ToolWindow: (GetWindowLongPtrW(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0,
                Presence: PresenceOf(hwnd),
                Kind: KindOf(hwnd),
                Ours: pid == self && !targetIsUs,
                // Asks without waiting: a window that hasn't answered for 5 s counts as hung. Never
                // our own: this is the thread that would answer, and Windows can call it hung meanwhile.
                Hung: pid != self && IsHungAppWindow(hwnd),
                Blocked: IsBlocked(hwnd)));
            return true;
        });
        return WindowPicker.Pick(facts, ActionRunner.Windows.History);
    }

    /// Every top-level window that an app counts as its own, front to back, on every
    /// virtual desktop: a window that was there before is never taken for a new one.
    public static List<nint> Windows(UseApplicationAction app)
    {
        app = WithRealPath(app);
        var list = new List<nint>();
        var paths = new Dictionary<uint, string?>();
        EnumTopLevel(hwnd =>
        {
            if (IsWindowVisible(hwnd) && IsSwitchable(hwnd) && PresenceOf(hwnd) != Core.Windows.WindowPresence.Nowhere && Matches(app, hwnd, paths)) list.Add(hwnd);
            return true;
        });
        return list;
    }

    private static bool Matches(UseApplicationAction app, nint hwnd, Dictionary<uint, string?> paths)
    {
        var pid = ContentProcess(hwnd);
        if (app.Exe is not null)
        {
            if (!paths.TryGetValue(pid, out var path)) paths[pid] = path = ProcessPath(pid);
            return path is not null && app.MatchesExe(path);
        }
        return string.Equals(AppUserModelId(hwnd, pid), app.AppId, StringComparison.OrdinalIgnoreCase);
    }

    /// A full path as the file system has it, so it compares with the path Windows
    /// reports for a running program: links, junctions and redirected folders
    /// (an app installed from inside a packaged app's session) resolve.
    private static UseApplicationAction WithRealPath(UseApplicationAction app) =>
        app.IsPath && RealPath(app.Exe!) is { } real ? app with { Exe = real } : app;

    public static string? RealPath(string path)
    {
        var file = CreateFileW(Environment.ExpandEnvironmentVariables(path), 0, 7, 0, 3, 0x02000000, 0);
        if (file == -1) return null;
        try
        {
            Span<char> buffer = stackalloc char[1024];
            var length = (int)GetFinalPathNameByHandleW(file, buffer, (uint)buffer.Length, 0);
            if (length <= 0 || length >= buffer.Length) return null;
            var real = new string(buffer[..length]);
            if (real.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + real[8..];
            return real.StartsWith(@"\\?\", StringComparison.Ordinal) ? real[4..] : real;
        }
        finally { CloseHandle(file); }
    }

    /// A standard window, a dialog (spec v21), or neither.
    internal static Core.Windows.WindowKind KindOf(nint hwnd) => Core.Windows.WindowKinds.Of(
        visible: IsWindowVisible(hwnd),
        owned: GetWindow(hwnd, GW_OWNER) != 0,
        toolWindow: (GetWindowLongPtrW(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0,
        enabled: IsWindowEnabled(hwnd),
        caption: (GetWindowLongPtrW(hwnd, GWL_STYLE) & WS_CAPTION) == WS_CAPTION);

    /// A window that a modal dialog of its own disabled, so it can't take input (spec v21).
    internal static bool IsBlocked(nint hwnd) =>
        !IsWindowEnabled(hwnd) && GetLastActivePopup(hwnd) is var popup && popup != 0 && popup != hwnd
        && KindOf(popup) == Core.Windows.WindowKind.Dialog;

    /// A window someone would switch to: a standard window or a dialog, not cloaked (on
    /// another desktop, or a suspended Store app).
    internal static bool IsCandidate(nint hwnd)
    {
        if (!IsSwitchable(hwnd)) return false;
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    /// Here, on another virtual desktop, or nowhere you can go (S-078).
    internal static Core.Windows.WindowPresence PresenceOf(nint hwnd) => ReadPresence(hwnd);

    /// The self-test swaps it: a test can't put a window on another desktop without switching yours.
    internal static Func<nint, Core.Windows.WindowPresence> ReadPresence { get; set; } = RealPresence;

    internal static Core.Windows.WindowPresence RealPresence(nint hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0) cloaked = 0;
        return Core.Windows.Presence.Of(cloaked, VirtualDesktops.IsOnCurrent(hwnd));
    }

    /// A standard window or a dialog, cloaked or not.
    internal static bool IsSwitchable(nint hwnd) => KindOf(hwnd) != Core.Windows.WindowKind.None;

    /// The exe path of the app that draws the window, for its icon (spec v18).
    internal static string? AppPath(nint hwnd) => ProcessPath(ContentProcess(hwnd));

    /// The process that draws the window. A Store app's frame belongs to
    /// ApplicationFrameHost; its content window, a child, belongs to the app.
    private static uint ContentProcess(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (ClassName(hwnd) != "ApplicationFrameWindow") return pid;
        var inner = pid;
        EnumChildWindows(hwnd, child =>
        {
            GetWindowThreadProcessId(child, out var childPid);
            if (childPid != pid) { inner = childPid; return false; }
            return true;
        });
        return inner;
    }

    /// The app a window belongs to, as Context Aware matches it (S-014): its content
    /// process's path, its AppUserModelID, and a name for the Guide.
    public static FrontApp? FrontAppOf(nint hwnd)
    {
        if (hwnd == 0) return null;
        var pid = ContentProcess(hwnd);
        var path = ProcessPath(pid);
        var appId = AppUserModelId(hwnd, pid);
        if (path is null && appId is null) return null;
        return new FrontApp(path, appId, DisplayName(path));
    }

    /// What messages call an app: its program's own name when it's installed, else
    /// the exe's file name without .exe, or the AppUserModelID.
    public static string Name(UseApplicationAction app) =>
        app.Exe is { } exe ? (Resolve(exe) is { } path ? DisplayName(path) : Path.GetFileNameWithoutExtension(exe.Trim())) : app.AppId!;

    /// The program's own name ("Notepad"), or its file name without .exe.
    private static string DisplayName(string? path)
    {
        if (path is null) return "this app";
        try
        {
            var description = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch (FileNotFoundException) { }
        return Path.GetFileNameWithoutExtension(path);
    }

    public static string? ProcessPath(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return null;
        try
        {
            Span<char> buffer = stackalloc char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer[..size]) : null;
        }
        finally { CloseHandle(process); }
    }

    /// The window's own AppUserModelID if it sets one, else its process's (packaged apps).
    private static string? AppUserModelId(nint hwnd, uint pid) => WindowAppId(hwnd) ?? ProcessAppId(pid);

    private static string? ProcessAppId(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return null;
        try
        {
            Span<char> buffer = stackalloc char[256];
            var length = (uint)buffer.Length;
            return GetApplicationUserModelId(process, ref length, buffer) == 0 ? new string(buffer[..((int)length - 1)]) : null;
        }
        finally { CloseHandle(process); }
    }

    /// PKEY_AppUserModel_ID from the window's property store, read through
    /// IPropertyStore's vtable so no COM wrappers are needed.
    private static unsafe string? WindowAppId(nint hwnd)
    {
        var iid = IID_IPropertyStore;
        if (SHGetPropertyStoreForWindow(hwnd, in iid, out var store) != 0 || store == 0) return null;
        try
        {
            var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
            var value = default(PROPVARIANT);
            var getValue = (delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, PROPVARIANT*, int>)(*(nint**)store)[5];
            if (getValue(store, &key, &value) != 0) return null;
            try { return value.vt == VT_LPWSTR && value.pointer != 0 ? Marshal.PtrToStringUni(value.pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.Release(store); }
    }

    /// Restores the window if minimized and brings it to the front with focus.
    /// Windows only lets the foreground app hand out the foreground; pressing the
    /// app's masking key first counts as the latest input, which allows it.
    public static bool Focus(nint hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        KeyboardHook.SendMaskKey();
        if (!UI.Win32.SetForegroundWindow(hwnd)) UI.Win32.ForceForeground(hwnd);
        return UI.Win32.GetForegroundWindow() == hwnd;
    }

    /// Whether a window has focus: it, or a window of the same owner, such as its dialog (S-084).
    internal static bool HasFocus(nint hwnd) => ReadFocus(hwnd);

    /// The self-test swaps it: a test can't make Windows refuse a window.
    internal static Func<nint, bool> ReadFocus { get; set; } = RealFocus;

    internal static bool RealFocus(nint hwnd)
    {
        var front = UI.Win32.GetForegroundWindow();
        return front != 0 && (front == hwnd || GetAncestor(front, GA_ROOTOWNER) == GetAncestor(hwnd, GA_ROOTOWNER));
    }

    /// Brings the window forward and waits for focus to arrive. Returns why it didn't,
    /// or null: a window that closed meanwhile is no failure.
    public static async Task<string?> FocusAndCheckAsync(nint hwnd)
    {
        Focus(hwnd);
        var began = Environment.TickCount64;
        while (!HasFocus(hwnd))
        {
            if (!UI.Win32.IsWindow(hwnd)) return null;
            if (Environment.TickCount64 - began >= Core.Windows.WindowControl.FocusWait.TotalMilliseconds)
                return $"Couldn't focus {FrontAppOf(hwnd)?.Name ?? "the window"}.";
            await Task.Delay(25);
        }
        return null;
    }

    /// Where an exe file name is installed: App Paths, then PATH. A full path is
    /// used as is. Null when it isn't found.
    public static string? Resolve(string exe)
    {
        exe = exe.Trim();
        if (exe.Contains('\\') || exe.Contains('/'))
            return File.Exists(Environment.ExpandEnvironmentVariables(exe)) ? Environment.ExpandEnvironmentVariables(exe) : null;
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string registered)
            {
                var path = Environment.ExpandEnvironmentVariables(registered.Trim('"'));
                if (File.Exists(path)) return path;
            }
        }
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Prepend(Environment.SystemDirectory)
            .Prepend(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        foreach (var folder in folders)
        {
            try
            {
                var path = Path.Combine(Environment.ExpandEnvironmentVariables(folder), exe);
                if (File.Exists(path)) return path;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// Whether an AppUserModelID is an app in Start (shell:AppsFolder).
    public static bool IsInstalled(string appId)
    {
        var iid = IID_IShellItem;
        if (SHCreateItemFromParsingName($@"shell:AppsFolder\{appId}", 0, in iid, out var item) != 0) return false;
        Marshal.Release(item);
        return true;
    }

    /// Starts the app. Returns why it couldn't, or null when it started.
    public static string? Start(UseApplicationAction app)
    {
        if (app.Exe is { } exe)
        {
            if (Resolve(exe) is not { } path)
                return $"Can't find {exe}. Check that it's installed, or use its full path.";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) })?.Dispose();
            return null;
        }
        if (!IsInstalled(app.AppId!))
            return $"Can't find the app {app.AppId}. Check that it's installed.";
        Process.Start(new ProcessStartInfo("explorer.exe", $@"shell:AppsFolder\{app.AppId}") { UseShellExecute = true })?.Dispose();
        return null;
    }

    public static string ClassName(nint hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        var length = GetClassNameW(hwnd, buffer, buffer.Length);
        return new string(buffer[..Math.Max(0, length)]);
    }

    internal delegate bool WindowVisitor(nint hwnd);

    internal static void EnumTopLevel(WindowVisitor visit)
    {
        var handle = GCHandle.Alloc(visit);
        try { unsafe { EnumWindows(&Visit, GCHandle.ToIntPtr(handle)); } }
        finally { handle.Free(); }
    }

    private static void EnumChildWindows(nint parent, WindowVisitor visit)
    {
        var handle = GCHandle.Alloc(visit);
        try { unsafe { EnumChildWindows(parent, &Visit, GCHandle.ToIntPtr(handle)); } }
        finally { handle.Free(); }
    }

    [UnmanagedCallersOnly]
    private static int Visit(nint hwnd, nint state) => ((WindowVisitor)GCHandle.FromIntPtr(state).Target!)(hwnd) ? 1 : 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt, r1, r2, r3;
        public nint pointer;
        public nint extra;
    }

    private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private const ushort VT_LPWSTR = 31;
    private const int GWL_EXSTYLE = -20, DWMWA_CLOAKED = 14, SW_RESTORE = 9;
    private const uint GW_OWNER = 4, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const nint WS_EX_TOOLWINDOW = 0x80, WS_CAPTION = 0xC00000;
    private const int GWL_STYLE = -16;
    private const uint GA_ROOTOWNER = 3;

    [LibraryImport("user32.dll")] private static unsafe partial int EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint state);
    [LibraryImport("user32.dll")] private static unsafe partial int EnumChildWindows(nint parent, delegate* unmanaged<nint, nint, int> callback, nint state);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsHungAppWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint hwnd, int command);
    [LibraryImport("user32.dll")] private static partial nint GetWindow(nint hwnd, uint command);
    [LibraryImport("user32.dll")] private static partial nint GetLastActivePopup(nint hwnd);
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowEnabled(nint hwnd);
    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int GetClassNameW(nint hwnd, Span<char> name, int size);
    [LibraryImport("dwmapi.dll")] private static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [LibraryImport("kernel32.dll")] private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint handle);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool QueryFullProcessImageNameW(nint process, uint flags, Span<char> name, ref int size);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int GetApplicationUserModelId(nint process, ref uint length, Span<char> id);
    [LibraryImport("shell32.dll")] private static partial int SHGetPropertyStoreForWindow(nint hwnd, in Guid iid, out nint store);
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] private static partial nint CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint GetFinalPathNameByHandleW(nint file, Span<char> path, uint size, uint flags);
    [LibraryImport("ole32.dll")] private static partial int PropVariantClear(ref PROPVARIANT value);
}
