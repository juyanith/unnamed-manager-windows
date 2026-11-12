using UnnamedManager.Core.Config;
using UnnamedManager.Keyboard;
using UnnamedManager.UI;
using UnnamedManager.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace UnnamedManager;

/// Self-test for agents: runs the app's real functions on a test config,
/// prints PASS or FAIL per check, and exits with code 0 or 1.
///   $env:APP_STORE="$env:TEMP\UnnamedManager-self\config.json"; UnnamedManager.exe --selftest
/// (tools/selftest.ps1 does this, starting with no file.) Add a check for every story with logic.
public static class SelfTest
{
    public static bool Requested => Environment.GetCommandLineArgs().Contains("--selftest");

    private static int _failures, _count;

    public static async Task RunIfRequestedAsync(MainWindow window)
    {
        if (!Requested) return;
        await Task.Delay(500);

        // S-002: no file at APP_STORE, so the app wrote the starter config and loaded it.
        var config = App.Config;
        Check("the config file is the one APP_STORE names", config.Path == Environment.GetEnvironmentVariable("APP_STORE"));
        Check("with no file, the app writes the starter config", config.CreatedStarter && File.Exists(config.Path));
        Check("the starter config loads", config.Current is not null && config.Errors.Count == 0,
            string.Join(" | ", config.Errors));
        var leader = config.Current?.Leaders.SingleOrDefault();
        Check("the starter has the leader Commands on Hyper+Space with F and T",
            leader is { Title: "Commands" } && leader.Path.ToString() == "Hyper+Space"
            && string.Join(",", leader.Children.Select(c => $"{c.Path.LastStep} {c.Title}")) == "F File Explorer,T Terminal");
        Check("F opens File Explorer by exe", config.Current?.Config.Action("open-explorer") is UseApplicationAction { Exe: "explorer.exe" });
        Check("T opens Windows Terminal by appId",
            config.Current?.Config.Action("open-terminal") is UseApplicationAction { AppId: "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" });
        var schema = Path.Combine(Path.GetDirectoryName(config.Path)!, ConfigLoader.SchemaFileName);
        Check("the schema is written next to the starter", File.Exists(schema) && File.ReadAllText(schema) == ConfigLoader.SchemaJson);
        Check("the starter points to the schema", config.Current?.Config.Schema == "./" + ConfigLoader.SchemaFileName);

        var mine = "{ \"version\": 1, \"entries\": [] }";
        await File.WriteAllTextAsync(config.Path, mine);
        Check("the starter never writes over a file", !ConfigLoader.WriteStarterIfMissing(config.Path) && await File.ReadAllTextAsync(config.Path) == mine);
        var again = new ConfigStore(config.Path);
        again.Load();
        Check("a broken file is read, not replaced, and its error names the rule",
            !again.CreatedStarter && again.Current is null && again.Errors.SingleOrDefault()?.Rule == Rule.NoEntries);

        await CheckKeysAsync();
        await CheckGuideAsync(window);
        await CheckUseApplicationAsync(window);
        await CheckTrayAsync();
        await CheckSingleCopyAsync();

        window.Show("settings");
        await Task.Delay(300);
        Check("Settings opens", Find<SettingsPage>(window.RootElement).Any());
        foreach (var button in Find<AppBarButton>(window.RootElement))
        {
            var shortcut = button.KeyboardAccelerators.Count > 0 || !string.IsNullOrEmpty(button.KeyboardAcceleratorTextOverride);
            Check($"command '{button.Label}' has a label and a shortcut", !string.IsNullOrEmpty(button.Label) && shortcut);
        }

        // Last: Exit removes the hook and the tray icon before the app goes.
        App.Shutdown(exitProcess: false);
        Check("Exit removes the hook and the tray icon first", !App.Keys.IsCapturing && !App.Tray.IsAdded);

        AgentLog.Line(_failures == 0 ? $"SELFTEST PASS {_count} checks" : $"SELFTEST FAIL {_failures} of {_count} checks");
        AgentLog.Exit(_failures == 0 ? 0 : 1);
    }

    /// S-003 and S-004 through the real hook. The test config's leader is F24 with
    /// F23 under it, keys no one types, so a key that slips through does nothing.
    private static async Task CheckKeysAsync()
    {
        var keys = App.Keys;
        Check("the keyboard hook is installed", keys.IsCapturing);
        if (keys.Hook is not { } hook || keys.Tracker is not { } tracker) return;

        // The callback's whole job, timed: 1,000 key events while idle, after a warm-up so
        // compiling the code once isn't counted.
        static KeyboardHook.KBDLLHOOKSTRUCT E(int i) => new() { vkCode = 0x45, scanCode = 0x12, flags = i % 2 == 0 ? 0 : KeyboardHook.LLKHF_UP };
        for (var i = 0; i < 200; i++) hook.Decide(E(i));
        var times = new double[1000];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < times.Length; i++)
        {
            var start = watch.Elapsed.TotalMilliseconds;
            hook.Decide(E(i));
            times[i] = watch.Elapsed.TotalMilliseconds - start;
        }
        Array.Sort(times);
        Check("the hook callback returns in under 1 ms for 1,000 events", times.Average() < 1 && times[989] < 1,
            $"average {times.Average():0.0000} ms, 99th percentile {times[989]:0.0000} ms, slowest {times[^1]:0.000} ms");
        AgentLog.Line($"HOOK timing: average {times.Average():0.0000} ms, 99th percentile {times[989]:0.0000} ms, slowest {times[^1]:0.000} ms");
        Check("events marked as the app's own pass through",
            !hook.Decide(new KeyboardHook.KBDLLHOOKSTRUCT { vkCode = 0x87, scanCode = 0x76, dwExtraInfo = KeyboardHook.Marker }));

        var test = ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "F24", "title": "Test" },
              { "keys": "F24 F22", "title": "Inner" },
              { "keys": "F24 F22 F23", "title": "Deep", "action": "x" } ],
              "actions": { "x": { "type": "noop" } } }
            """).Loaded!;
        var real = App.Config.Current!;
        keys.Start(test);
        var seen = new List<Core.Keyboard.SequenceEffect>();
        void Record(Core.Keyboard.SequenceEffect e) => seen.Add(e);
        keys.Effect += Record;
        try
        {
            foreach (var key in new[] { KeyName.F24, KeyName.F22, KeyName.F23 })
            {
                KeyboardHook.InjectForTest(key, up: false);
                KeyboardHook.InjectForTest(key, up: true);
            }
            for (var i = 0; i < 20 && seen.Count < 3; i++) await Task.Delay(50);
            Check("a leader pressed anywhere starts a sequence",
                seen.ElementAtOrDefault(0) is Core.Keyboard.SequenceEffect.ShowMenu { Menu.Title: "Test" }, $"{seen.Count} effects");
            Check("a step key opens the child menu",
                seen.ElementAtOrDefault(1) is Core.Keyboard.SequenceEffect.ShowMenu m && string.Join(" › ", m.Path) == "Test › Inner");
            Check("a binding's key ends the sequence with that binding",
                seen.ElementAtOrDefault(2) is Core.Keyboard.SequenceEffect.Run { Binding.Title: "Deep" } && !tracker.IsActive);
        }
        finally
        {
            keys.Effect -= Record;
            keys.Start(real);
        }

        keys.Stop();
        Check("turning capture off removes the hook", !keys.IsCapturing);
        keys.Start(real);
        Check("turning it back on installs it again", keys.IsCapturing);
    }

    /// S-005: the Guide shows without taking the foreground, and stays out of the taskbar and Alt+Tab.
    private static async Task CheckGuideAsync(MainWindow window)
    {
        if (App.Config.Current is not { } loaded) return;
        UI.Win32.ForceForeground(window.Handle());
        await Task.Delay(200);
        var front = UI.Win32.GetForegroundWindow();
        var leader = loaded.Leaders[0];
        App.Guide.Show(leader, [leader.Title], loaded);
        await Task.Delay(300);
        var guide = App.Guide.Handle();
        Check("the Guide shows", App.Guide.IsShown && UI.Win32.IsWindowVisible(guide));
        Check("showing the Guide never changes the foreground window", UI.Win32.GetForegroundWindow() == front && front != guide);
        var ex = Guide.GuideWindow.GetWindowLongPtrW(guide, -20);
        Check("the Guide is never activated, topmost, and not in the taskbar",
            (ex & Guide.GuideWindow.WS_EX_NOACTIVATE) != 0 && (ex & Guide.GuideWindow.WS_EX_TOOLWINDOW) != 0
            && (ex & Guide.GuideWindow.WS_EX_TOPMOST) != 0 && (ex & Guide.GuideWindow.WS_EX_APPWINDOW) == 0);
        Check("the Guide is not in Alt+Tab", !App.Guide.AppWindow.IsShownInSwitchers);
        var texts = Find<TextBlock>(App.Guide.View.RootElement).Select(t => t.Text).ToList();
        Check("the Guide shows the path and one row per child, in file order",
            texts.Contains("Commands") && texts.IndexOf("File Explorer") < texts.IndexOf("Terminal") && texts.IndexOf("File Explorer") >= 0,
            string.Join(", ", texts));
        Check("the footer shows Esc Cancel and Backspace Back",
            texts.Contains("Esc") && texts.Contains("Cancel") && texts.Contains("Backspace") && texts.Contains("Back"));
        App.Guide.Hide();
        await Task.Delay(100);
        Check("the Guide hides", !App.Guide.IsShown);

        var rows = Enumerable.Range(0, 30).Select(i => new Guide.GuideRow(KeyName.A, $"Row {i}", false)).ToList();
        Check("up to 12 rows make one column, more two, more than 24 three",
            Guide.GuideView.Columns(rows[..12]).Count == 1 && Guide.GuideView.Columns(rows[..13]).Count == 2
            && Guide.GuideView.Columns(rows[..24]).Count == 2 && Guide.GuideView.Columns(rows[..25]).Count == 3
            && Guide.GuideView.Columns(rows).Count == 3);
    }

    /// S-006 against the app's own window, so nothing outside the app is started or moved.
    private static async Task CheckUseApplicationAsync(MainWindow window)
    {
        var self = Environment.ProcessPath!;
        var byName = new UseApplicationAction(Path.GetFileName(self), null);
        var byPath = new UseApplicationAction(self, null);
        Check("a running app is found by its exe file name", Actions.AppSwitcher.FindWindow(byName) == window.Handle());
        Check("a running app is found by its full path", Actions.AppSwitcher.FindWindow(byPath) == window.Handle(),
            $"{self} vs {Actions.AppSwitcher.ProcessPath((uint)Environment.ProcessId)}");
        Check("a path to another copy doesn't match", Actions.AppSwitcher.FindWindow(new UseApplicationAction(@"C:\Elsewhere\UnnamedManager.exe", null)) == 0);
        Check("the Guide never counts as the app's window", Actions.AppSwitcher.Windows(byName).All(w => w != App.Guide.Handle()));

        window.AppWindow.Hide();
        window.AppWindow.Show();
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Minimize();
        await Task.Delay(400);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var failure = await Actions.ActionRunner.PerformAsync(new ActionPlan.UseApp(byName));
        watch.Stop();
        await Task.Delay(300);
        Check("a minimized app is restored and brought to the front", failure is null
            && window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: not Microsoft.UI.Windowing.OverlappedPresenterState.Minimized }
            && Win32.GetForegroundWindow() == window.Handle(), $"failure {failure}, foreground {Win32.GetForegroundWindow()}");
        Check("switching to a running app takes under 100 ms", watch.ElapsedMilliseconds < 100, $"{watch.ElapsedMilliseconds} ms");

        Check("an exe file name is found through App Paths and PATH", Actions.AppSwitcher.Resolve("notepad.exe") is { } notepad && File.Exists(notepad));
        Check("a full path is used as is", Actions.AppSwitcher.Resolve(self) == self);
        Check("an exe that isn't installed isn't found", Actions.AppSwitcher.Resolve("unnamed-manager-missing.exe") is null);
        Check("an appId in Start is found", Actions.AppSwitcher.IsInstalled("Microsoft.Windows.Explorer"));
        Check("an appId that isn't installed isn't found", !Actions.AppSwitcher.IsInstalled("Contoso.Missing_0000000000000!App"));

        var missing = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "Missing", "action": "m" },
              { "keys": "Ctrl+Alt+J", "title": "Missing store app", "action": "s" } ],
              "actions": { "m": { "type": "application", "exe": "unnamed-manager-missing.exe" },
                           "s": { "type": "application", "appId": "Contoso.Missing_0000000000000!App" } } }
            """).Loaded!;
        foreach (var binding in missing.Leaders)
        {
            var runner = new Actions.ActionRunner();
            await runner.RunAsync(binding, missing);
            Check($"an app that isn't installed is recorded as a failure ({binding.Title})",
                runner.LastFailure is { } f && f.Binding == binding.Path.ToString() && f.Message.StartsWith("Can't find"), runner.LastFailure?.Message ?? "none");
        }
    }

    private static List<string> MenuTexts(Tray.TrayStatus status) =>
        Tray.TrayMenu.Build(status, App.TrayCommandsNow).Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToList();

    /// S-008 and S-009: reload, errors, Enabled, the version, the menu order, Explorer restarting.
    private static async Task CheckTrayAsync()
    {
        var config = App.Config;
        var good = ConfigLoader.StarterJson;
        await File.WriteAllTextAsync(config.Path, good);
        config.Reload();
        var running = config.Current;
        Check("Reload config reads the file again", running is not null && !config.State.HasErrors && App.Tray.State == Tray.TrayState.Ready);
        Check("the tray menu shows when the config loaded", MenuTexts(App.TrayStatusNow).Any(t => t.StartsWith("Config loaded at ")));

        await File.WriteAllTextAsync(config.Path, """
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Commands" },
              { "keys": "Hyper+Space Q", "title": "Q", "action": "open-foo" },
              { "keys": "Hyper+Space W", "title": "W", "action": "b" },
              { "keys": "Hyper+Space E", "title": "E", "action": "c" } ] }
            """);
        config.Reload();
        Check("a reload with errors keeps the last valid config running", ReferenceEquals(config.Current, running) && App.Keys.IsCapturing);
        Check("with an error, the tray icon shows its warning state", App.Tray.State == Tray.TrayState.Warning);
        Check("with an error, the tooltip says config error", App.Tray.Tooltip == "Unnamed Manager, config error", App.Tray.Tooltip);
        var texts = MenuTexts(App.TrayStatusNow);
        Check("the menu shows the first error with its entry path, and +2 more",
            texts.IndexOf("Config error") == 0 && texts[1] == "Binding “Hyper+Space Q” uses action “open-foo”, which isn't defined." && texts[2] == "+2 more",
            string.Join(" | ", texts));

        await File.WriteAllTextAsync(config.Path, good);
        config.Reload();
        Check("fixing the file and reloading clears the warning", App.Tray.State == Tray.TrayState.Ready && !ReferenceEquals(config.Current, running));
        texts = MenuTexts(App.TrayStatusNow);
        string[] order = ["Enabled", "Reload config", "Open config file", "Open file location", App.VersionLine, texts[^2], "Exit"];
        Check("the tray menu's items are in order", texts.SequenceEqual(order), string.Join(" | ", texts));

        var editor = Tray.ConfigFiles.Editor();
        Check("Open config file uses the app set for .json, or Notepad", File.Exists(editor), editor);
        var location = Tray.ConfigFiles.LocationCommand(config.Path);
        Check("Open file location opens its folder with the file selected",
            location.FileName == "explorer.exe" && location.Arguments == $"/select,\"{config.Path}\"");

        App.SetEnabled(false);
        Check("when off, the hook is removed", !App.Keys.IsCapturing);
        Check("when off, the tray icon is dimmed and the tooltip says off", App.Tray.State == Tray.TrayState.Off && App.Tray.Tooltip == "Unnamed Manager, off");
        Check("when off, the menu starts with Not capturing leaders", MenuTexts(App.TrayStatusNow).FirstOrDefault() == "Not capturing leaders");
        Check("Enabled off is remembered across starts", !Prefs.Reread().Enabled);
        App.SetEnabled(true);
        Check("turning Enabled back on installs the hook again", App.Keys.IsCapturing && Prefs.Reread().Enabled);

        Check("a local build shows its branch and commit",
            System.Text.RegularExpressions.Regex.IsMatch(App.VersionLine, @"^Version 0\.1 \(.+, [0-9a-f]{7,}(, modified)?\)$"), App.VersionLine);

        var adds = App.Tray.AddCount;
        Win32.SendMessageW(App.Tray.Handle, Tray.TrayIcon.TaskbarCreatedMessage, 0, 0);
        Check("when Explorer restarts, the tray icon is added again", App.Tray.AddCount == adds + 1);
        await Task.Delay(10);
    }

    /// S-010: a second start finds this copy, asks for the tray menu, and exits without a hook.
    private static async Task CheckSingleCopyAsync()
    {
        using var second = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        var exited = second is not null && await Task.Run(() => second.WaitForExit(15000));
        Check("a second start exits", exited && second!.ExitCode == 0, exited ? $"exit code {second!.ExitCode}" : "still running");
        for (var i = 0; i < 40 && !App.TrayMenu.IsOpen; i++) await Task.Delay(50);
        Check("a second start opens the tray menu of the copy already running", App.TrayMenu.IsOpen);
        await Task.Delay(300);
        var first = App.TrayMenu.Menu?.Items.OfType<Control>().FirstOrDefault(i => i.IsEnabled);
        Check("the opened menu has keyboard focus on its first item", first?.FocusState == FocusState.Keyboard, first?.FocusState.ToString() ?? "none");
        App.TrayMenu.Close();
        await Task.Delay(200);
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        _count++;
        if (!ok) _failures++;
        AgentLog.Line(ok ? $"PASS {name}" : $"FAIL {name}{(detail.Length > 0 ? ": " + detail : "")}");
    }

    public static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var d in Find<T>(child)) yield return d;
        }
    }
}
