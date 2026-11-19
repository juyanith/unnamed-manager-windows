using UnnamedManager.Core.Config;
using UnnamedManager.Pages;

namespace UnnamedManager;

/// The self-test's checks for built-in actions in the Bindings window (S-070, S-071):
/// the Built in menu, overrides on the Actions page, and the list on Help.
public static partial class SelfTest
{
    private static async Task CheckBuiltInsAsync(MainWindow window)
    {
        var file = await File.ReadAllTextAsync(App.Config.Path);
        window.Open("bindings");
        await Task.Delay(400);
        if (window.CurrentPage is not BindingsPage bindings) { Check("the Bindings page opens", false); return; }

        // S-070: choosing a built-in action saves its name in the binding and nothing in actions.
        var menu = string.Join(",", bindings.BuiltInMenuLabels());
        bindings.Select(bindings.All().First(i => i.Title.Contains("Terminal")));
        await Task.Delay(200);
        bindings.SetAction("window-swap-left");
        await Task.Delay(300);
        var text = await File.ReadAllTextAsync(App.Config.Path);
        Check("the Built in menu has Place Window, Focus Window, Swap Window, Arrange Windows, Resize Window, Switch Desktop and Send to Desktop, then Fill, Minimize, Show Help and Do Nothing",
            menu == "Place Window (14),Focus Window (9),Swap Window (9),Arrange Windows (8),Resize Window (2),Switch Desktop (2),Send to Desktop (2),Fill,Minimize,Show Help,Do Nothing", menu);
        Check("choosing a built-in action saves its name in the binding and nothing in actions",
            text.Contains("\"action\": \"window-swap-left\"") && !text.Contains("\"window-swap-left\":") && bindings.DetailActionText == "Swap Window: Left, built in",
            bindings.DetailActionText);

        // S-070: an action with a built-in name overrides it; deleting it while in use gives the built-in one back.
        App.Editor.Change("Override", c => Core.Config.ConfigEdits.SetActionDefinition(c, "window-swap-left", new Core.Config.BasicAction("noop")));
        await Task.Delay(200);
        var overriding = App.Config.Current!.Config.Resolve("window-swap-left") is Core.Config.BasicAction;
        window.Open("actions");
        await Task.Delay(400);
        var actions = window.CurrentPage as ActionsPage;
        actions?.Select("window-swap-left");
        await Task.Delay(200);
        var marked = actions?.SelectedDetail ?? "";
        actions?.Delete();
        await Task.Delay(300);
        Check("an action with a built-in name runs instead of it, and the Actions page marks it as an override",
            overriding && marked.EndsWith("override"), marked);
        Check("deleting an override that bindings use is allowed and gives them the built-in action back",
            App.Config.Current!.Config.Action("window-swap-left") is null && App.Config.Current.Config.Resolve("window-swap-left") is Core.Config.SwapAction
            && actions?.ProblemShown == false);

        // S-071: Help lists every built-in action by kind, with numbered windows, and marks an override.
        App.Editor.Change("Override", c => Core.Config.ConfigEdits.SetActionDefinition(c, "window-fill", new Core.Config.BasicAction("noop")));
        await Task.Delay(200);
        window.Open("help");
        await Task.Delay(400);
        if (window.CurrentPage is HelpPage help)
        {
            var kinds = string.Join(",", help.BuiltInRows.Select(k => $"{k.Kind} {k.Rows.Count}"));
            Check("Help lists every built-in action in the Built in menu's kinds, with a row for Focus Window's and Swap Window's numbered windows and Send to Desktop's numbered desktops",
                kinds == "Place Window 14,Focus Window 10,Swap Window 10,Arrange Windows 8,Resize Window 2,Switch Desktop 2,Send to Desktop 3,Other 4"
                && help.BuiltInRows[0].Rows[0] == "Left half|window-place-left-half|", kinds);
            Check("a built-in action the config overrides says so on Help",
                help.BuiltInRows[^1].Rows.Contains("Fill|window-fill|overridden"), string.Join(" / ", help.BuiltInRows[^1].Rows));
        }
        else Check("the Help page opens", false);

        while (App.Editor.UndoName is not null && await File.ReadAllTextAsync(App.Config.Path) != file) App.Editor.Undo();
        Check("undo restores the file", await File.ReadAllTextAsync(App.Config.Path) == file);
        window.AppWindow.Hide();
    }
}
