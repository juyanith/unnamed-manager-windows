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
        string[] order = ["Enabled", "Reload config", "Open config file", "Open file location", "Edit bindings", "Open log folder", App.VersionLine, texts[^2], "Exit"];
        Check("the tray menu's items are in order", texts.SequenceEqual(order), string.Join(" | ", texts));

        var editor = Tray.ConfigFiles.Editor();
        Check("Open config file uses the app set for .json, or Notepad", File.Exists(editor), editor);
        var location = Tray.ConfigFiles.LocationCommand(config.Path);
        Check("Open file location opens its folder with the file selected",
            location.FileName == "explorer.exe" && location.Arguments == $"/select,\"{config.Path}\"");

        App.SetEnabled(false);
        Check("when off, the hook is removed", !App.Keys.IsCapturing);
        Check("when off, the tray icon is dimmed and the tooltip says off", App.Tray.State == Tray.TrayState.Off && App.Tray.Tooltip == "Unnamed Manager, off");
        Check("when off, the menu starts with Not capturing leaders", MenuTexts(App.TrayStatusNow).FirstOrDefault() == "Not capturing leaders");
        Check("Enabled off is remembered across starts", !Prefs.Reread().Enabled);
        App.SetEnabled(true);
        Check("turning Enabled back on installs the hook again", App.Keys.IsCapturing && Prefs.Reread().Enabled);

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
}
