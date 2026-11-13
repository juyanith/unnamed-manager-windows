using UnnamedManager.Keyboard;
using UnnamedManager.Pages;
using UnnamedManager.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnnamedManager;
/// The self-test's Contexts page checks (S-046).
public static partial class SelfTest
{
    /// S-046: contexts in order with their apps, added with Choose app and Browse, and reordered.
    private static async Task CheckContextsAsync(MainWindow window)
    {
        window.Open("contexts");
        await Task.Delay(400);
        if (window.CurrentPage is not ContextsPage page) { Check("the Contexts page opens", false); return; }
        var file = await File.ReadAllTextAsync(App.Config.Path);
        page.New();
        page.Rename("browser");
        var edge = AppPicker.Apps.FirstOrDefault(a => a.Exe?.EndsWith("msedge.exe", StringComparison.OrdinalIgnoreCase) == true)
            ?? AppPicker.Apps.First(a => a.Exe is not null);
        var choosing = AppPicker.ChooseAsync(page.XamlRoot, page.ActualTheme);
        await Task.Delay(500);
        AppPicker.PickForTest(edge);
        if (await choosing is { } chosen) page.AddApp(chosen);
        page.AddApp(new Actions.StartApp("Notepad", @"C:\Windows\notepad.exe", null));
        await Task.Delay(200);
        var context = App.Config.Current!.Config.Contexts.SingleOrDefault(c => c.Id == "browser");
        var item = page.Selected;
        Check("apps are added with Choose app and Browse, saved as exe names, and shown with their names",
            context is not null && context.Apps.Count == 2 && context.Apps.All(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            && item?.Apps.Count == 2 && item.Apps[0].Name == edge.Name,
            $"{string.Join(",", context?.Apps ?? [])} {item?.Names}");
        Check("an app found in Start shows its icon", item?.Apps[0].Icon is not null);
        page.New();
        page.Rename("editors");
        page.Move(-1);
        await Task.Delay(200);
        Check("contexts reorder with Alt+Up and Alt+Down, and the order is saved",
            string.Join(",", App.Config.Current!.Config.Contexts.Select(c => c.Id)) == "editors,browser" && page.Items[0].Id == "editors");
        while (App.Editor.UndoName is not null && await File.ReadAllTextAsync(App.Config.Path) != file) App.Editor.Undo();
        Check("undo restores the file", await File.ReadAllTextAsync(App.Config.Path) == file);
        window.AppWindow.Hide();
    }

}
