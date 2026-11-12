using UnnamedManager.Core.Config;
using UnnamedManager.Keyboard;
using UnnamedManager.UI;
using UnnamedManager.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace UnnamedManager;

/// The self-test's Keys checks; SelfTest.cs runs them in order.
public static partial class SelfTest
{
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
        }

        // S-013: what Send Shortcut injects is marked, so the hook lets it straight through.
        seen.Clear();
        keys.Effect += Record;
        Check("sent keys pass through the hook untouched", KeyboardHook.Inject([new(0, KeyName.F24.ScanCode(), false, false), new(0, KeyName.F24.ScanCode(), false, true)]));
        await Task.Delay(300);
        Check("a sent leader chord starts no sequence", seen.Count == 0 && !tracker.IsActive, $"{seen.Count} effects");
        keys.Effect -= Record;
        tracker.Process(new Core.Keyboard.RawKey(Core.Keyboard.ShortcutEvents.LCtrl, 0x1D, false, false));
        var held = Actions.ActionRunner.HeldModifierKeys();
        tracker.Process(new Core.Keyboard.RawKey(Core.Keyboard.ShortcutEvents.LCtrl, 0x1D, false, true));
        Check("Send Shortcut learns the leader modifiers still held from the hook", held.Contains(Core.Keyboard.ShortcutEvents.LCtrl));
        keys.Start(real);

        keys.Stop();
        Check("turning capture off removes the hook", !keys.IsCapturing);
        keys.Start(real);
        Check("turning it back on installs it again", keys.IsCapturing);
    }
}
