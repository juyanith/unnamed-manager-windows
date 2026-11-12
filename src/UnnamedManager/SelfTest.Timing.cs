using UnnamedManager.Core.Config;
using UnnamedManager.Keyboard;

namespace UnnamedManager;

/// The self-test's Guide timing and feedback checks (M4), through the real hook and
/// real timers. The rules themselves are unit-tested in `GuideScheduleTests`.
public static partial class SelfTest
{
    private static LoadedConfig TimingConfig(string runtime, string ui = "") => ConfigLoader.Load($$"""
        { "version": 1, "runtime": { {{runtime}} }, "ui": { {{ui}} }, "entries": [
          { "keys": "F24", "title": "Test" },
          { "keys": "F24 F22", "title": "Inner" },
          { "keys": "F24 F22 F23", "title": "Deep", "action": "x" } ],
          "actions": { "x": { "type": "noop" } } }
        """).Loaded!;

    private static void Tap(KeyName key)
    {
        KeyboardHook.InjectForTest(key, up: false);
        KeyboardHook.InjectForTest(key, up: true);
    }

    private static async Task CheckTimingAsync()
    {
        var keys = App.Keys;
        var real = App.Config.Current!;

        // S-031 and S-032 with real timers: a delay of 300 ms, a timeout of 600 ms.
        keys.Start(TimingConfig("\"hudDelayMs\": 300, \"timeoutMs\": 600"));
        Tap(KeyName.F24);
        await Task.Delay(150);
        var early = App.Guide.IsShown;
        await Task.Delay(350);
        var shown = App.Guide.IsShown;
        Check("with hudDelayMs 300, the Guide waits for the delay, then shows", !early && shown, $"at 150 ms {early}, at 500 ms {shown}");
        await Task.Delay(800);
        Check("with timeoutMs 600, the forgotten Guide closes and the sequence ends",
            !App.Guide.IsShown && keys.Tracker?.IsActive == false, $"shown {App.Guide.IsShown}, active {keys.Tracker?.IsActive}");

        // S-033: a key that isn't bound (F21) beeps and flashes, and the sequence stays.
        keys.Start(TimingConfig("\"hudDelayMs\": 0"));
        Tap(KeyName.F24);
        await Task.Delay(200);
        var before = UI.Sound.Played;
        Tap(KeyName.F21);
        await Task.Delay(100);
        Check("with errorBell, a key that isn't bound plays the Default Beep, flashes the Guide, and the sequence stays",
            UI.Sound.Played == before + 1 && App.Guide.View.IsFlashing && App.Guide.IsShown && keys.Tracker?.IsActive == true,
            $"beeps {UI.Sound.Played - before}, flashing {App.Guide.View.IsFlashing}, active {keys.Tracker?.IsActive}");
        await Task.Delay(400);
        Check("the flash ends after 300 ms", !App.Guide.View.IsFlashing);
        Tap(KeyName.Escape);

        keys.Start(TimingConfig("\"hudDelayMs\": 0", "\"errorBell\": false, \"errorFlash\": false"));
        Tap(KeyName.F24);
        await Task.Delay(200);
        before = UI.Sound.Played;
        Tap(KeyName.F21);
        await Task.Delay(100);
        Check("without errorBell nothing plays, and without errorFlash nothing flashes",
            UI.Sound.Played == before && !App.Guide.View.IsFlashing && keys.Tracker?.IsActive == true);
        Tap(KeyName.Escape);
        await Task.Delay(100);

        keys.Start(real);
    }
}
