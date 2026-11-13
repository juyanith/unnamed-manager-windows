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
/// (tools/selftest.ps1 does this, starting with no file.) Add a check for every story with logic, in the file for its area (SelfTest.Keys.cs, .Guide.cs, .Icons.cs, .Timing.cs, .Actions.cs, .Windows.cs, .Tray.cs, and for the Bindings window .Bindings.cs, .ActionsPage.cs, .Contexts.cs, .Settings.cs).
public static partial class SelfTest
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
        var leader = config.Current?.Leaders.FirstOrDefault();
        Check("the starter has the leader Commands on Hyper+Space with F and T",
            leader is { Title: "Commands" } && leader.Path.ToString() == "Hyper+Space"
            && string.Join(",", leader.Children.Select(c => $"{c.Path.LastStep} {c.Title}")) == "F File Explorer,T Terminal");
        Check("the starter has Show Help on Hyper+H, and Show Help is in the Type list",
            config.Current?.Leaders.ElementAtOrDefault(1) is { Title: "Help", Action: "show-help" } help && help.Path.ToString() == "Hyper+H"
            && config.Current.Config.Action("show-help") is BasicAction { Kind: "help" } && ActionForms.Types().Any(k => k.Type == "help"));
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

        await CheckNoGuideAsync();
        await CheckKeysAsync();
        await CheckGuideAsync(window);
        await CheckGuideIconsAsync();
        await CheckTimingAsync();
        await CheckPlacementAsync(window);
        await CheckThemeAsync(window);
        await CheckUseApplicationAsync(window);
        await CheckContextAwareAsync(window);
        await CheckOpenAsync();
        await CheckFailureAsync();
        await CheckRunCommandAsync();
        await CheckWindowsAsync(window);
        await CheckTrayAsync();
        await CheckSingleCopyAsync();
        await CheckBindingsTreeAsync(window);
        await CheckSaveAsync(window);
        await CheckNewEntryAsync(window);
        await CheckRecordKeyAsync(window);
        await CheckDeleteAsync(window);
        await CheckMoveAsync(window);
        await CheckActionsAsync(window);
        await CheckEditEntryAsync(window);
        await CheckErrorLinksAsync(window);
        await CheckWindowFormsAsync(window);
        await CheckContextsAsync(window);
        await CheckSettingsAsync(window);
        await CheckHelpAsync(window);

        await CheckCommandsAsync(window);

        // Last: Exit removes the hook and the tray icon before the app goes.
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
