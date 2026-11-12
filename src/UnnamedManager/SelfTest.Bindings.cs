using UnnamedManager.Pages;
using UnnamedManager.UI;
using Microsoft.UI.Xaml.Controls;

namespace UnnamedManager;

/// The self-test's Bindings window checks (M5).
public static partial class SelfTest
{
    private const uint WM_CLOSE = 0x0010;

    /// S-038: the tree, the tray's left click, and closing to the tray.
    private static async Task CheckBindingsTreeAsync(MainWindow window)
    {
        var hwnd = window.Handle();
        window.AppWindow.Hide();
        App.Tray.NotifyForTest(select: true);
        await Task.Delay(500);
        var page = window.CurrentPage as BindingsPage;
        Check("a left click on the tray icon opens the Bindings window on the Bindings page",
            Win32.IsWindowVisible(hwnd) && page is not null && !App.TrayMenu.IsOpen);

        if (page is not null)
        {
            var rows = page.All().Select(i => $"{i.Key} {i.Title} {i.Action}").ToList();
            var entries = App.Config.Current!.Config.Entries.Select(e => $"{(Core.Config.KeyPath.Parse(e.Keys)!.LastStep ?? Core.Config.KeyPath.Parse(e.Keys)!.Leader)} {e.Title} {e.Action ?? "Menu"}").ToList();
            Check("the tree shows every entry with its key, title and action, in file order", rows.SequenceEqual(entries), string.Join(" | ", rows));
            var spoken = page.All().First(i => i.Title == "Terminal").Spoken;
            Check("Narrator reads a row as key, title, action", spoken == "T, Terminal, open-terminal", spoken);
        }

        App.Tray.NotifyForTest(select: false);
        await Task.Delay(500);
        Check("a right click opens the tray menu", App.TrayMenu.IsOpen);
        App.TrayMenu.Close();
        await Task.Delay(200);

        Win32.SendMessageW(hwnd, WM_CLOSE, 0, 0);
        await Task.Delay(300);
        Check("closing the Bindings window hides it and the app keeps running",
            !Win32.IsWindowVisible(hwnd) && App.Keys.IsCapturing);

        // An invalid file: the errors show above the tree, which keeps the last valid config.
        var text = await File.ReadAllTextAsync(App.Config.Path);
        await File.WriteAllTextAsync(App.Config.Path, Core.Config.ConfigLoader.StarterJson.Replace("open-terminal\" }\n", "quick-note\" }\n"));
        App.Config.Reload();
        window.Open("bindings");
        await Task.Delay(500);
        page = window.CurrentPage as BindingsPage;
        var infoBar = page is null ? null : Find<InfoBar>(page).FirstOrDefault();
        Check("an invalid config shows its errors above the tree, and the tree keeps the last valid config",
            infoBar is { IsOpen: true } && page!.All().Any(i => i.Title == "Terminal" && i.Action == "open-terminal"),
            string.Join(" | ", App.Config.Errors.Select(e => e.Message)));
        await File.WriteAllTextAsync(App.Config.Path, text);
    }
}
