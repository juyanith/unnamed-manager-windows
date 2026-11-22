using System.Diagnostics;
using System.Windows.Controls;
using UnnamedManager.Actions;
using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using UnnamedManager.Core.Windows;
using UnnamedManager.Keyboard;

namespace UnnamedManager;

/// The WPF app's self-test (S-106): its real hook, config store, Guide, tray and runner on a
/// test config, with fake windows for the window actions.
///   tools/dev.ps1 selftest -App wpf
/// Add a check for every story that changes WPF: a story's `Apps:` line says which do.
public static partial class SelfTest
{
    public static bool Requested => Environment.GetCommandLineArgs().Contains("--selftest");

    private static int _failures, _count;

    public static async Task RunAsync()
    {
        await Task.Delay(500);
        Check("with no file, the app writes the starter config and loads it",
            App.Config.CreatedStarter && App.Config.Current is not null && App.Config.Errors.Count == 0);
        Check("the keyboard hook is installed", App.Keys.IsCapturing);

        (string Area, Func<Task> Run)[] areas =
        [
            ("refusal", CheckRefusalAsync),
            ("sequence", CheckSequenceAsync),
            ("held menu", CheckHeldAsync),
            ("failure", CheckFailureAsync),
            ("reload", CheckReloadAsync),
            ("tray", CheckTrayAsync),
        ];
        foreach (var (area, run) in areas)
        {
            try { await run(); }
            catch (Exception e) { Check($"the {area} checks run", false, e.ToString().ReplaceLineEndings(" ")); }
        }

        App.Shutdown(exitProcess: false);
        Check("Exit removes the hook and the tray icon first", !App.Keys.IsCapturing && !App.Tray.IsAdded);
        AgentLog.Line(_failures == 0 ? $"SELFTEST PASS {_count} checks" : $"SELFTEST FAIL {_failures} of {_count} checks");
        AgentLog.Exit(_failures == 0 ? 0 : 1);
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        _count++;
        if (!ok) _failures++;
        AgentLog.Line(ok ? $"PASS {name}" : $"FAIL {name}{(detail.Length > 0 ? ": " + detail : "")}");
    }

    /// Waits until something is true, for up to 3 seconds: no fixed wait for a slow runner to miss.
    private static async Task<bool> Until(Func<bool> done)
    {
        for (var i = 0; i < 120 && !done(); i++) await Task.Delay(25);
        return done();
    }

    private static void Press(KeyName key, bool? up = null)
    {
        // Through the hook's own decision, so nothing is typed into the app in front.
        var code = key.ScanCode();
        var extended = code > 0xFF ? 1u : 0u;
        if (up != true) App.Keys.Hook!.Decide(new KeyboardHook.KBDLLHOOKSTRUCT { scanCode = (uint)(code & 0xFF), flags = extended });
        if (up != false) App.Keys.Hook!.Decide(new KeyboardHook.KBDLLHOOKSTRUCT { scanCode = (uint)(code & 0xFF), flags = extended | KeyboardHook.LLKHF_UP });
    }

    private static void Alt(bool up) =>
        App.Keys.Hook!.Decide(new KeyboardHook.KBDLLHOOKSTRUCT { vkCode = 0xA4, scanCode = 0x38, flags = up ? KeyboardHook.LLKHF_UP : 0 });

    private static void Tab()
    {
        App.Keys.Hook!.Decide(new KeyboardHook.KBDLLHOOKSTRUCT { vkCode = 0x09, scanCode = 0x0F });
        App.Keys.Hook!.Decide(new KeyboardHook.KBDLLHOOKSTRUCT { vkCode = 0x09, scanCode = 0x0F, flags = KeyboardHook.LLKHF_UP });
    }

    /// Writes the test config and reloads it, as saving the file does.
    private static async Task UseAsync(string json)
    {
        await File.WriteAllTextAsync(App.Config.Path, json);
        App.Config.Reload();
        await Task.Delay(150);
    }

    private static void EndSequence()
    {
        App.Keys.Tracker?.Reset();
        App.Schedule.Reset();
        App.Guide.Hide();
    }

    private static async Task CheckRefusalAsync()
    {
        // A folder that isn't in %TEMP% and doesn't exist. The app refuses before it reads or writes anything.
        var outside = @"C:\unnamed-manager-selftest-refusal\config.json";
        var start = new ProcessStartInfo(Environment.ProcessPath!, "--selftest") { UseShellExecute = false };
        start.Environment["APP_STORE"] = outside;
        start.Environment.Remove("APP_LOG");
        using var child = Process.Start(start)!;
        await Until(() => child.HasExited);
        var refused = child.HasExited && child.ExitCode == 1;
        if (!child.HasExited) child.Kill();
        Check("the self-test refuses a config outside %TEMP%, and writes nothing there", refused && !File.Exists(outside));
    }

    private static async Task CheckSequenceAsync()
    {
        var saved = ActionRunner.Windows;
        var fake = new FakeWindowSystem();
        fake.Add(1, new PixelRect(100, 100, 800, 600));
        ActionRunner.Windows = new WindowControl(fake);
        try
        {
            await UseAsync("""
                { "version": 1, "runtime": { "hudDelayMs": 0 }, "entries": [
                  { "keys": "F24", "title": "Test" },
                  { "keys": "F24 F22", "title": "Inner" },
                  { "keys": "F24 F22 F23", "title": "Fill", "action": "window-fill" } ] }
                """);
            var foreground = UI.Win32.GetForegroundWindow();
            Press(KeyName.F24);
            Check("a leader shows the Guide with its menu",
                await Until(() => App.Guide.IsShown && App.Guide.View.Rows.Any(r => r.Title == "Inner")));
            Check("showing the Guide never takes the foreground", UI.Win32.GetForegroundWindow() == foreground);
            var beeps = UI.Sound.Played;
            Press(KeyName.F21);
            Check("a key that isn't bound beeps and flashes, and the sequence stays",
                await Until(() => UI.Sound.Played > beeps && App.Guide.View.IsFlashing) && App.Keys.Tracker?.IsActive == true);
            Press(KeyName.F22);
            Check("a key that opens a menu shows that menu", await Until(() => App.Guide.View.Rows.Any(r => r.Title == "Fill")));
            Press(KeyName.Backspace);
            Check("Back returns to the menu before", await Until(() => App.Guide.View.Rows.Any(r => r.Title == "Inner")));
            Press(KeyName.F22);
            Press(KeyName.F23);
            var work = fake.Monitors[0].Work;
            Check("the last key runs its action and closes the Guide",
                await Until(() => fake.Windows[1].Frame == work && !App.Guide.IsShown), $"{fake.Windows[1].Frame}");
            Press(KeyName.F24);
            await Until(() => App.Guide.IsShown);
            Press(KeyName.Escape);
            Check("Esc cancels and closes the Guide", await Until(() => !App.Guide.IsShown) && App.Keys.Tracker?.IsActive == false);
        }
        finally
        {
            EndSequence();
            ActionRunner.Windows = saved;
        }
    }

    private static async Task CheckHeldAsync()
    {
        var saved = ActionRunner.Windows;
        var savedKeyDown = App.IsKeyDown;
        var fake = new FakeWindowSystem();
        ActionRunner.Windows = new WindowControl(fake);
        foreach (var (h, title) in new[] { (11, "Notes"), (12, "Mail"), (13, "Code") }) fake.Add(h, new PixelRect(100 * h, 100, 800, 600)).Title = title;
        fake.Click(11);
        fake.Click(12);
        fake.Click(13);
        App.IsKeyDown = _ => true;
        try
        {
            await UseAsync("""
                { "version": 1, "entries": [ { "keys": "Alt+Tab", "title": "Recent windows", "hold": true, "list": { "type": "windows.recent" } } ] }
                """);
            Alt(up: false);
            Tab();
            var listed = await Until(() => App.Guide.IsShown && App.Guide.View.Highlight == 1);
            var rows = string.Join(",", App.Guide.View.Rows.Select(r => r.Title));
            Check("a held list shows the windows, the focused one as Cancel and the one used before highlighted",
                listed && rows == "Code,Mail,Notes" && App.Guide.View.Rows[1].IsHighlighted, rows);
            Tab();
            Check("the repeat key moves the highlight", await Until(() => App.Guide.View.Highlight == 2));
            Alt(up: true);
            Check("letting go focuses the highlighted window and closes the Guide",
                await Until(() => fake.Front == 11 && !App.Guide.IsShown), $"{fake.Front}");

            Alt(up: false);
            Tab();
            await Until(() => App.Guide.IsShown);
            App.IsKeyDown = _ => false;
            Check("a key-up the hook missed still chooses", await Until(() => fake.Front == 13 && !App.Guide.IsShown), $"{fake.Front}");

            App.IsKeyDown = _ => true;
            Alt(up: false);
            Tab();
            await Until(() => App.Guide.IsShown && App.Guide.View.Rows.Count == 3);
            fake.Remove(11);
            Check("a window that closes while the list is up leaves it", await Until(() => App.Guide.View.Rows.Count == 2));
            Alt(up: true);
            await Until(() => !App.Guide.IsShown);
        }
        finally
        {
            App.IsKeyDown = savedKeyDown;
            EndSequence();
            ActionRunner.Windows = saved;
        }
    }

    private static async Task CheckFailureAsync()
    {
        await UseAsync("""
            { "version": 1, "runtime": { "hudDelayMs": 0 }, "entries": [ { "keys": "Ctrl+Alt+K", "title": "Missing app", "action": "m" } ],
              "actions": { "m": { "type": "application", "exe": "unnamed-manager-missing.exe" } } }
            """);
        var loaded = App.Config.Current!;
        await App.Runner.RunAsync(loaded.Leaders[0], loaded);
        var message = App.Runner.LastFailure?.Message;
        Check("a failed action says why in the Guide",
            message is not null && await Until(() => App.Guide.IsShown && App.Guide.View.FailureMessage == message), message ?? "no failure");
        var log = File.Exists(AppLog.TodayFile) ? await File.ReadAllTextAsync(AppLog.TodayFile) : "";
        Check("and writes it to the log", message is not null && log.Contains(message));
        Check("the failure's message closes by itself", await Until(() => !App.Guide.IsShown));
    }

    private static string ReloadConfig(string title, int delay = 0) => $$"""
        { "version": 1, "runtime": { "hudDelayMs": {{delay}} }, "entries": [ { "keys": "F24", "title": "{{title}}" }, { "keys": "F24 F23", "title": "Run", "action": "noop" } ] }
        """;

    private static async Task CheckReloadAsync()
    {
        await UseAsync(ReloadConfig("Before"));
        Press(KeyName.F24);
        await Until(() => App.Guide.IsShown);
        await UseAsync("{ not json");
        Check("a reload with errors keeps the last config, its sequence and the warning icon",
            App.Keys.Tracker?.IsActive == true && App.Guide.IsShown && App.Tray.State == Tray.TrayState.Warning
            && App.Config.Current?.Leaders[0].Title == "Before");
        await UseAsync(ReloadConfig("After"));
        Check("a good reload replaces the config and ends the sequence",
            App.Config.Current?.Leaders[0].Title == "After" && App.Keys.Tracker?.IsActive == false && App.Tray.State == Tray.TrayState.Ready);
        EndSequence();
    }

    private static List<string> MenuTexts(Tray.TrayStatus status) =>
        Tray.TrayMenu.Build(status, App.TrayCommandsNow).Items.OfType<MenuItem>()
            .Select(i => i.Header as string is { } header ? header.Replace("_", "") : ((TextBlock)i.Header).Text).ToList();

    private static async Task CheckTrayAsync()
    {
        await UseAsync(ConfigLoader.StarterJson);
        var texts = MenuTexts(App.TrayStatusNow with { OfficeShortcutsOn = true });
        string[] order = ["Enabled", "Run at startup", "Turn off Office shortcuts", "Reload config", "Open config file", "Open file location", "Help", "Open log folder", App.VersionLine, texts[^2], "Exit"];
        Check("the tray menu's items are in order", texts.SequenceEqual(order), string.Join(" | ", texts));
        Check("the version line says it's the WPF app", App.VersionLine.EndsWith("(WPF)"), App.VersionLine);

        App.SetEnabled(false);
        Check("Enabled off removes the hook, dims the icon and is remembered",
            !App.Keys.IsCapturing && App.Tray.State == Tray.TrayState.Off && !Prefs.Reread().Enabled
            && MenuTexts(App.TrayStatusNow).FirstOrDefault() == "Not capturing leaders");
        App.SetEnabled(true);
        Check("Enabled on installs it again", App.Keys.IsCapturing && Prefs.Reread().Enabled);

        App.TrayMenu.Open(0, 0, App.TrayStatusNow, App.TrayCommandsNow, keyboard: false);
        Check("the tray menu opens", await Until(() => App.TrayMenu.IsOpen));
        App.TrayMenu.Close();
    }
}
