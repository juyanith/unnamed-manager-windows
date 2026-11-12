using UnnamedManager.Core.Config;
using UnnamedManager.UI;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace UnnamedManager;

/// Screenshot mode for agents.
///   $env:APP_STORE="$env:TEMP\UnnamedManager-test\config.json"; $env:APP_SNAPSHOT="$env:TEMP\shots"; UnnamedManager.exe
/// writes <page>-light.png and <page>-dark.png for every page (Settings, until M5 adds the Bindings pages), then quits.
/// Each PNG gets a <page>-<mode>-layout.json with the grid probes (tools/check-layout.ps1).
/// APP_SNAPSHOT_PAGES=settings limits the pages. APP_ACCENT=<id> shows another accent.
public static class Snapshot
{
    public static bool Requested => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_SNAPSHOT"));

    public static async Task RunIfRequestedAsync(MainWindow window)
    {
        if (!Requested) return;
        var dir = Environment.GetEnvironmentVariable("APP_SNAPSHOT")!;
        var only = Environment.GetEnvironmentVariable("APP_SNAPSHOT_PAGES")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var pages = new[] { "settings", "guide", "guide-columns", "guide-back", "guide-error", "tray-menu", "tray-menu-warning" }
            .Where(p => only is null || only.Contains(p)).ToList();

        // A sleeping display renders nothing. Keep it on until the run quits.
        Win32.SetThreadExecutionState(Win32.ES_CONTINUOUS | Win32.ES_DISPLAY_REQUIRED | Win32.ES_SYSTEM_REQUIRED);
        Directory.CreateDirectory(dir);
        await Task.Delay(1500);
        foreach (var mode in new[] { "light", "dark" })
        {
            window.ApplyTheme(mode);
            foreach (var page in pages)
            {
                if (page.StartsWith("tray-menu"))
                {
                    await CaptureTrayMenuAsync(window, page, mode, Path.Combine(dir, $"{page}-{mode}.png"));
                    continue;
                }
                if (page.StartsWith("guide"))
                {
                    await CaptureGuideAsync(window, page, mode, Path.Combine(dir, $"{page}-{mode}.png"));
                    continue;
                }
                window.Show(page);
                await Task.Delay(800);
                window.DrawGridOverlay();
                await Task.Delay(100);
                await CaptureAsync(window, Path.Combine(dir, $"{page}-{mode}.png"));
            }
        }
        AgentLog.Line($"SNAPSHOT done {dir}");
        AgentLog.Exit(0);
    }

    /// The Guide over the app's own window, as it shows over any app: `guide` is
    /// Commands › Applications, `guide-columns` the 14 rows of Commands › Websites, and
    /// `guide-back` Commands after Back from Applications (S-007).
    private static async Task CaptureGuideAsync(MainWindow window, string page, string mode, string path)
    {
        if (App.Config.Current is not { } loaded) { AgentLog.Line($"SNAPSHOT {page} skipped: the config didn't load"); return; }
        // The keys each page presses, through the real engine: what shows is what the sequence shows.
        KeyName[] steps = page switch
        {
            "guide-columns" => [KeyName.W],
            "guide-back" => [KeyName.A, KeyName.Backspace],
            _ => [KeyName.A],
        };
        var engine = new Core.Keyboard.SequenceEngine(loaded);
        // The app in front is this window, which has nothing for Context Aware rows (S-014).
        engine.SetFrontApp(Actions.AppSwitcher.FrontAppOf(window.Handle()));
        var shown = engine.Handle(Core.Keyboard.KeyEvent.Down(KeyName.Space, Modifiers.Hyper)).Effect as Core.Keyboard.SequenceEffect.ShowMenu;
        engine.ModifiersChanged(Modifiers.None);
        foreach (var key in steps)
            if (engine.Handle(Core.Keyboard.KeyEvent.Down(key)).Effect is Core.Keyboard.SequenceEffect.ShowMenu next) shown = next;
        if (shown is null) { AgentLog.Line($"SNAPSHOT {page} skipped: no menu"); return; }
        Win32.ForceForeground(window.Handle());
        await Task.Delay(300);
        App.Guide.ThemeOverride = mode;
        if (page == "guide-error")
            App.Guide.ShowFailure("Can't find Code.exe. Check that it's installed, or use its full path.", loaded);
        else
            App.Guide.Show(shown.Menu, shown.Path, loaded, shown.Front);
        await Task.Delay(800);
        GridProbes.Write(App.Guide.View.RootElement, path.Replace(".png", "-layout.json"));
        Check(page, "showing the Guide keeps the window in front", Win32.GetForegroundWindow() == window.Handle());
        CaptureScreen(App.Guide.Handle(), out var pixels, out var width, out var height);
        App.Guide.Hide();
        await SavePngAsync(pixels, width, height, path);
    }

    /// The tray menu as it opens, all OK (`tray-menu`) or with a config error
    /// (`tray-menu-warning`). It draws in a popup window of its own: the app's one
    /// visible window that isn't the main window, the Guide or the menu's host.
    private static async Task CaptureTrayMenuAsync(MainWindow window, string page, string mode, string path)
    {
        var status = App.TrayStatusNow with { Theme = mode };
        if (page == "tray-menu-warning")
        {
            var broken = new ConfigState();
            broken.Apply(ConfigLoader.Load("""
                { "version": 1, "entries": [
                  { "keys": "Hyper+Space", "title": "Commands" },
                  { "keys": "Hyper+Space Q", "title": "Quit", "action": "open-foo" },
                  { "keys": "Hyper+Space W", "title": "W", "action": "b" },
                  { "keys": "Hyper+Space E", "title": "E", "action": "c" } ] }
                """), DateTimeOffset.Now);
            status = status with { Error = broken.ErrorSummary };
        }
        Win32.GetWindowRect(window.Handle(), out var main);
        var nothing = new Tray.TrayCommands(_ => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        App.TrayMenu.Open(main.Left + main.Width / 2, main.Top + main.Height / 3, status, nothing, keyboard: false);
        await Task.Delay(900);
        nint popup = 0;
        var biggest = 0;
        var known = new[] { window.Handle(), App.Guide.Handle(), App.TrayMenu.HostHandle, App.Tray.Handle };
        Actions.AppSwitcher.EnumTopLevel(hwnd =>
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != Environment.ProcessId || !Win32.IsWindowVisible(hwnd) || known.Contains(hwnd)) return true;
            Win32.GetWindowRect(hwnd, out var r);
            if (r.Width * r.Height > biggest) { biggest = r.Width * r.Height; popup = hwnd; }
            return true;
        });
        Check(page, "the tray menu opens", App.TrayMenu.IsOpen && popup != 0);
        if (popup != 0)
        {
            // A popup's window bounds don't quite match what it draws: take a margin round it.
            CaptureScreen(popup, out var pixels, out var width, out var height, margin: 16);
            await SavePngAsync(pixels, width, height, path);
        }
        App.TrayMenu.Close();
        await Task.Delay(300);
    }

    private static void Check(string page, string what, bool ok) => AgentLog.Line($"SNAPSHOT {page}: {(ok ? "" : "FAIL ")}{what}");

    /// Copies a window's frame from the screen, as composed: acrylic, the shadow's
    /// edge and whatever is behind. For windows that must not be activated.
    private static void CaptureScreen(nint hwnd, out byte[] pixels, out int width, out int height, int margin = 0)
    {
        Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, System.Runtime.InteropServices.Marshal.SizeOf<Win32.RECT>());
        frame = new Win32.RECT { Left = frame.Left - margin, Top = frame.Top - margin, Right = frame.Right + margin, Bottom = frame.Bottom + margin };
        width = frame.Width;
        height = frame.Height;
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        var screen = Win32.GetDC(0);
        var dc = Win32.CreateCompatibleDC(screen);
        var bitmap = Win32.CreateDIBSection(dc, ref header, 0, out var bits, 0, 0);
        var old = Win32.SelectObject(dc, bitmap);
        try
        {
            Win32.BitBlt(dc, 0, 0, width, height, screen, frame.Left, frame.Top, Win32.SRCCOPY);
            pixels = new byte[width * height * 4];
            System.Runtime.InteropServices.Marshal.Copy(bits, pixels, 0, pixels.Length);
        }
        finally
        {
            Win32.SelectObject(dc, old);
            Win32.DeleteObject(bitmap);
            Win32.DeleteDC(dc);
            Win32.ReleaseDC(0, screen);
        }
    }

    /// Captures the real window with PrintWindow(PW_RENDERFULLCONTENT), so Mica, the title bar,
    /// and every control look as on screen, even when another window covers this one.
    /// Crops the invisible resize border with the DWM frame bounds.
    public static async Task CaptureAsync(MainWindow window, string path)
    {
        var hwnd = window.Handle();
        // An active window, like one someone is using: a bright title bar and full Mica.
        Win32.ForceForeground(hwnd);
        await Task.Delay(150);

        GridProbes.Write(window.RootElement, path.Replace(".png", "-layout.json"));

        Win32.GetWindowRect(hwnd, out var outer);
        Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, System.Runtime.InteropServices.Marshal.SizeOf<Win32.RECT>());
        int width = outer.Width, height = outer.Height;
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        var screen = Win32.GetDC(0);
        var dc = Win32.CreateCompatibleDC(screen);
        var bitmap = Win32.CreateDIBSection(dc, ref header, 0, out var bits, 0, 0);
        var old = Win32.SelectObject(dc, bitmap);
        byte[] pixels;
        int cropX = frame.Left - outer.Left, cropY = frame.Top - outer.Top;
        int cropW = Math.Min(frame.Width, width - cropX), cropH = Math.Min(frame.Height, height - cropY);
        try
        {
            if (!Win32.PrintWindow(hwnd, dc, Win32.PW_RENDERFULLCONTENT))
            {
                AgentLog.Line($"SNAPSHOT {path} failed: PrintWindow returned false");
                return;
            }
            pixels = new byte[cropW * cropH * 4];
            unsafe
            {
                var source = (byte*)bits;
                for (var y = 0; y < cropH; y++)
                    new ReadOnlySpan<byte>(source + ((y + cropY) * width + cropX) * 4, cropW * 4).CopyTo(pixels.AsSpan(y * cropW * 4));
            }
        }
        finally
        {
            Win32.SelectObject(dc, old);
            Win32.DeleteObject(bitmap);
            Win32.DeleteDC(dc);
            Win32.ReleaseDC(0, screen);
        }

        await SavePngAsync(pixels, cropW, cropH, path);
    }

    private static async Task SavePngAsync(byte[] pixels, int width, int height, string path)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        var png = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(png);
        await File.WriteAllBytesAsync(path, png);
        AgentLog.Line($"SNAPSHOT {path}");
    }
}
