using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using UnnamedManager.Core.Config;
using UnnamedManager.UI;
using Windows.Graphics;
using WinRT;

namespace UnnamedManager.Guide;

/// The Guide's window (S-005): borderless, topmost, never activated, not in the
/// taskbar or Alt+Tab, acrylic, with DWM's rounded corners, border and shadow.
/// The app in front keeps its focus and caret the whole time.
public sealed partial class GuideWindow : Window
{
    private readonly GuideView _view = new();
    private readonly DesktopAcrylicController? _acrylic;
    private readonly SystemBackdropConfiguration _backdrop = new() { IsInputActive = true };

    public GuideWindow()
    {
        Content = _view;
        Title = "Unnamed Manager Guide";
        var hwnd = this.Handle();

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        // A border but no title bar: DWM still draws the thin system border and the shadow.
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        // No activation, no taskbar button: the app in front keeps the keyboard.
        var ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST) & ~WS_EX_APPWINDOW);
        var round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        // Acrylic, kept looking active: the Guide is never the active window, and an
        // inactive acrylic falls back to a flat color.
        if (DesktopAcrylicController.IsSupported())
        {
            _acrylic = new DesktopAcrylicController();
            _acrylic.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
            _acrylic.SetSystemBackdropConfiguration(_backdrop);
        }
        Closed += (_, _) => _acrylic?.Dispose();
    }

    public GuideView View => _view;

    /// "light" or "dark" instead of the config's theme, for snapshots.
    public string? ThemeOverride { get; set; }

    public bool IsShown => AppWindow.IsVisible;

    /// Shows a menu on the monitor of the window in front, without activating.
    public void Show(BindingNode menu, IReadOnlyList<string> path, LoadedConfig config, FrontApp? front = null)
    {
        _closeTimer?.Stop();
        Prepare(config);
        _view.Show(menu, path, config, front);
        _view.AlignKeycaps();
        Present();
        _view.Announce($"{path[^1]}, {menu.Children.Count} {(menu.Children.Count == 1 ? "key" : "keys")}");
    }

    /// Shows Home: every leader and direct binding in groups by their modifiers (S-021).
    public void ShowHome(Core.Keyboard.Home home, IReadOnlyList<string> path, LoadedConfig config, FrontApp? front = null)
    {
        _closeTimer?.Stop();
        Prepare(config);
        _view.ShowHome(home, path, config, front);
        _view.AlignKeycaps();
        Present();
        var count = home.Groups.Sum(g => g.Entries.Count);
        _view.Announce($"Home, {home.Groups.Count} groups, {count} {(count == 1 ? "key" : "keys")}");
    }

    /// Shows why an action failed for Motion.ErrorMessageHold (2 s), then closes (S-016).
    /// A new sequence before then replaces it.
    public void ShowFailure(string message, LoadedConfig config)
    {
        Prepare(config);
        _view.ShowFailure(message);
        Present();
        _view.Announce(message);
        _closeTimer ??= DispatcherQueue.CreateTimer();
        _closeTimer.Stop();
        _closeTimer.Interval = Motion.ErrorMessageHold;
        _closeTimer.IsRepeating = false;
        _closeTimer.Tick += CloseAfterFailure;
        _closeTimer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _closeTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _flashTimer;

    /// Outlines the Guide in the accent for Motion.ErrorFlashHold (S-033). A state, not
    /// an animation, so it shows with animation effects off too.
    public void Flash()
    {
        _view.IsFlashing = true;
        if (_flashTimer is null)
        {
            _flashTimer = DispatcherQueue.CreateTimer();
            _flashTimer.IsRepeating = false;
            _flashTimer.Interval = Motion.ErrorFlashHold;
            _flashTimer.Tick += (_, _) => _view.IsFlashing = false;
        }
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void CloseAfterFailure(Microsoft.UI.Dispatching.DispatcherQueueTimer timer, object _)
    {
        timer.Tick -= CloseAfterFailure;
        if (_view.FailureMessage is not null) Hide();
    }

    private void Prepare(LoadedConfig config)
    {
        ApplyTheme(ThemeOverride ?? config.Config.UI.Theme.ToLowerInvariant());
        var effects = new Windows.UI.ViewManagement.UISettings();
        var contrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        _view.UseSolidBackground(!effects.AdvancedEffectsEnabled || contrast || _acrylic is null);
        _monitor = Monitor.OfForegroundWindow();
        _position = config.Config.UI.Position.ToLowerInvariant();
        _view.MaxWidth = _monitor.Work.Width / _monitor.Scale * 0.8;
    }

    private void Present()
    {
        Fit();
        if (!AppWindow.IsVisible) AppWindow.Show(false);
        // The rows only get their final size once laid out in the window: fit again then.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Fit);
    }

    private Monitor _monitor;
    private string _position = "center";

    /// Sizes the window to its content and puts it where the config says, on the
    /// monitor of the window in front.
    private void Fit()
    {
        _view.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _view.DesiredSize;
        var client = new SizeInt32((int)Math.Ceiling(size.Width * _monitor.Scale), (int)Math.Ceiling(size.Height * _monitor.Scale));
        if (AppWindow.ClientSize != client)
        {
            AppWindow.ResizeClient(client);
            // ResizeClient makes room for a caption this window doesn't have: take it back off.
            var extra = new SizeInt32(AppWindow.ClientSize.Width - client.Width, AppWindow.ClientSize.Height - client.Height);
            if (extra.Width != 0 || extra.Height != 0)
                AppWindow.Resize(new SizeInt32(AppWindow.Size.Width - extra.Width, AppWindow.Size.Height - extra.Height));
        }
        var outer = AppWindow.Size;
        var work = _monitor.Work;
        var edge = (int)Math.Round((double)Application.Current.Resources["GuideEdgeOffset"] * _monitor.Scale);
        var x = work.X + (work.Width - outer.Width) / 2;
        var y = _position switch
        {
            "top" => work.Y + edge,
            "bottom" => work.Y + work.Height - outer.Height - edge,
            _ => work.Y + (work.Height - outer.Height) / 2,
        };
        AppWindow.Move(new PointInt32(x, y));
    }

    public void Hide()
    {
        _flashTimer?.Stop();
        _view.IsFlashing = false;
        if (AppWindow.IsVisible) AppWindow.Hide();
    }

    private void ApplyTheme(string mode)
    {
        _view.RequestedTheme = Theme.Element(mode);
        var dark = _view.ActualTheme == ElementTheme.Dark;
        _backdrop.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
    }

    /// The work area and scale of the monitor holding the window in front.
    private readonly record struct Monitor(RectInt32 Work, double Scale)
    {
        public static Monitor OfForegroundWindow()
        {
            var handle = MonitorFromWindow(Win32.GetForegroundWindow(), MONITOR_DEFAULTTOPRIMARY);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfoW(handle, ref info);
            GetDpiForMonitor(handle, 0, out var dpi, out _);
            var w = info.rcWork;
            return new Monitor(new RectInt32(w.Left, w.Top, w.Width, w.Height), dpi / 96.0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public Win32.RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    private const int GWL_EXSTYLE = -20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;
    public const nint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [LibraryImport("user32.dll")] public static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport("user32.dll")] private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [LibraryImport("shcore.dll")] private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [LibraryImport("dwmapi.dll")] private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
