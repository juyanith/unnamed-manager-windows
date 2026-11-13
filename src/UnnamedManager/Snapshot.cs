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
        var pages = new[] { "bindings", "bindings-key", "bindings-error", "actions", "actions-window", "contexts", "help", "empty-help", "settings", "settings-end", "guide", "guide-columns", "guide-back", "guide-home", "guide-home-wide", "guide-error", "guide-flash", "guide-keys", "guide-window", "tray-menu", "tray-menu-warning" }
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
                var broken = page == "bindings-error" ? await BreakConfigAsync() : null;
                // `empty-<page>`: the page with no bindings, actions or contexts, never saved (S-053).
                if (page.StartsWith("empty-")) App.Editor.Preview(new Config { Entries = [] });
                window.Show(page.StartsWith("empty-") ? page["empty-".Length..] : page.StartsWith("bindings") ? "bindings" : page.StartsWith("actions") ? "actions" : page.StartsWith("settings") ? "settings" : page);
                await Task.Delay(800);
                if (window.CurrentPage is Pages.BindingsPage bindings)
                    bindings.Select(bindings.All().FirstOrDefault(i => i.Title == "Terminal"));
                // `settings-end`: Settings scrolled to its last cards.
                if (window.CurrentPage is Pages.SettingsPage settings)
                    SelfTest.Find<Microsoft.UI.Xaml.Controls.ScrollViewer>(settings).First().ChangeView(null, page == "settings-end" ? 10000 : 0, null, true);
                if (window.CurrentPage is Pages.ActionsPage actions)
                    actions.Refresh(page == "actions-window" ? "left-half" : "code");
                // A step key the menu already has, as the recorder shows it (S-040).
                if (page == "bindings-key" && window.CurrentPage is Pages.BindingsPage recorder && recorder.Selected is { } row)
                {
                    recorder.StartRecording(row.Node.Path);
                    recorder.OnRecorded(new Chord(Modifiers.None, KeyName.E));
                }
                await Task.Delay(200);
                window.DrawGridOverlay();
                await Task.Delay(100);
                await CaptureAsync(window, Path.Combine(dir, $"{page}-{mode}.png"));
                if (page.StartsWith("empty-")) App.Editor.EndPreview();
                if (broken is not null)
                {
                    await File.WriteAllTextAsync(App.Config.Path, broken);
                    App.Config.Reload();
                }
            }
        }
        AgentLog.Line($"SNAPSHOT done {dir}");
        AgentLog.Exit(0);
    }

    /// The Guide over the app's own window, as it shows over any app: `guide` is
    /// Commands › Applications, `guide-columns` the 14 rows of Commands › Websites, and
    /// `guide-back` Commands after Back from Applications (S-007), `guide-home` Home after Back
    /// from Commands, and `guide-home-wide` a Home with more than 12 rows (S-021).
    /// `guide-flash` is `guide` in its flash state, for a key that isn't bound (S-033), and
    /// `guide-keys` a config with Q to cancel and Back off (S-037).
    private static async Task CaptureGuideAsync(MainWindow window, string page, string mode, string path)
    {
        if (App.Config.Current is not { } loaded) { AgentLog.Line($"SNAPSHOT {page} skipped: the config didn't load"); return; }
        if (page == "guide-home-wide") loaded = WideHome;
        if (page == "guide-keys") loaded = OwnKeys;
        // The keys each page presses, through the real engine: what shows is what the sequence shows.
        KeyName[] steps = page switch
        {
            "guide-columns" => [KeyName.W],
            "guide-back" => [KeyName.A, KeyName.Backspace],
            "guide-home" or "guide-home-wide" => [KeyName.Backspace],
            "guide-window" => [],
            _ => [KeyName.A],
        };
        var engine = new Core.Keyboard.SequenceEngine(loaded);
        // The app in front is this window, which has nothing for Context Aware rows (S-014).
        engine.SetFrontApp(Actions.AppSwitcher.FrontAppOf(window.Handle()));
        // `guide-window` is the Windows menu on Meh+Space, with each window action's glyph (S-045).
        var shown = engine.Handle(Core.Keyboard.KeyEvent.Down(KeyName.Space, page == "guide-window" ? Modifiers.Meh : Modifiers.Hyper)).Effect;
        engine.ModifiersChanged(Modifiers.None);
        foreach (var key in steps)
        {
            var next = engine.Handle(Core.Keyboard.KeyEvent.Down(key)).Effect;
            if (next is Core.Keyboard.SequenceEffect.ShowMenu or Core.Keyboard.SequenceEffect.ShowHome) shown = next;
        }
        Win32.ForceForeground(window.Handle());
        await Task.Delay(300);
        App.Guide.ThemeOverride = mode;
        switch (shown)
        {
            case not null when page == "guide-error":
                App.Guide.ShowFailure("Can't find Code.exe. Check that it's installed, or use its full path.", loaded);
                break;
            case Core.Keyboard.SequenceEffect.ShowHome home:
                App.Guide.ShowHome(home.Home, home.Path, loaded, home.Front);
                break;
            case Core.Keyboard.SequenceEffect.ShowMenu menu:
                App.Guide.Show(menu.Menu, menu.Path, loaded, menu.Front);
                break;
            default:
                AgentLog.Line($"SNAPSHOT {page} skipped: no menu");
                return;
        }
        await Task.Delay(800);
        // The flash lasts 300 ms; the snapshot holds the state itself.
        if (page == "guide-flash") { App.Guide.View.IsFlashing = true; await Task.Delay(400); }
        GridProbes.Write(App.Guide.View.RootElement, path.Replace(".png", "-layout.json"));
        Check(page, "showing the Guide keeps the window in front", Win32.GetForegroundWindow() == window.Handle());
        CaptureScreen(App.Guide.Handle(), out var pixels, out var width, out var height);
        App.Guide.Hide();
        await SavePngAsync(pixels, width, height, path);
    }

    /// For `bindings-error`: a binding whose action isn't defined, so the file doesn't
    /// load and the last valid config stays. Returns the file as it was.
    private static async Task<string> BreakConfigAsync()
    {
        var text = await File.ReadAllTextAsync(App.Config.Path);
        await File.WriteAllTextAsync(App.Config.Path, text.Replace("\"entries\": [",
            "\"entries\": [\n    { \"keys\": \"Hyper+N\", \"title\": \"Quick note\", \"action\": \"quick-note\" },"));
        App.Config.Reload();
        return text;
    }

    /// `cancelKey` Q and `backKey` "" (Back off), for `guide-keys`.
    private static LoadedConfig OwnKeys => ConfigLoader.Load("""
        { "version": 1, "runtime": { "cancelKey": "Q", "backKey": "" }, "entries": [
          { "keys": "Hyper+Space", "title": "Commands" }, { "keys": "Hyper+Space A", "title": "Applications" },
          { "keys": "Hyper+Space A T", "title": "Terminal", "action": "x" }, { "keys": "Hyper+Space A E", "title": "Editor", "action": "x" } ],
          "actions": { "x": { "type": "noop" } } }
        """).Loaded!;

    /// A config with 16 top-level entries in three modifier groups, for `guide-home-wide`.
    private static LoadedConfig WideHome => ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "Hyper+Space", "title": "Commands" }, { "keys": "Hyper+Space F", "title": "Find", "action": "x" },
          { "keys": "Hyper+E", "title": "Editor", "action": "x" }, { "keys": "Hyper+B", "title": "Browser", "action": "x" },
          { "keys": "Hyper+M", "title": "Music", "action": "x" }, { "keys": "Hyper+C", "title": "Calendar", "action": "x" },
          { "keys": "Hyper+Z", "title": "Zoom", "action": "x" },
          { "keys": "Meh+Space", "title": "Windows" }, { "keys": "Meh+Space M", "title": "Maximize", "action": "x" },
          { "keys": "Meh+Left", "title": "Left half", "action": "x" }, { "keys": "Meh+Right", "title": "Right half", "action": "x" },
          { "keys": "Meh+Up", "title": "Top half", "action": "x" }, { "keys": "Meh+Down", "title": "Bottom half", "action": "x" },
          { "keys": "Ctrl+Alt+K", "title": "Keyboard layout", "action": "x" }, { "keys": "Ctrl+Alt+V", "title": "Clipboard history", "action": "x" },
          { "keys": "F13", "title": "Notes", "action": "x" }, { "keys": "F14", "title": "Scratchpad", "action": "x" } ],
          "actions": { "x": { "type": "noop" } } }
        """).Loaded!;

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
        var nothing = new Tray.TrayCommands(_ => { }, () => { }, () => { }, () => { }, () => { }, () => { }, _ => { });
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
