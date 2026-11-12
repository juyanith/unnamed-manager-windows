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
            typeof(Prefs).GetProperty("Theme") is null && !Find<Microsoft.UI.Xaml.Controls.ComboBox>(window.RootElement).Any());
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
}
