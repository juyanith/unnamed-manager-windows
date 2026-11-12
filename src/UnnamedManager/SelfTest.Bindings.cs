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

    /// S-048: edits are written back atomically, an invalid one is refused, and a
    /// change on disk reloads within a second.
    private static async Task CheckSaveAsync(MainWindow window)
    {
        var store = App.Config;
        await File.WriteAllTextAsync(store.Path, Core.Config.ConfigLoader.StarterJson);
        store.Reload();
        var config = store.Current!.Config;
        var entries = config.Entries.ToList();
        entries[2] = entries[2] with { Title = "Windows Terminal" };
        var reloads = store.WatchedReloads;
        var errors = store.Save(config with { Entries = entries });
        var after = await File.ReadAllTextAsync(store.Path);
        var changed = Core.Config.ConfigLoader.StarterJson.Split('\n').Zip(after.Split('\n')).Count(p => p.First != p.Second);
        Check("a change is written to the file at once, on its one line, and used",
            errors.Count == 0 && changed == 1 && after.Contains("\"Windows Terminal\"") && store.Current.Config.Entries[2].Title == "Windows Terminal",
            $"{changed} lines changed");
        Check("the write goes through a temp file that doesn't stay", !File.Exists(store.Path + ".saving"));
        await Task.Delay(1000);
        Check("the app's own save doesn't reload the file", store.WatchedReloads == reloads, $"{store.WatchedReloads - reloads} reloads");

        var clash = entries.Append(entries[2] with { Title = "Another" }).ToList();
        errors = store.Save(config with { Entries = clash });
        Check("a write that would make an invalid config is refused, and the file isn't touched",
            errors.Count > 0 && await File.ReadAllTextAsync(store.Path) == after, errors.FirstOrDefault()?.Message ?? "");

        // An editor's save: a temp file in the folder, renamed over the config.
        window.Open("bindings");
        await Task.Delay(300);
        var temp = Path.Combine(Path.GetDirectoryName(store.Path)!, "config.json.tmp");
        await File.WriteAllTextAsync(temp, after.Replace("\"Windows Terminal\"", "\"Terminal Preview\""));
        File.Move(temp, store.Path, overwrite: true);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (store.Current.Config.Entries[2].Title != "Terminal Preview" && watch.ElapsedMilliseconds < 2000) await Task.Delay(50);
        var page = window.CurrentPage as BindingsPage;
        Check("a save through a temp file and a rename reloads within a second, and the open window shows it",
            watch.ElapsedMilliseconds < 1000 && page?.All().Any(i => i.Title == "Terminal Preview") == true, $"{watch.ElapsedMilliseconds} ms");
        window.AppWindow.Hide();
    }

    /// S-039: New binding and New menu go at the end of the selected menu with the key
    /// recorder on; with nothing selected, New binding is a direct binding.
    private static async Task CheckNewEntryAsync(MainWindow window)
    {
        window.Open("bindings");
        await Task.Delay(300);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens", false); return; }
        var file = await File.ReadAllTextAsync(App.Config.Path);
        page.Select(page.All().First(i => i.Title == "Commands"));
        page.NewBinding();
        await Task.Delay(200);
        var added = page.Selected;
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot) as Button;
        Check("New binding goes at the end of the selected menu, selected, with its key recorder on",
            added is { Title: "New binding", NeedsAction: true } && added.Node.Path.ToString() == "Hyper+Space A"
            && page.Find(Core.Config.KeyPath.Parse("Hyper+Space")!)!.Children[^1] == added
            && page.Recording?.Equals(added.Node.Path) == true && focused?.Name == "KeyField",
            $"{added?.Node.Path} recording {page.Recording}, focus {focused?.Name}");
        Check("a binding with no action isn't saved, and says why",
            await File.ReadAllTextAsync(App.Config.Path) == file && App.Editor.DraftErrors.Any(e => e.Message == "Choose an action for “Hyper+Space A”."),
            string.Join(" | ", App.Editor.DraftErrors.Select(e => e.Message)));
        App.Editor.Undo();
        page.StopRecording();

        page.Select(null);
        page.NewBinding();
        await Task.Delay(200);
        Check("with nothing selected, New binding adds a direct binding", page.Selected?.Node.Path.ToString() == "Hyper+A" && page.Items[^1] == page.Selected);
        App.Editor.Undo();
        page.Select(page.All().First(i => i.Title == "Commands"));
        page.NewMenu();
        await Task.Delay(200);
        Check("New menu adds a menu at the end of the selected menu", page.Selected is { Title: "New menu" } m && m.Node.Path.ToString() == "Hyper+Space A");
        App.Editor.Undo();
        page.StopRecording();
        await Task.Delay(200);
        Check("undo takes the new entries back out of the file", await File.ReadAllTextAsync(App.Config.Path) == file && App.Editor.DraftErrors.Count == 0);
        window.AppWindow.Hide();
    }
}
