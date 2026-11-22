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
        var pages = new[] { "bindings", "bindings-key", "bindings-error", "actions", "actions-window", "actions-focus", "actions-swap", "actions-arrange", "actions-move", "actions-contextual", "actions-contextual-fallback", "bindings-builtin", "bindings-list", "bindings-direct", "bindings-repeat", "bindings-contents", "bindings-icon", "contexts", "help", "help-builtin", "empty-help", "empty-bindings", "empty-actions", "empty-contexts", "settings", "settings-windows", "settings-end", "guide", "guide-columns", "guide-back", "guide-home", "guide-home-wide", "guide-error", "guide-flash", "guide-keys", "guide-window", "guide-held", "guide-recent", "guide-recent-empty", "guide-recent-more", "guide-icons", "tray-menu", "tray-menu-warning", "welcome" }
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
                if (page == "welcome")
                {
                    await CaptureWelcomeAsync(mode, Path.Combine(dir, $"{page}-{mode}.png"));
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
                window.Show(page.StartsWith("empty-") ? page["empty-".Length..] : page.StartsWith("bindings") ? "bindings" : page.StartsWith("actions") ? "actions" : page.StartsWith("settings") ? "settings" : page.StartsWith("help") ? "help" : page);
                await Task.Delay(800);
                if (window.CurrentPage is Pages.BindingsPage bindings)
                    bindings.Select(page == "bindings-direct"
                        ? bindings.All().LastOrDefault(i => i.Title == "Terminal")
                        : bindings.All().FirstOrDefault(i => i.Title == (page switch { "bindings-builtin" => "Swap left", "bindings-list" => "Recent windows", "bindings-contents" or "bindings-icon" => "Applications", _ => "Terminal" })));
                // `settings-end`: Settings scrolled to its last cards.
                if (window.CurrentPage is Pages.SettingsPage settings)
                    SelfTest.Find<Microsoft.UI.Xaml.Controls.ScrollViewer>(settings).First().ChangeView(null, page switch { "settings-end" => 10000, "settings-windows" => settings.WindowsOffset, _ => 0 }, null, true);
                // `help-builtin`: Help scrolled to its built-in actions, with Place Window open (S-071).
                if (window.CurrentPage is Pages.HelpPage help)
                {
                    if (page == "help-builtin") help.OpenBuiltIn("Place Window");
                    await Task.Delay(300);
                    SelfTest.Find<Microsoft.UI.Xaml.Controls.ScrollViewer>(help).First().ChangeView(null, page == "help-builtin" ? help.BuiltInOffset : 0, null, true);
                }
                if (window.CurrentPage is Pages.ActionsPage actions)
                    actions.Refresh(page switch { "actions-window" => "left-half", "actions-focus" => "focus-recent", "actions-swap" => "swap-recent", "actions-arrange" => "arrange-all", "actions-move" => "move-center", "actions-contextual" or "actions-contextual-fallback" => "address-bar", _ => "code" });
                // A second context with no variant, undone after (S-096).
                if (page == "actions-contextual" && window.CurrentPage is Pages.ActionsPage)
                {
                    App.Editor.Change("Context", c => c.AddContext("editors").SetContextApps("editors", ["Code.exe", "notepad.exe"]));
                    await Task.Delay(700);
                }
                // A fallback, scrolled to, undone after (S-097).
                if (page == "actions-contextual-fallback" && window.CurrentPage is Pages.ActionsPage fallbackPage)
                {
                    App.Editor.Change("Fallback", c => c.SetFallback("address-bar", new SendShortcutAction(KeyPath.Parse("Primary+F")!)));
                    await Task.Delay(700);
                    fallbackPage.ScrollFormToEnd();
                    await Task.Delay(300);
                }
                // A held menu's repeat key on a key under it: not saved, and undone after (S-091).
                if (page == "bindings-repeat" && window.CurrentPage is Pages.BindingsPage clash)
                {
                    App.Editor.Change("Hold to choose", c => c.ChangeKey(KeyPath.Parse("Hyper+Space F")!, new Chord(Modifiers.None, KeyName.Space), out _)
                        .SetHold(KeyPath.Parse("Hyper+Space")!, true));
                    clash.Refresh(KeyPath.Parse("Hyper+Space"));
                    await Task.Delay(700);
                }
                // An icon of its own, undone after (S-095).
                if (page == "bindings-icon" && window.CurrentPage is Pages.BindingsPage { Selected: not null } iconPage)
                {
                    iconPage.SetIcon("U+E71D");
                    await Task.Delay(600);
                }
                // A step key the menu already has, as the recorder shows it (S-040).
                if (page == "bindings-key" && window.CurrentPage is Pages.BindingsPage recorder && recorder.Selected is { } row)
                {
                    recorder.StartRecording(row.Node.Path);
                    recorder.OnRecorded(new Chord(Modifiers.None, KeyName.E));
                }
                // A switch that changed slides for a moment; `bindings-list` waits for Hold to choose to settle.
                await Task.Delay(page == "bindings-list" ? 800 : 200);
                window.DrawGridOverlay();
                await Task.Delay(100);
                await CaptureAsync(window, Path.Combine(dir, $"{page}-{mode}.png"));
                if (page is "bindings-repeat" or "bindings-icon" or "actions-contextual" or "actions-contextual-fallback") App.Editor.Undo();
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
        if (page == "guide-icons")
        {
            loaded = OwnIcons;
            UnnamedManager.Guide.GuideIconCache.Prefetch(loaded);
            for (var i = 0; i < 40 && !UnnamedManager.Guide.GuideIconCache.Idle; i++) await Task.Delay(50);
        }
        if (page is "guide-held" or "guide-recent") { await CaptureHeldAsync(window, page, mode, path); return; }
        if (page is "guide-recent-empty" or "guide-recent-more") { await CaptureListAsync(window, page, mode, path); return; }
        // The keys each page presses, through the real engine: what shows is what the sequence shows.
        KeyName[] steps = page switch
        {
            "guide-columns" => [KeyName.W],
            "guide-back" => [KeyName.A, KeyName.Backspace],
            "guide-home" or "guide-home-wide" => [KeyName.Backspace],
            "guide-window" or "guide-icons" => [],
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

    /// `guide-recent-empty` and `guide-recent-more`: a list that isn't held, with nothing
    /// in it and with three pages (S-100, S-101).
    private static async Task CaptureListAsync(MainWindow window, string page, string mode, string path)
    {
        var loaded = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Hyper+R", "title": "Recent windows", "list": { "type": "windows.recent", "count": 20 } } ] }
            """).Loaded!;
        var count = page == "guide-recent-more" ? 20 : 0;
        var list = loaded.Leaders[0].Filled([.. Enumerable.Range(1, count).Select(i => new ListItem(i, $"Window {i}"))], null);
        Win32.ForceForeground(window.Handle());
        await Task.Delay(300);
        App.Guide.ThemeOverride = mode;
        App.Guide.Show(list, [.. list.PathTitles], loaded);
        await Task.Delay(800);
        GridProbes.Write(App.Guide.View.RootElement, path.Replace(".png", "-layout.json"));
        CaptureScreen(App.Guide.Handle(), out var pixels, out var width, out var height);
        App.Guide.Hide();
        await SavePngAsync(pixels, width, height, path);
    }

    /// `guide-held`: a held menu with the highlight on its second item; `guide-recent`:
    /// Recent Windows, held, with a window of each kind the Guide marks (spec v18).
    private static async Task CaptureHeldAsync(MainWindow window, string page, string mode, string path)
    {
        var loaded = ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "CAG+A", "title": "Apps", "hold": true },
              { "keys": "CAG+A F", "title": "Fill", "action": "window-fill" },
              { "keys": "CAG+A M", "title": "Minimize", "action": "window-minimize" },
              { "keys": "CAG+A R", "title": "Arrange", "action": "window-arrange" },
              { "keys": "CAG+A H", "title": "Help", "action": "help" },
              { "keys": "Alt+Tab", "title": "Recent windows", "hold": true, "list": { "type": "windows.recent" } } ] }
            """).Loaded!;
        var engine = new Core.Keyboard.SequenceEngine(loaded);
        Core.Keyboard.SequenceEffect? shown;
        if (page == "guide-held")
        {
            engine.Handle(Core.Keyboard.KeyEvent.Down(KeyName.A, Modifiers.Cag));
            shown = engine.Handle(Core.Keyboard.KeyEvent.Down(KeyName.A, Modifiers.Cag)).Effect;
        }
        else
        {
            engine.Handle(Core.Keyboard.KeyEvent.Down(KeyName.Tab, Modifiers.Alt));
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string explorer = Path.Combine(windows, "explorer.exe"), notepad = Path.Combine(windows, "System32", "notepad.exe"), cmd = Path.Combine(windows, "System32", "cmd.exe");
            ListItem[] items =
            [
                new(1, "Downloads", explorer),
                new(2, "Weekly planning.txt - Notepad", notepad, ItemMarks.OtherDesktop),
                new(3, "Command Prompt", cmd, ItemMarks.Minimized),
                new(4, "Draft of the quarterly report for the team.txt - Notepad", notepad, ItemMarks.OtherDesktop | ItemMarks.OtherDisplay),
            ];
            var front = new ListItem(0, "Documents", explorer);
            UnnamedManager.Guide.GuideIconCache.Load([new IconSource.Exe(explorer), new IconSource.Exe(notepad), new IconSource.Exe(cmd)]);
            for (var i = 0; i < 40 && !UnnamedManager.Guide.GuideIconCache.Idle; i++) await Task.Delay(50);
            shown = engine.Fill(items, front).FirstOrDefault();
        }
        Win32.ForceForeground(window.Handle());
        await Task.Delay(300);
        App.Guide.ThemeOverride = mode;
        if (shown is not Core.Keyboard.SequenceEffect.ShowHeld held) { AgentLog.Line($"SNAPSHOT {page} skipped: not held"); return; }
        App.Guide.Show(held.Menu, held.Path, loaded, null, held.Highlight);
        await Task.Delay(800);
        GridProbes.Write(App.Guide.View.RootElement, path.Replace(".png", "-layout.json"));
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

    /// Entries' own icons, for `guide-icons` (S-093).
    private static LoadedConfig OwnIcons => ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "Hyper+Space", "title": "Commands" },
          { "keys": "Hyper+Space H", "title": "Home folder", "icon": "U+E80F", "action": "x" },
          { "keys": "Hyper+Space S", "title": "Search", "icon": "U+E721", "action": "x" },
          { "keys": "Hyper+Space M", "title": "Music", "icon": "U+E8D6" },
          { "keys": "Hyper+Space M P", "title": "Play", "action": "x" },
          { "keys": "Hyper+Space G", "title": "Games" },
          { "keys": "Hyper+Space G C", "title": "Chess", "action": "x" },
          { "keys": "Hyper+Space N", "title": "Notes file", "icon": "C:\\Windows\\win.ini", "action": "x" },
          { "keys": "Hyper+Space X", "title": "No such glyph", "icon": "U+0E01", "action": "x" },
          { "keys": "Hyper+Space L", "title": "Left half", "action": "window-place-left-half" } ],
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
            status = status with { Error = broken.ErrorSummary, ElevatedApp = "Task Manager" };
        }
        Win32.GetWindowRect(window.Handle(), out var main);
        var nothing = new Tray.TrayCommands(_ => { }, () => { }, () => { }, () => { }, () => { }, () => { }, _ => { });
        // The menu's popup is a window that wasn't there before it opened: a popup from a
        // menu opened earlier can stay visible, drawn gray.
        var before = new HashSet<nint>();
        Actions.AppSwitcher.EnumTopLevel(hwnd => { before.Add(hwnd); return true; });
        App.TrayMenu.Open(main.Left + main.Width / 2, main.Top + main.Height / 3, status, nothing, keyboard: false);
        // The pointer may rest on a row: its tooltip would cover the menu.
        foreach (var item in App.TrayMenu.Menu!.Items) Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(item, null);
        // After a theme switch the popup can take over a second to draw.
        await Task.Delay(1800);
        nint popup = 0;
        var biggest = 0;
        var known = new[] { window.Handle(), App.Guide.Handle(), App.TrayMenu.HostHandle, App.Tray.Handle };
        Actions.AppSwitcher.EnumTopLevel(hwnd =>
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != Environment.ProcessId || !Win32.IsWindowVisible(hwnd) || known.Contains(hwnd) || before.Contains(hwnd)) return true;
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

    /// Welcome with its first step done and the other two to do (S-058).
    private static async Task CaptureWelcomeAsync(string mode, string path)
    {
        var welcome = new Welcome.WelcomeWindow();
        welcome.ApplyTheme(mode);
        welcome.Show(new Core.WelcomeStatus(OpenedConfig: true, OfficeShortcutsOn: true, RanSequence: false));
        welcome.Activate();
        await Task.Delay(1000);
        await CaptureAsync(welcome, welcome.RootElement, path);
        welcome.Close();
        await Task.Delay(200);
    }

    public static Task CaptureAsync(MainWindow window, string path) => CaptureAsync(window, window.RootElement, path);

    /// Captures the real window with PrintWindow(PW_RENDERFULLCONTENT), so Mica, the title bar,
    /// and every control look as on screen, even when another window covers this one.
    /// Crops the invisible resize border with the DWM frame bounds.
    public static async Task CaptureAsync(Microsoft.UI.Xaml.Window window, Microsoft.UI.Xaml.FrameworkElement root, string path)
    {
        var hwnd = window.Handle();
        // An active window, like one someone is using: a bright title bar and full Mica.
        Win32.ForceForeground(hwnd);
        await Task.Delay(150);

        GridProbes.Write(root, path.Replace(".png", "-layout.json"));

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
