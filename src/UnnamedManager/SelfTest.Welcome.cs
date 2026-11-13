using UnnamedManager.Core.Config;
using UnnamedManager.Keyboard;
using UnnamedManager.UI;

namespace UnnamedManager;

/// The self-test's Welcome checks (S-058).
public static partial class SelfTest
{
    /// Welcome shows at start until one sequence runs; each step is checked once done;
    /// Later closes it until the next start.
    private static async Task CheckWelcomeAsync()
    {
        Prefs.Current.OpenedConfig = Prefs.Current.RanSequence = false;
        Prefs.Current.Save();
        Platform.OfficeShortcuts.ClearTestKey();

        var shown = App.ShowWelcomeIfNeeded();
        await Task.Delay(500);
        var welcome = App.Welcome;
        Check("on first start, Welcome opens with three steps to do and Later",
            shown && welcome is not null && Win32.IsWindowVisible(welcome.Handle()) && welcome.Status is { OpenedConfig: false, OfficeDone: false, RanSequence: false } && welcome.CloseText == "Later",
            welcome?.Status.ToString() ?? "no window");
        if (welcome is null) return;

        App.NoteConfigOpened();
        welcome.Refresh();
        var config = welcome.Status.OpenedConfig;
        Platform.OfficeShortcuts.Set(false);
        welcome.Refresh();
        Check("opening the config file and turning off Office shortcuts each check their step",
            config && welcome.Status.OfficeDone && Prefs.Reread().OpenedConfig);

        welcome.Close();
        await Task.Delay(200);
        var closed = App.Welcome is null;
        var again = App.ShowWelcomeIfNeeded();
        Check("Later closes it, and it shows again at the next start until a sequence runs", closed && again);

        // A real sequence through the hook: F24 then F23 runs a binding.
        var test = ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "F24", "title": "Test" },
              { "keys": "F24 F23", "title": "Run", "action": "x" } ],
              "actions": { "x": { "type": "noop" } } }
            """).Loaded!;
        var real = App.Config.Current!;
        App.Keys.Start(test);
        foreach (var key in new[] { KeyName.F24, KeyName.F23 })
        {
            KeyboardHook.InjectForTest(key, up: false);
            KeyboardHook.InjectForTest(key, up: true);
        }
        for (var i = 0; i < 20 && !Prefs.Current.RanSequence; i++) await Task.Delay(50);
        await Task.Delay(100);
        App.Keys.Start(real);
        Check("running one sequence checks the last step and Welcome says Done",
            Prefs.Reread().RanSequence && App.Welcome?.Status.RanSequence == true && App.Welcome.CloseText == "Done", App.Welcome?.Status.ToString() ?? "no window");
        App.Welcome?.Close();
        await Task.Delay(200);
        Check("after one sequence, Welcome no longer shows at start", !App.ShowWelcomeIfNeeded());
        Platform.OfficeShortcuts.ClearTestKey();
    }
}
