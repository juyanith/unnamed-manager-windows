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
        var pages = new[] { "settings", "guide", "guide-columns" }
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
    /// Commands › Applications, `guide-columns` the 14 rows of Commands › Websites.
    private static async Task CaptureGuideAsync(MainWindow window, string page, string mode, string path)
    {
        if (App.Config.Current is not { } loaded) { AgentLog.Line($"SNAPSHOT {page} skipped: the config didn't load"); return; }
        var leader = loaded.Leaders[0];
        var menu = leader.Children.First(c => c.IsMenu && c.Title == (page == "guide" ? "Applications" : "Websites"));
        Win32.ForceForeground(window.Handle());
        await Task.Delay(300);
        App.Guide.ThemeOverride = mode;
        App.Guide.Show(menu, [leader.Title, menu.Title], loaded);
        await Task.Delay(800);
        GridProbes.Write(App.Guide.View.RootElement, path.Replace(".png", "-layout.json"));
        Check(page, "showing the Guide keeps the window in front", Win32.GetForegroundWindow() == window.Handle());
        CaptureScreen(App.Guide.Handle(), out var pixels, out var width, out var height);
        App.Guide.Hide();
        await SavePngAsync(pixels, width, height, path);
    }

    private static void Check(string page, string what, bool ok) => AgentLog.Line($"SNAPSHOT {page}: {(ok ? "" : "FAIL ")}{what}");

    /// Copies a window's frame from the screen, as composed: acrylic, the shadow's
    /// edge and whatever is behind. For windows that must not be activated.
    private static void CaptureScreen(nint hwnd, out byte[] pixels, out int width, out int height)
    {
        Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, System.Runtime.InteropServices.Marshal.SizeOf<Win32.RECT>());
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
