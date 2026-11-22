using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using UnnamedManager.UI;

namespace UnnamedManager.Guide;

/// The Guide's window (S-005), in WPF: borderless, topmost, never activated, not in the
/// taskbar or Alt+Tab, with DWM's rounded corners. The app in front keeps its focus and
/// caret the whole time. Solid, not acrylic: WPF has no acrylic for a window it never activates.
public sealed partial class GuideWindow : Window
{
    /// How long the accent outline stays for a key that isn't bound (S-033).
    private static readonly TimeSpan ErrorFlashHold = TimeSpan.FromMilliseconds(300);

    /// How long a failed action's message stays before the Guide closes (S-016).
    private static readonly TimeSpan ErrorMessageHold = TimeSpan.FromSeconds(2);

    private readonly GuideView _view = new();
    private readonly nint _hwnd;
    private readonly DispatcherTimer _closeTimer = new() { Interval = ErrorMessageHold };
    private readonly DispatcherTimer _flashTimer = new() { Interval = ErrorFlashHold };

    public GuideWindow()
    {
        Content = _view;
        Title = "Unnamed Manager Guide";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");

        // The handle now, so the styles are set before the window first shows.
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        var ex = GetWindowLongPtrW(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, (ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST) & ~WS_EX_APPWINDOW);
        var round = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (_view.FailureMessage is not null) Hide();
        };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            _view.IsFlashing = false;
        };
        // A move to a monitor at another scale resizes the window: place it again.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Fit, DispatcherPriority.Background);
    }

    public GuideView View => _view;

    public bool IsShown => IsVisible;

    /// Shows a menu on the monitor of the window in front, without activating.
    /// `highlight` is a held menu's highlighted item, 0 for Cancel (spec v18).
    public void Show(BindingNode menu, IReadOnlyList<string> path, LoadedConfig config, FrontApp? front = null, int? highlight = null)
    {
        _closeTimer.Stop();
        Prepare(config);
        var moved = _view.Highlight is not null && _view.ShownMenu?.Equals(menu.Path) == true;
        _view.Show(menu, path, config, front, highlight);
        _view.AlignKeycaps();
        Present();
        if (highlight is { } h && moved)
            _view.Announce(h == 0 ? _view.CancelTitle : menu.Children[h - 1].Title);
        else
            _view.Announce($"{path[^1]}, {menu.Children.Count} {(menu.Children.Count == 1 ? "key" : "keys")}{(highlight is { } first && first > 0 ? $", {menu.Children[first - 1].Title}" : "")}");
    }

    /// Shows Home: every leader and direct binding in groups by their modifiers (S-021).
    public void ShowHome(Core.Keyboard.Home home, IReadOnlyList<string> path, LoadedConfig config, FrontApp? front = null)
    {
        _closeTimer.Stop();
        Prepare(config);
        _view.ShowHome(home, path, config, front);
        _view.AlignKeycaps();
        Present();
        var count = home.Groups.Sum(g => g.Entries.Count);
        _view.Announce($"Home, {home.Groups.Count} groups, {count} {(count == 1 ? "key" : "keys")}");
    }

    /// Shows why an action failed for two seconds, then closes (S-016). A new sequence
    /// before then replaces it.
    public void ShowFailure(string message, LoadedConfig config)
    {
        Prepare(config);
        _view.ShowFailure(message);
        Present();
        _view.Announce(message);
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    /// Outlines the Guide in the accent for a moment (S-033). A state, not an animation.
    public void Flash()
    {
        _view.IsFlashing = true;
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    /// Saves what the Guide shows as a PNG, for --check and snapshots. Returns the path.
    public string SavePng(string path)
    {
        UpdateLayout();
        return Render((FrameworkElement)Content, Background, path);
    }

    /// Draws an element on its background into a PNG at the screen's scale.
    internal static string Render(FrameworkElement content, Brush backdrop, string path)
    {
        var scale = VisualTreeHelper.GetDpi(content).DpiScaleX;
        var size = new Size(content.ActualWidth, content.ActualHeight);
        // The window's background first, then the view itself at its own size, padding and all.
        var background = new DrawingVisual();
        using (var context = background.RenderOpen())
        {
            context.DrawRectangle(backdrop, null, new Rect(size));
            // Through a brush, so an element's offset in its popup doesn't crop the picture.
            context.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(size) }, null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(background);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    public new void Hide()
    {
        _flashTimer.Stop();
        _view.IsFlashing = false;
        if (IsVisible) base.Hide();
    }

    private Screen _screen = new(default, default);
    private string _position = "center";

    private void Prepare(LoadedConfig config)
    {
        App.ApplyTheme(config.Config.UI.Theme);
        _screen = ScreenOfForegroundWindow();
        _position = config.Config.UI.Position;
        _view.MaxWidth = GuidePlacement.MaxWidthPoints(_screen);
    }

    private void Present()
    {
        if (!IsVisible) base.Show();
        Fit();
        // The rows only get their final size once laid out: place it again then.
        Dispatcher.BeginInvoke(Fit, DispatcherPriority.Background);
    }

    /// The monitor of the window in front, from the same list the window actions use.
    private static Screen ScreenOfForegroundWindow()
    {
        var screens = new Actions.Win32WindowSystem().Screens();
        var hwnd = Win32.GetForegroundWindow();
        return GuidePlacement.ScreenFor(hwnd == 0 ? null : Actions.Win32WindowSystem.VisibleFrame(hwnd), screens)
            ?? new Screen(default, default);
    }

    /// Puts the window where the config says on the monitor of the window in front.
    /// Sizes and the place are physical pixels, as the window actions use.
    private void Fit()
    {
        if (!IsVisible) return;
        UpdateLayout();
        if (!Win32.GetWindowRect(_hwnd, out var rect)) return;
        var (x, y) = GuidePlacement.Origin(rect.Right - rect.Left, rect.Bottom - rect.Top, _screen, _position, (double)FindResource("GuideEdgeOffset"));
        if (x != rect.Left || y != rect.Top)
            SetWindowPos(_hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private const int GWL_EXSTYLE = -20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;
    private const nint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport("user32.dll")] private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("dwmapi.dll")] private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
