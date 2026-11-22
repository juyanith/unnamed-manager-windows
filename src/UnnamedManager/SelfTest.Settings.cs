using UnnamedManager.Keyboard;
using UnnamedManager.Pages;
using UnnamedManager.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnnamedManager;
/// The self-test's Settings and Help checks (S-047, S-050, S-051).
public static partial class SelfTest
{
    /// S-047: every Settings change is written to the file at once, values are kept in
    /// range, and Ctrl+Z undoes each change.
    private static async Task CheckSettingsAsync(MainWindow window)
    {
        window.Open("settings");
        await Task.Delay(400);
        if (window.CurrentPage is not SettingsPage page) { Check("Settings opens", false); return; }
        var file = await File.ReadAllTextAsync(App.Config.Path);
        T Named<T>(string name) where T : FrameworkElement => Find<T>(page).First(e => e.Name == name);
        var sections = Find<TextBlock>(page).Where(t => t.Style == (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]).Select(t => t.Text);
        Check("Settings has the sections Appearance, Guide, Keys, Feedback, Windows, Focus and swap, Apps, Startup and About, and no accent picker",
            string.Join(",", sections) == "Appearance,Guide,Keys,Feedback,Windows,Focus and swap,Apps,Startup,About", string.Join(",", sections));

        Named<ComboBox>("ThemeBox").SelectedIndex = 1;
        Named<NumberBox>("DelayBox").Value = 350;
        Named<NumberBox>("TimeoutBox").Value = -200;
        Named<NumberBox>("GapBox").Value = 9;
        Named<ComboBox>("PositionBox").SelectedIndex = 1;
        Named<NumberBox>("ColumnsBox").Value = 12;
        Named<NumberBox>("MinVisibleBox").Value = 30;
        page.AddApp(new Actions.StartApp("Calculator", null, "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));
        await Task.Delay(200);
        page.SetAppRule("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Focus", true);
        Named<ToggleSwitch>("BellToggle").IsOn = false;
        page.StartRecording("cancel");
        page.OnRecorded(new Core.Config.Chord(Core.Config.Modifiers.Ctrl, Core.Config.KeyName.Q));
        var modifier = page.KeyErrorText;
        page.OnRecorded(new Core.Config.Chord(Core.Config.Modifiers.None, Core.Config.KeyName.Q));
        Named<ToggleSwitch>("BackToggle").IsOn = false;
        await Task.Delay(300);
        var text = await File.ReadAllTextAsync(App.Config.Path);
        var c = App.Config.Current!.Config;
        Check("every change is written to the config file at once",
            c.UI.Theme == "dark" && c.Runtime.HudDelayMs == 350 && c.UI.Position == "top" && !c.UI.ErrorBell && c.Runtime.CancelKey == "Q" && c.Runtime.BackKey == ""
            && text.Contains("\"cancelKey\": \"Q\"") && App.ThemeMode == "dark", text.Split('\n').FirstOrDefault(l => l.Contains("runtime")) ?? "");
        Check("a negative timeout can't be entered, and the gap keeps to steps of 2",
            c.Runtime.TimeoutMs == 0 && c.Windows.Gap == 10, $"{c.Runtime.TimeoutMs} {c.Windows.Gap}");
        Check("Maximum columns keeps to 1 to 8, and Smallest visible part is saved",
            c.Windows.MaxColumns == 8 && c.Windows.MinVisible == 30 && text.Contains("\"maxColumns\": 8"), $"{c.Windows.MaxColumns} {c.Windows.MinVisible}");
        Check("an app added in Apps starts left out of all three, and a checked box saves its rule",
            text.Contains("\"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App\": { \"focus\": true, \"swap\": false, \"arrange\": false }"),
            text.Split('\n').FirstOrDefault(l => l.Contains("\"windows\"")) ?? "");
        Check("a cancel key with modifiers is refused and says why", modifier == "Use one key with no modifiers.", modifier ?? "");
        while (App.Editor.UndoName is not null && await File.ReadAllTextAsync(App.Config.Path) != file) App.Editor.Undo();
        Check("Ctrl+Z undoes each change", await File.ReadAllTextAsync(App.Config.Path) == file && App.ThemeMode == "system");
        await CheckStartupAsync(page, Named<ToggleSwitch>("StartupToggle"));
        window.AppWindow.Hide();
    }

    /// S-054: the switch writes and removes the Run value (test keys under APP_STORE),
    /// and shows off, saying where, when Task Manager turned it off.
    private static async Task CheckStartupAsync(SettingsPage page, ToggleSwitch toggle)
    {
        Platform.Startup.ClearTestKeys();
        page.Refresh();
        var startsOff = !toggle.IsOn && Platform.Startup.RunValue is null;
        toggle.IsOn = true;
        await Task.Delay(100);
        Check("Run at startup starts off, and on writes the app's path in quotes to the Run key",
            startsOff && Platform.Startup.RunValue == $"\"{Environment.ProcessPath}\"", Platform.Startup.RunValue ?? "none");
        using (var approved = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Platform.Startup.ApprovedKey))
            approved.SetValue(Core.StartupEntry.Name, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, Microsoft.Win32.RegistryValueKind.Binary);
        page.Refresh();
        var link = Find<HyperlinkButton>(page).First(b => b.Name == "StartupAppsLink");
        Check("turned off in Task Manager, the switch shows off and says where",
            !toggle.IsOn && page.StartupLine.StartsWith("Turned off in Startup apps") && link.Visibility == Visibility.Visible, page.StartupLine);
        toggle.IsOn = true;
        await Task.Delay(100);
        Check("turning it on again clears Task Manager's off", Platform.Startup.State == Core.StartupState.On && link.Visibility == Visibility.Collapsed);
        toggle.IsOn = false;
        await Task.Delay(100);
        Check("off removes the Run value", Platform.Startup.RunValue is null && Platform.Startup.State == Core.StartupState.Off);

        // S-109: the WPF app's value is neither on here nor this app's to remove.
        const string wpf = "\"C:\\Programs\\UnnamedManager.Wpf\\UnnamedManager.Wpf.exe\"";
        using (var run = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Platform.Startup.TestRoot + @"\Run"))
            run.SetValue(Core.StartupEntry.Name, wpf);
        page.Refresh();
        await Task.Delay(100);
        Check("with the WPF app in the Run value, the switch is off", !toggle.IsOn && Platform.Startup.State == Core.StartupState.Off);
        Platform.Startup.RepairIfMoved();
        Platform.Startup.Set(false);
        Check("neither the repair nor off touches the WPF app's value", Platform.Startup.RunValue == wpf, Platform.Startup.RunValue ?? "none");
        Platform.Startup.ClearTestKeys();
        page.Refresh();
    }

    /// S-050: Help lists your keys as Home groups them, names the cancel and back keys,
    /// opens from the tray and F1, and a row shows its binding on the Bindings page.
    private static async Task CheckHelpAsync(MainWindow window)
    {
        window.AppWindow.Hide();
        App.TrayCommandsNow.OpenWindow("help");
        await Task.Delay(400);
        Check("Help in the tray menu opens the Help page", window.CurrentPage is HelpPage && Win32.IsWindowVisible(window.Handle()));
        window.Show("bindings");
        await Task.Delay(200);
        var f1 = Find<NavigationViewItem>(window.RootElement).First(i => (string)i.Tag == "help").KeyboardAccelerators.Any(k => k.Key == Windows.System.VirtualKey.F1);
        window.Show("help");
        await Task.Delay(400);
        if (window.CurrentPage is not HelpPage page) { Check("the Help page opens", false); return; }
        var leaders = Core.Config.ConfigEdits.Tree(App.Editor.Draft);
        var home = new Core.Keyboard.Home(leaders, leaders[0].Path.Leader.Modifiers);
        var expected = home.Groups.SelectMany(g => g.Entries.Select(e => e.Path.ToString()));
        Check("Help lists every leader and direct binding, grouped as Home groups them",
            page.Rows.Select(r => r.Path.ToString()).SequenceEqual(expected) && page.Rows.Count == leaders.Count,
            string.Join(",", page.Rows.Select(r => r.Path)));
        Check("F1 opens Help in the Bindings window", f1);
        var sequence = string.Join(" | ", page.SequenceText);
        Check("Help names your cancel and back keys", sequence.Contains("Cancel, Esc") && sequence.Contains("Back, Backspace"), sequence);
        var row = page.Rows.First();
        new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(row.Row).Invoke();
        await Task.Delay(500);
        Check("clicking a row shows it selected on the Bindings page",
            window.CurrentPage is BindingsPage bindings && bindings.Selected?.Node.Path.Equals(row.Path) == true);
        window.AppWindow.Hide();

        // S-051: the starter's Hyper+H runs Show Help, which brings the Help page forward.
        var loaded = App.Config.Current!;
        await App.Runner.RunAsync(loaded.Leaders.First(l => l.Action == "help"), loaded);
        await Task.Delay(400);
        CheckFront("Show Help opens the Help page in front", window.CurrentPage is HelpPage, Win32.GetForegroundWindow() == window.Handle());
        window.AppWindow.Hide();
    }

}
