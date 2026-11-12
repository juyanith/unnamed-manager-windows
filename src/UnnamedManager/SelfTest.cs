using UnnamedManager.Core.Config;
using UnnamedManager.Keyboard;
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

        window.Show("settings");
        await Task.Delay(300);
        Check("Settings opens", Find<SettingsPage>(window.RootElement).Any());
        foreach (var button in Find<AppBarButton>(window.RootElement))
        {
            var shortcut = button.KeyboardAccelerators.Count > 0 || !string.IsNullOrEmpty(button.KeyboardAcceleratorTextOverride);
            Check($"command '{button.Label}' has a label and a shortcut", !string.IsNullOrEmpty(button.Label) && shortcut);
        }

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
