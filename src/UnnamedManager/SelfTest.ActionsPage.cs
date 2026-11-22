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
        page.Form.Set("_target", "number");
        await Task.Delay(150);
        var numberShown = Labels(typeof(NumberBox));
        page.Form.Set("_number", "3");
        page.Form.Set("scope", "display");
        page.Apply();
        await Task.Delay(150);
        var focusNumber = Saved();
        page.SetType("window.swap");
        await Task.Delay(150);
        page.Form.Set("_target", "recent");
        page.Apply();
        await Task.Delay(150);
        var swap = (Labels(typeof(ComboBox)), Saved());
        page.SetType("window.arrange");
        await Task.Delay(150);
        page.Form.Set("count", "all");
        page.Form.Set("layout", "rows");
        page.Apply();
        await Task.Delay(150);
        var arrange = (Labels(typeof(ComboBox)), Saved());
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
        page.SetType("window.fill");
        await Task.Delay(150);
        var fill = (Labels(typeof(ToggleSwitch)), Saved());
        var types = page.TypeNames();

        Check("Focus Window has Focus on and Windows lists and writes its target",
            focus == ("Focus on,Windows", """{ "type": "window.focus", "target": "right" }"""), $"{focus}");
        Check("Window number shows a Number box, and saves a number and the scope",
            numberShown == "Number" && focusNumber == """{ "type": "window.focus", "target": 3, "scope": "display" }""", $"{numberShown} {focusNumber}");
        Check("Swap Window has Swap with and Windows, with Window used before",
            swap == ("Swap with,Windows", """{ "type": "window.swap", "target": "recent" }"""), $"{swap}");
        Check("Arrange Windows has Windows, Groups and Layout",
            arrange == ("Windows,Groups,Layout", """{ "type": "window.arrange", "count": "all", "layout": "rows" }"""), $"{arrange}");
        Check("Place Window has a Preset list plus four fraction fields, and a preset writes its name",
            place == ("Preset", "X, percent of the screen,Y, percent of the screen,Width, percent of the screen,Height, percent of the screen")
            && preset == """{ "type": "window.place", "preset": "rightThird" }""", $"{place} {preset}");
        Check("numbers are clamped as you type: a width over 100 % becomes 100 %", clamped == 100, $"{clamped}");
        Check("Move Window goes to a spot or by points, and writes the spec's fields",
            move.Item1 == "Right, in points,Down, in points" && move.Item2 == """{ "type": "window.move", "by": { "dx": -40 } }""", $"{move}");
        Check("Resize Window has its four ways and a Widths list for grow and shrink",
            resizeWays == "Grow through widths,Shrink through widths,To a size,By points" && resize == """{ "type": "window.resize", "step": "grow", "widths": [0.5, 0.75] }""", $"{resizeWays} {resize}");
        Check("Fill has Press again to restore", fill == ("Press again to restore", """{ "type": "window.fill" }"""), $"{fill}");
        Check("the Type list offers Fill once, not under its old name, and Swap Window and Arrange Windows",
            types.Count(t => t == "Fill") == 1 && types.Contains("Swap Window") && types.Contains("Arrange Windows"), string.Join(",", types));

        while (App.Editor.UndoName is not null && await File.ReadAllTextAsync(App.Config.Path) != file) App.Editor.Undo();
        Check("Ctrl+Z restores the file", await File.ReadAllTextAsync(App.Config.Path) == file);
        window.AppWindow.Hide();
    }


    /// Puts a check's own config in the test file and loads it.
    private static async Task LoadTestConfigAsync(string json)
    {
        await File.WriteAllTextAsync(App.Config.Path, json);
        App.Config.Reload();
        await Task.Delay(300);
    }

    /// 0.5.1: a form shows every line, and leaving fields you didn't edit, or editing
    /// another one, never changes the rest of the action.
    private static async Task CheckFormsKeepValuesAsync(MainWindow window)
    {
        var original = await File.ReadAllTextAsync(App.Config.Path);
        await LoadTestConfigAsync("""
            { "version": 1, "entries": [ { "keys": "Hyper+Space", "title": "Commands" }, { "keys": "Hyper+Space R", "title": "Run", "action": "run" } ],
              "actions": {
                "run": { "type": "exec", "command": ["code.exe", "--new-window", "C:/notes"], "env": { "A": "1", "B": "x=y" } },
                "blank": { "type": "exec", "command": ["tool.exe", "", "last"] },
                "breaks": { "type": "exec", "command": ["pwsh.exe", "-Command", "one\ntwo"], "cwd": "C:/first\nsecond" },
                "widths": { "type": "window.resize", "step": "grow", "widths": [0.333333333333, 0.5] },
                "third": { "type": "window.place", "frame": { "x": 0.333333333333, "y": 0, "width": 0.333333333333, "height": 1 } },
                "far": { "type": "window.focus", "target": 150 } } }
            """);
        window.Open("actions");
        await Task.Delay(400);
        if (window.CurrentPage is not ActionsPage page) { Check("the Actions page opens for its forms' values", false); return; }
        string Text(string field) => ((TextBox)page.Form.Control(field)!).Text.ReplaceLineEndings("|");
        string Saved(string id) => Core.Config.ConfigWriter.Action(App.Config.Current!.Config.Action(id)!, []);

        page.Refresh("run");
        await Task.Delay(200);
        var shown = (Text("command"), Text("env"));
        Check("Run Command's form shows every argument and every variable", shown == ("code.exe|--new-window|C:/notes", "A=1|B=x=y"), $"{shown}");

        foreach (var id in new[] { "run", "blank", "breaks", "widths", "third", "far" })
        {
            var before = Saved(id);
            page.Refresh(id);
            await Task.Delay(200);
            foreach (var field in Core.Config.ActionForms.Fields(App.Config.Current!.Config.Action(id)!.Type)!)
            {
                page.Form.Control(field.Name)?.Focus(FocusState.Programmatic);
                await Task.Delay(80);
            }
            page.Apply();
            await Task.Delay(200);
            Check($"tabbing through the form of “{id}” changes nothing", Saved(id) == before, Saved(id));
        }

        page.Refresh("run");
        await Task.Delay(200);
        page.Form.Set("cwd", "~/src");
        page.Apply();
        await Task.Delay(200);
        var run = Saved("run");
        Check("editing Start in keeps every argument and variable",
            run == """{ "type": "exec", "command": ["code.exe", "--new-window", "C:/notes"], "cwd": "~/src", "env": { "A": "1", "B": "x=y" } }""", run);

        page.Refresh("blank");
        await Task.Delay(200);
        var readOnly = ((TextBox)page.Form.Control("command")!).IsReadOnly;
        Check("arguments that can't be one line each make the field read-only", readOnly);

        page.Refresh("far");
        await Task.Delay(200);
        page.Form.Set("scope", "display");
        await Task.Delay(200);
        var far = Saved("far");
        Check("editing another field keeps a number past the box's usual range", far.Contains("\"target\": 150") && far.Contains("display"), far);

        await LoadTestConfigAsync(original);
        window.AppWindow.Hide();
    }

    /// 0.5.1: until Context Aware has a form, its action says where to edit it, and the
    /// page can't replace it.
    private static async Task CheckContextAwareFormAsync(MainWindow window)
    {
        var original = await File.ReadAllTextAsync(App.Config.Path);
        await LoadTestConfigAsync("""
            { "version": 1, "entries": [ { "keys": "Hyper+Space", "title": "Commands" }, { "keys": "Hyper+Space A", "title": "Address", "action": "address" } ],
              "actions": {
                "address": { "type": "contextual", "variants": { "web": { "type": "send", "keys": "Ctrl+L" } }, "fallback": { "type": "send", "keys": "Ctrl+F" } },
                "other": { "type": "noop" } },
              "contexts": [ { "id": "web", "apps": ["msedge.exe"] } ] }
            """);
        window.Open("actions");
        await Task.Delay(400);
        if (window.CurrentPage is not ActionsPage page) { Check("the Actions page opens for Context Aware", false); return; }
        string Words() => string.Join(" | ", Find<TextBlock>(page).Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text));
        page.Refresh("address");
        await Task.Delay(200);
        var type = Find<ComboBox>(page).First(b => b.Header as string == "Type");
        Check("a Context Aware action says its type and that it's edited in the config file",
            !type.IsEnabled && type.PlaceholderText == "Context Aware" && Words().Contains("edited in the config file"),
            $"enabled {type.IsEnabled}, placeholder “{type.PlaceholderText}”, {Words()}");
        page.SetType("noop");
        await Task.Delay(300);
        Check("picking a type can't throw away a Context Aware action's variants",
            App.Config.Current!.Config.Action("address") is Core.Config.ContextualAction { Variants.Count: 1, Fallback: not null });
        page.Refresh("other");
        await Task.Delay(200);
        Check("the next action's Type list works again, with no note",
            type.IsEnabled && page.FormType == "noop" && !Words().Contains("edited in the config file"),
            $"enabled {type.IsEnabled}, type {page.FormType}, {Words()}");

        await LoadTestConfigAsync(original);
        window.AppWindow.Hide();
    }
}
