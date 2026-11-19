using System.Runtime.InteropServices;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Actions;

/// Windows' virtual desktops (spec v17, v20). Which desktop a window is on, and whether
/// it's the current one, use the public IVirtualDesktopManager. Listing the desktops,
/// switching with no slide and moving another app's window need the shell's own
/// IVirtualDesktopManagerInternal, which Windows doesn't document and changes between
/// builds: this knows its shape from Windows 11 24H2 (build 26100) on. Where it isn't
/// there, Switch Desktop presses Ctrl+Win+Left or Right, which slides, and Send to
/// Desktop fails with the alert sound.
public static class VirtualDesktops
{
    private static readonly Guid ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid ManagerInternalService = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid PublicManager = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    private static readonly Lazy<IVirtualDesktopManager?> Public = new(() =>
    {
        try { return (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(PublicManager)!)!; }
        catch (COMException) { return null; }
    });

    private static (IVirtualDesktopManagerInternal Desktops, IApplicationViewCollection Views)? _shell;

    /// The shell's services, opened again after Explorer restarts.
    private static (IVirtualDesktopManagerInternal Desktops, IApplicationViewCollection Views)? Shell()
    {
        if (_shell is { } open)
        {
            try
            {
                open.Desktops.GetCount();
                return open;
            }
            catch (COMException) { _shell = null; }
            catch (InvalidComObjectException) { _shell = null; }
        }
        try
        {
            var provider = (IServiceProvider10)Activator.CreateInstance(Type.GetTypeFromCLSID(ImmersiveShell)!)!;
            var service = ManagerInternalService;
            var iid = typeof(IVirtualDesktopManagerInternal).GUID;
            var desktops = (IVirtualDesktopManagerInternal)provider.QueryService(ref service, ref iid);
            var viewsId = typeof(IApplicationViewCollection).GUID;
            var views = (IApplicationViewCollection)provider.QueryService(ref viewsId, ref viewsId);
            _shell = (desktops, views);
            return _shell;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            AgentLog.Line($"DESKTOPS unavailable: {e.Message}");
            return null;
        }
    }

    /// Whether the shell's desktops can be listed and windows moved, for the self-test.
    public static bool Available => Shell() is not null;

    /// True when the window is on the desktop shown, or Windows doesn't say.
    public static bool IsOnCurrent(nint hwnd)
    {
        try { return Public.Value?.IsWindowOnCurrentVirtualDesktop(hwnd) ?? true; }
        catch (COMException) { return true; }
    }

    private static List<IVirtualDesktop> All(IVirtualDesktopManagerInternal desktops)
    {
        desktops.GetDesktops(out var array);
        var iid = typeof(IVirtualDesktop).GUID;
        return [.. Enumerable.Range(0, array.GetCount()).Select(i => (IVirtualDesktop)array.GetAt(i, ref iid))];
    }

    public static (int Current, int Count)? Desktops()
    {
        if (Shell() is not { } shell) return null;
        try
        {
            var all = All(shell.Desktops);
            var current = shell.Desktops.GetCurrentDesktop().GetId();
            return (all.FindIndex(d => d.GetId() == current) + 1, all.Count);
        }
        catch (COMException) { return null; }
    }

    public static DesktopPlace DesktopOf(nint hwnd)
    {
        if (Shell() is not { } shell || Public.Value is not { } manager) return new DesktopPlace.Unknown();
        try
        {
            if (shell.Views.GetViewForHwnd(hwnd, out var view) == 0 && view is not null && Pinned()?.IsViewPinned(view) == true)
                return new DesktopPlace.Every();
            var id = manager.GetWindowDesktopId(hwnd);
            var all = All(shell.Desktops);
            var index = all.FindIndex(d => d.GetId() == id);
            return index < 0 ? new DesktopPlace.Unknown() : new DesktopPlace.On(index + 1, all.Count);
        }
        catch (COMException) { return new DesktopPlace.Unknown(); }
    }

    /// Shows desktop `number` at once. Without the shell's interface, presses Ctrl+Win
    /// and an arrow when it's the one beside the current desktop.
    public static bool Switch(int number, int current)
    {
        if (Shell() is { } shell)
        {
            try
            {
                var all = All(shell.Desktops);
                if (number < 1 || number > all.Count) return false;
                shell.Desktops.SwitchDesktop(all[number - 1]);
                return true;
            }
            catch (COMException e) { AgentLog.Line($"DESKTOPS switch failed: {e.Message}"); }
        }
        if (Math.Abs(number - current) != 1) return false;
        var chord = new Core.Config.Chord(Core.Config.Modifiers.Ctrl | Core.Config.Modifiers.Win, number < current ? Core.Config.KeyName.Left : Core.Config.KeyName.Right);
        return Keyboard.KeyboardHook.Inject(Core.Keyboard.ShortcutEvents.Build([chord], ActionRunner.HeldModifierKeys()));
    }

    public static bool Send(nint hwnd, int number)
    {
        if (Shell() is not { } shell) return false;
        try
        {
            var all = All(shell.Desktops);
            if (number < 1 || number > all.Count || shell.Views.GetViewForHwnd(hwnd, out var view) != 0 || view is null) return false;
            shell.Desktops.MoveViewToDesktop(view, all[number - 1]);
            return true;
        }
        catch (COMException e)
        {
            AgentLog.Line($"DESKTOPS send failed: {e.Message}");
            return false;
        }
    }

    private static IVirtualDesktopPinnedApps? _pinned;

    private static IVirtualDesktopPinnedApps? Pinned()
    {
        if (_pinned is not null) return _pinned;
        try
        {
            var provider = (IServiceProvider10)Activator.CreateInstance(Type.GetTypeFromCLSID(ImmersiveShell)!)!;
            var service = new Guid("B5A399E7-1C87-46B8-88E9-FC5747B171BD");
            var iid = typeof(IVirtualDesktopPinnedApps).GUID;
            return _pinned = (IVirtualDesktopPinnedApps)provider.QueryService(ref service, ref iid);
        }
        catch (Exception e) when (e is COMException or InvalidCastException) { return null; }
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider10
    {
        [return: MarshalAs(UnmanagedType.IUnknown)] object QueryService(ref Guid service, ref Guid riid);
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        int GetCount();
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetAt(int index, ref Guid riid);
    }

    /// Only passed back to the shell, so none of its methods are declared.
    [ComImport, Guid("372E1D3B-38D3-42E4-A15B-8AB2B178F513"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationView;

    [ComImport, Guid("3F07F4BE-B107-441A-AF0F-39D82529072C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktop
    {
        bool IsViewVisible(IApplicationView view);
        Guid GetId();
    }

    /// The first methods of the 24H2 interface, in vtable order.
    [ComImport, Guid("53F5CA0B-158F-4124-900C-057158060B27"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManagerInternal
    {
        int GetCount();
        void MoveViewToDesktop(IApplicationView view, IVirtualDesktop desktop);
        bool CanViewMoveDesktops(IApplicationView view);
        IVirtualDesktop GetCurrentDesktop();
        void GetDesktops(out IObjectArray desktops);
        [PreserveSig] int GetAdjacentDesktop(IVirtualDesktop from, int direction, out IVirtualDesktop desktop);
        void SwitchDesktop(IVirtualDesktop desktop);
    }

    [ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationViewCollection
    {
        [PreserveSig] int GetViews(out IObjectArray array);
        [PreserveSig] int GetViewsByZOrder(out IObjectArray array);
        [PreserveSig] int GetViewsByAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id, out IObjectArray array);
        [PreserveSig] int GetViewForHwnd(nint hwnd, out IApplicationView view);
    }

    [ComImport, Guid("4CE81583-1E4C-4632-A621-07A53543148F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopPinnedApps
    {
        bool IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void PinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void UnpinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        bool IsViewPinned(IApplicationView view);
    }

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        bool IsWindowOnCurrentVirtualDesktop(nint hwnd);
        Guid GetWindowDesktopId(nint hwnd);
    }
}
