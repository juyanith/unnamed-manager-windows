using UnnamedManager.Keyboard;
using UnnamedManager.Pages;
using UnnamedManager.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnnamedManager;
/// The self-test's Actions page checks (S-044, S-045).
public static partial class SelfTest
{
    /// S-044: the Actions page lists actions alphabetically, Choose app and Browse fill
    /// Use Application, renaming updates the bindings, and an action in use stays.
    private static async Task CheckActionsAsync(MainWindow window)
    {
        window.Open("actions");
        await Task.Delay(400);
        if (window.CurrentPage is not ActionsPage page) { Check("the Actions page opens", false); return; }
        var file = await File.ReadAllTextAsync(App.Config.Path);
        var ids = page.Items.Select(i => i.Id).ToList();
        Check("the Actions page lists every action alphabetically, with its type and how many bindings use it",
            ids.SequenceEqual(Core.Config.ConfigEdits.SortedActionIds(App.Editor.Draft)) && page.Items.All(i => i.Detail == $"{i.Action.DisplayName} · 1 binding"),
            string.Join(" | ", page.Items.Select(i => $"{i.Id} {i.Detail}")));

        var apps = AppPicker.Apps;
        var packaged = apps.FirstOrDefault(a => a.AppId?.Contains('!') == true);
        Check("Choose app lists the apps in Start, packaged ones included",
            apps.Count > 5 && packaged is not null && apps.Any(a => a.Exe?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true), $"{apps.Count} apps");
        page.Refresh("open-explorer");
        var choosing = AppPicker.ChooseAsync(page.XamlRoot, page.ActualTheme);
        await Task.Delay(500);
        var shown = AppPicker.Dialog is not null;
        if (packaged is not null) AppPicker.PickForTest(packaged);
        if (await choosing is { } chosen) page.UseApp(chosen);
        await Task.Delay(200);
        Check("choosing a packaged app fills in its AppUserModelID",
            shown && App.Config.Current!.Config.Action("open-explorer") is Core.Config.UseApplicationAction { Exe: null } a && a.AppId == packaged?.AppId,
            packaged?.AppId ?? "");
        page.UseApp(new Actions.StartApp("Notepad", @"C:\Windows\notepad.exe", null));
        await Task.Delay(200);
        Check("Browse fills in the exe's path", App.Config.Current!.Config.Action("open-explorer") is Core.Config.UseApplicationAction { Exe: @"C:\Windows\notepad.exe", AppId: null });
        page.Form.Set("exe", "explorer.exe");
        page.Apply();
        Check("the exe can be typed", App.Config.Current!.Config.Action("open-explorer") is Core.Config.UseApplicationAction { Exe: "explorer.exe" });

        page.Rename("files");
        await Task.Delay(200);
        var renamed = await File.ReadAllTextAsync(App.Config.Path);
        Check("renaming an action updates every binding that uses it",
            renamed.Contains("\"files\": {") && renamed.Contains("\"action\": \"files\"") && !renamed.Contains("open-explorer"));
        page.Delete();
        await Task.Delay(200);
        Check("deleting an action in use is blocked, and the reason lists its bindings",
            page.ProblemShown && await File.ReadAllTextAsync(App.Config.Path) == renamed);
        while (App.Editor.UndoName is not null && await File.ReadAllTextAsync(App.Config.Path) != file) App.Editor.Undo();
        Check("undo restores the file", await File.ReadAllTextAsync(App.Config.Path) == file);
        window.AppWindow.Hide();
    }

    /// S-045: each window type's form has its controls, writes the spec's fields,
    /// clamps its numbers, and undo restores the file.
    private static async Task CheckWindowFormsAsync(MainWindow window)
    {
        window.Open("actions");
        await Task.Delay(400);
        if (window.CurrentPage is not ActionsPage page) { Check("the Actions page opens", false); return; }
        var file = await File.ReadAllTextAsync(App.Config.Path);
        page.New();
        await Task.Delay(200);
        string Saved() => Core.Config.ConfigWriter.Action(App.Config.Current!.Config.Action("new-action")!, []);
        string Labels(Type control) => string.Join(",", page.Form.Controls(control));

        page.SetType("window.focus");
        await Task.Delay(150);
        var focus = (Labels(typeof(ComboBox)), Saved());
        page.SetType("window.place");
        await Task.Delay(150);
        var place = (Labels(typeof(ComboBox)), Labels(typeof(NumberBox)));
        page.Form.Set("preset", "rightThird");
        await Task.Delay(150);
        var preset = Saved();
        (page.Form.Control("frame.width") as NumberBox)!.Value = 150;
        await Task.Delay(150);
        var clamped = (page.Form.Control("frame.width") as NumberBox)!.Value;
        page.SetType("window.move");
        await Task.Delay(150);
        page.Form.Set("_how", "by");
        page.Form.Set("by.dx", "-40");
        page.Apply();
        await Task.Delay(150);
        var move = (Labels(typeof(NumberBox)), Saved());
        page.SetType("window.resize");
        await Task.Delay(150);
        var resizeWays = page.Form.Control("_how") is ComboBox ways ? string.Join(",", ways.Items.OfType<ComboBoxItem>().Select(i => i.Content)) : "";
        page.Form.Set("widths", "50\n75");
        page.Apply();
        await Task.Delay(150);
        var resize = Saved();
        page.SetType("window.maximize");
        await Task.Delay(150);
        var maximize = (Labels(typeof(ToggleSwitch)), Saved());

        Check("Focus Window has a Direction list and writes the direction",
            focus == ("Direction", """{ "type": "window.focus", "direction": "left" }"""), $"{focus}");
        Check("Place Window has a Preset list plus four fraction fields, and a preset writes its name",
            place == ("Preset", "X, percent of the screen,Y, percent of the screen,Width, percent of the screen,Height, percent of the screen")
            && preset == """{ "type": "window.place", "preset": "rightThird" }""", $"{place} {preset}");
        Check("numbers are clamped as you type: a width over 100 % becomes 100 %", clamped == 100, $"{clamped}");
        Check("Move Window goes to a spot or by points, and writes the spec's fields",
            move.Item1 == "Right, in points,Down, in points" && move.Item2 == """{ "type": "window.move", "by": { "dx": -40 } }""", $"{move}");
        Check("Resize Window has its four ways and a Widths list for grow and shrink",
            resizeWays == "Grow through widths,Shrink through widths,To a size,By points" && resize == """{ "type": "window.resize", "step": "grow", "widths": [0.5, 0.75] }""", $"{resizeWays} {resize}");
        Check("Maximize has Press again to restore", maximize == ("Press again to restore", """{ "type": "window.maximize" }"""), $"{maximize}");

        while (App.Editor.UndoName is not null && await File.ReadAllTextAsync(App.Config.Path) != file) App.Editor.Undo();
        Check("Ctrl+Z restores the file", await File.ReadAllTextAsync(App.Config.Path) == file);
        window.AppWindow.Hide();
    }

}
