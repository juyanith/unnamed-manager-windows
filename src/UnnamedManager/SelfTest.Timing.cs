using UnnamedManager.Core.Config;
using UnnamedManager.Keyboard;
using UnnamedManager.UI;

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

    /// S-036, run before anything else shows the Guide: with `showHud` false, sequences
    /// resolve and errors beep, and the Guide's window is never made.
    private static async Task CheckNoGuideAsync()
    {
        var keys = App.Keys;
        var real = App.Config.Current!;
        var created = App.GuideCreated;
        var quiet = ConfigLoader.Load("""
            { "version": 1, "runtime": { "showHud": false }, "entries": [
              { "keys": "F24", "title": "Test" },
              { "keys": "F24 F22", "title": "Inner" },
              { "keys": "F24 F22 F23", "title": "Deep", "action": "x" },
              { "keys": "Ctrl+Alt+K", "title": "Missing app", "action": "m" } ],
              "actions": { "x": { "type": "noop" }, "m": { "type": "application", "exe": "unnamed-manager-missing.exe" } } }
            """).Loaded!;
        keys.Start(quiet);
        var runs = new List<string>();
        void Record(Core.Keyboard.SequenceEffect e) { if (e is Core.Keyboard.SequenceEffect.Run r) runs.Add(r.Binding.Title); }
        keys.Effect += Record;
        var beeps = UI.Sound.Played;
        Tap(KeyName.F24);
        Tap(KeyName.F21);
        Tap(KeyName.F22);
        Tap(KeyName.F23);
        await Task.Delay(400);
        keys.Effect -= Record;
        Check("with showHud false, a sequence still runs its binding and a wrong key still beeps",
            runs.SequenceEqual(["Deep"]) && UI.Sound.Played == beeps + 1 && keys.Tracker?.IsActive == false,
            $"runs {string.Join(",", runs)}, beeps {UI.Sound.Played - beeps}");
        await App.Runner.RunAsync(quiet.Leaders[1], quiet);
        await Task.Delay(300);
        var log = File.Exists(AppLog.TodayFile) ? await File.ReadAllTextAsync(AppLog.TodayFile) : "";
        Check("with showHud false, a failure goes to the log only, and no Guide window is ever made",
            !created && !App.GuideCreated && App.Runner.LastFailure is { } failure && log.Contains(failure.Message),
            $"made before {created}, after {App.GuideCreated}");
        keys.Start(real);
    }

    /// S-035: the config's `theme` is the one theme setting, and `system` follows Windows' app mode.
    private static async Task CheckThemeAsync(MainWindow window)
    {
        var appsLight = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 1;
        var windows = appsLight ? Microsoft.UI.Xaml.ElementTheme.Light : Microsoft.UI.Xaml.ElementTheme.Dark;
        var seen = new List<string>();
        foreach (var (theme, expected) in new[] { ("light", Microsoft.UI.Xaml.ElementTheme.Light), ("dark", Microsoft.UI.Xaml.ElementTheme.Dark), ("system", windows) })
        {
            var config = TimingConfig("", $"\"theme\": \"{theme}\"");
            App.Guide.Show(config.Leaders[0], [config.Leaders[0].Title], config);
            window.ApplyTheme(theme);
            await Task.Delay(200);
            var guide = App.Guide.View.ActualTheme;
            var main = ((Microsoft.UI.Xaml.FrameworkElement)window.RootElement).ActualTheme;
            seen.Add($"{theme}: Guide {guide}, window {main}");
            Check($"with theme {theme}, the Guide and the window are {expected}" + (theme == "system" ? ", Windows' app mode" : ""),
                guide == expected && main == expected, string.Join("; ", seen));
        }
        App.Guide.Hide();
        Check("with theme system, the Guide and the window follow the app mode rather than pin one",
            App.Guide.View.RequestedTheme == Microsoft.UI.Xaml.ElementTheme.Default
            && ((Microsoft.UI.Xaml.FrameworkElement)window.RootElement).RequestedTheme == Microsoft.UI.Xaml.ElementTheme.Default
            && App.TrayStatusNow.Theme == App.ThemeMode);
        window.ApplyTheme(App.ThemeMode);
        Check("the config's theme is the only theme setting: no app theme preference and no theme box in Settings",
            typeof(Prefs).GetProperty("Theme") is null && !Find<Pages.SettingsPage>(window.RootElement).Any(p => Find<Microsoft.UI.Xaml.Controls.ComboBox>(p).Any()));
    }

    /// S-034: the Guide on the monitor of the window in front, at its center, top or bottom.
    private static async Task CheckPlacementAsync(MainWindow window)
    {
        var hwnd = window.Handle();
        window.AppWindow.Show();
        var system = new Actions.Win32WindowSystem(() => hwnd);
        var screens = system.Screens();
        // With a second monitor, the app's window goes there, so "not the primary one" is tested.
        var target = screens.Count > 1 ? screens[1] : screens[0];
        system.SetFrame(new Core.Windows.WindowRef(hwnd), new(target.Work.X + 40, target.Work.Y + 40, target.Work.Width / 2, target.Work.Height / 2));
        UI.Win32.ForceForeground(hwnd);
        await Task.Delay(300);
        var frames = new List<string>();
        var ok = true;
        foreach (var position in new[] { "center", "top", "bottom" })
        {
            var config = TimingConfig("", $"\"position\": \"{position}\"");
            App.Guide.Show(config.Leaders[0], [config.Leaders[0].Title], config);
            await Task.Delay(300);
            var at = App.Guide.AppWindow.Position;
            var size = App.Guide.AppWindow.Size;
            var work = target.Work;
            var edge = target.Pixels((double)Microsoft.UI.Xaml.Application.Current.Resources["GuideEdgeOffset"]);
            var y = position switch { "top" => work.Y + edge, "bottom" => work.Bottom - size.Height - edge, _ => work.Y + (work.Height - size.Height) / 2 };
            ok &= App.Guide.Screen == target && Math.Abs(at.X - (work.X + (work.Width - size.Width) / 2)) <= 1 && Math.Abs(at.Y - y) <= 1;
            frames.Add($"{position} {at.X},{at.Y} {size.Width}x{size.Height}");
        }
        App.Guide.Hide();
        Check(screens.Count > 1
                ? "the Guide shows on the monitor of the window in front, not the primary one, at its center, top and bottom"
                : "the Guide shows on the monitor of the window in front at its center, top and bottom (one monitor here)",
            ok, $"{string.Join("; ", frames)} on {target.Work} at {target.Scale:0.##}x");
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p) p.Minimize();
    }

    /// 0.5.1: a config that replaces the one in use ends the sequence, so its Guide goes
    /// too: one on screen, one waiting for its delay, and one whose key was still queued.
    private static async Task CheckReloadEndsGuideAsync()
    {
        var original = await File.ReadAllTextAsync(App.Config.Path);
        string Json(string title, int delay) => $$"""
            { "version": 1, "runtime": { "hudDelayMs": {{delay}} }, "entries": [ { "keys": "F24", "title": "{{title}}" }, { "keys": "F24 F23", "title": "Run", "action": "noop" } ] }
            """;
        async Task Use(string json)
        {
            await File.WriteAllTextAsync(App.Config.Path, json);
            App.Config.Reload();
            await Task.Delay(300);
        }

        await Use(Json("Before", 0));
        Tap(KeyName.F24);
        await Task.Delay(300);
        var shown = App.Guide.IsShown;
        await Use("{ not json");
        Check("a reload with errors leaves the sequence and its Guide",
            shown && App.Guide.IsShown && App.Keys.Tracker?.IsActive == true, $"shown {shown}, then {App.Guide.IsShown}");
        await Use(Json("After", 0));
        Check("a reload closes the Guide of the sequence it ends",
            !App.Guide.IsShown && App.Keys.Tracker?.IsActive == false, $"shown {App.Guide.IsShown}, active {App.Keys.Tracker?.IsActive}");
        App.Guide.Hide();
        App.Schedule.Reset();

        await Use(Json("Delayed", 500));
        Tap(KeyName.F24);
        await Task.Delay(100);
        await Use(Json("Delayed again", 500));
        await Task.Delay(500);
        Check("a Guide waiting for its delay doesn't appear after a reload", !App.Guide.IsShown);
        App.Guide.Hide();
        App.Schedule.Reset();

        await Use(Json("Queued", 0));
        // No wait from the key to the reload, so the key's effect is still queued on the UI thread.
        File.WriteAllText(App.Config.Path, Json("Queued again", 0));
        var hook = App.Keys.Hook!;
        hook.Decide(new KeyboardHook.KBDLLHOOKSTRUCT { scanCode = (uint)KeyName.F24.ScanCode() });
        hook.Decide(new KeyboardHook.KBDLLHOOKSTRUCT { scanCode = (uint)KeyName.F24.ScanCode(), flags = 0x80 });
        App.Config.Reload();
        await Task.Delay(300);
        Check("a key still queued when the config reloads leaves no Guide behind", !App.Guide.IsShown);
        App.Guide.Hide();
        App.Schedule.Reset();

        await Use(Json("Failure", 0));
        App.Guide.ShowFailure("Couldn't open it.", App.Config.Current!);
        await Use(Json("Failure again", 0));
        Check("a reload leaves a failure's message up", App.Guide.IsShown && App.Guide.View.FailureMessage is not null);
        App.Guide.Hide();

        await Use(original);
    }
}
