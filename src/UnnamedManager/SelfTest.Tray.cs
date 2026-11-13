using UnnamedManager.Core.Config;
using UnnamedManager.Keyboard;
using UnnamedManager.UI;
using UnnamedManager.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace UnnamedManager;

/// The self-test's Tray checks; SelfTest.cs runs them in order.
public static partial class SelfTest
{
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
        string[] order = ["Enabled", "Turn off Office shortcuts", "Reload config", "Open config file", "Open file location", "Edit bindings", "Settings", "Help", "Open log folder", App.VersionLine, texts[^2], "Exit"];
        Check("the tray menu's items are in order", texts.SequenceEqual(order), string.Join(" | ", texts));

        var editor = Tray.ConfigFiles.Editor();
        Check("Open config file uses the app set for .json, or Notepad", File.Exists(editor), editor);
        var location = Tray.ConfigFiles.LocationCommand(config.Path);
        Check("Open file location opens its folder with the file selected",
            location.FileName == "explorer.exe" && location.Arguments == $"/select,\"{config.Path}\"");

        App.SetEnabled(false);
        Check("when off, the hook is removed", !App.Keys.IsCapturing);
        Check("when off, the tray icon is dimmed and the tooltip says off", App.Tray.State == Tray.TrayState.Off && App.Tray.Tooltip == "Unnamed Manager, off");
        // S-059: each state has an icon for a light and a dark taskbar at every tray size.
        var icons = Enum.GetValues<Tray.TrayState>().SelectMany(s => new[] { Tray.TrayIcon.IconPath(s, true), Tray.TrayIcon.IconPath(s, false) }).ToList();
        var loads = icons.All(path => new[] { 16, 20, 24, 32 }.All(size =>
        {
            var icon = Win32.LoadImageW(0, path, 1, size, size, 0x10);
            if (icon != 0) Win32.DestroyIcon(icon);
            return icon != 0;
        }));
        Check("every tray state has an icon for the light and the dark taskbar, at 16, 20, 24 and 32 px", icons.Count == 6 && loads);
        var redraws = App.Tray.ThemeRedraws;
        unsafe
        {
            fixed (char* area = "ImmersiveColorSet") Win32.SendMessageW(App.Tray.Handle, 0x001A, 0, (nint)area);
        }
        Check("when the taskbar's theme changes, the tray icon is drawn again", App.Tray.ThemeRedraws == redraws + 1);
        Check("when off, the menu starts with Not capturing leaders", MenuTexts(App.TrayStatusNow).FirstOrDefault() == "Not capturing leaders");
        Check("Enabled off is remembered across starts", !Prefs.Reread().Enabled);
        App.SetEnabled(true);
        Check("turning Enabled back on installs the hook again", App.Keys.IsCapturing && Prefs.Reread().Enabled);

        // S-056: the elevation check, and the menu's lines while such an app is in front.
        var taskbar = Win32.FindWindowW("Shell_TrayWnd", null);
        Check("this app's windows and the taskbar don't count as running as administrator",
            !Platform.ProcessElevation.Self && !Platform.ProcessElevation.Above(App.MainWindow.Handle()) && (taskbar == 0 || !Platform.ProcessElevation.Above(taskbar)));
        var elevated = MenuTexts(App.TrayStatusNow with { ElevatedApp = "Task Manager" });
        Check("with an app running as administrator in front, the menu says so first, naming it",
            elevated.Take(2).SequenceEqual(["An app running as administrator is in front", "Windows keeps Unnamed Manager from moving Task Manager or typing into it."]), string.Join(" | ", elevated.Take(2)));

        await CheckOfficeShortcutsAsync();

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

    /// S-057: Turn off Office shortcuts gives `ms-officeapp` a handler that does nothing,
    /// for this user (a test key under APP_STORE); Turn on removes it; a handler of the
    /// user's own is left alone, and the menu then offers neither.
    private static Task CheckOfficeShortcutsAsync()
    {
        Platform.OfficeShortcuts.ClearTestKey();
        var on = MenuTexts(App.TrayStatusNow);
        App.TrayCommandsNow.SetOfficeShortcuts!(false);
        using (var command = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Platform.Startup.TestRoot + @"\Classes\ms-officeapp\Shell\Open\Command"))
            Check("Turn off Office shortcuts sets the ms-officeapp handler, for this user, to do nothing",
                on.Contains("Turn off Office shortcuts") && Platform.OfficeShortcuts.On == false && command?.GetValue("") as string == "rundll32");
        var off = MenuTexts(App.TrayStatusNow);
        App.TrayCommandsNow.SetOfficeShortcuts!(true);
        Check("Turn on Office shortcuts puts Windows' handler back",
            off.Contains("Turn on Office shortcuts") && Platform.OfficeShortcuts.On == true
            && Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Platform.Startup.TestRoot + @"\Classes\ms-officeapp") is null);
        Platform.OfficeShortcuts.SetCustomForTest(@"C:\Tools\my-office.exe ""%1""");
        App.TrayCommandsNow.SetOfficeShortcuts!(true);
        var custom = MenuTexts(App.TrayStatusNow);
        Check("a handler of your own is left alone, and the menu offers neither",
            Platform.OfficeShortcuts.On is null && !custom.Any(t => t.Contains("Office shortcuts")));
        Platform.OfficeShortcuts.ClearTestKey();
        return Task.CompletedTask;
    }}
