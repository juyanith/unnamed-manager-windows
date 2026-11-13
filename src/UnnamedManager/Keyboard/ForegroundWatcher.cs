using System.Runtime.InteropServices;
using UnnamedManager.Core.Config;

namespace UnnamedManager.Keyboard;

/// Follows the window in front (EVENT_SYSTEM_FOREGROUND) and tells the engine which
/// app it belongs to, so Context Aware and the Guide know without asking in the hook
/// callback (S-014). Runs on the UI thread; Windows calls back on it.
public sealed partial class ForegroundWatcher : IDisposable
{
    private static ForegroundWatcher? _instance;
    private readonly nint _hook;

    public ForegroundWatcher()
    {
        _instance = this;
        unsafe { _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS); }
        Current = Actions.AppSwitcher.FrontAppOf(UI.Win32.GetForegroundWindow());
        NoteElevated(UI.Win32.GetForegroundWindow(), Current);
    }

    /// The app in front now.
    public FrontApp? Current { get; private set; }

    /// The app in front, by name, while it runs as administrator (S-056). Clicking the
    /// tray icon puts the taskbar in front, so the taskbar and its overflow flyout leave
    /// this as it was: the tray menu shows the app you came from.
    public string? ElevatedApp { get; private set; }

    private void NoteElevated(nint hwnd, FrontApp? app)
    {
        if (hwnd == 0 || TrayClasses.Contains(Actions.AppSwitcher.ClassName(hwnd))) return;
        ElevatedApp = Platform.ProcessElevation.Above(hwnd) ? app?.Name ?? "The app in front" : null;
    }

    private static readonly HashSet<string> TrayClasses =
        ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow"];

    public event Action<FrontApp?>? Changed;

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (_instance is not { } watcher || objectId != 0) return;
        watcher.Current = Actions.AppSwitcher.FrontAppOf(hwnd);
        watcher.NoteElevated(hwnd, watcher.Current);
        watcher.Changed?.Invoke(watcher.Current);
    }

    public void Dispose() => UnhookWinEvent(_hook);

    private const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;

    [LibraryImport("user32.dll")]
    private static unsafe partial nint SetWinEventHook(uint min, uint max, nint module, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint process, uint thread, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);
}
