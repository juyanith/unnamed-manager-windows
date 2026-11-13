using UnnamedManager.Keyboard;
using UnnamedManager.Pages;
using UnnamedManager.UI;
using Microsoft.UI.Xaml;
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

    /// Presses keys through the hook's callback, as the keyboard would: modifier
    /// virtual keys held down, then one key.
    private static void Press(int[] modifiers, Core.Config.KeyName key)
    {
        var hook = App.Keys.Hook!;
        KeyboardHook.KBDLLHOOKSTRUCT Raw(int vk, int scan, bool up) => new()
        {
            vkCode = (uint)vk, scanCode = (uint)(scan & 0xFF), flags = (uint)((scan > 0xFF ? 1 : 0) | (up ? 0x80 : 0)),
        };
        foreach (var vk in modifiers) hook.Decide(Raw(vk, 0, false));
        hook.Decide(Raw(0, Core.Config.Keys.ScanCode(key), false));
        hook.Decide(Raw(0, Core.Config.Keys.ScanCode(key), true));
        foreach (var vk in modifiers.Reverse()) hook.Decide(Raw(vk, 0, true));
    }

    private static readonly int[] Hyper = [0xA2, 0xA4, 0xA0, 0x5B];

    /// S-040: the recorder takes chords through the hook, and says why a key can't be used.
    private static async Task CheckRecordKeyAsync(MainWindow window)
    {
        window.Open("bindings");
        await Task.Delay(300);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens", false); return; }
        var leader = page.All().First(i => i.Title == "Commands");
        page.Select(leader);
        page.StartRecording(leader.Node.Path);
        var guideShown = App.GuideCreated && App.Guide.IsShown;
        Press(Hyper, Core.Config.KeyName.J);
        await Task.Delay(300);
        var file = await File.ReadAllTextAsync(App.Config.Path);
        Check("while recording, Hyper+J records as the leader's key and starts no sequence, and the file has it in canonical form",
            page.Recording is null && file.Contains("\"keys\": \"Hyper+J\"") && file.Contains("\"keys\": \"Hyper+J T\"")
            && App.Keys.Tracker?.IsActive == false && (App.GuideCreated && App.Guide.IsShown) == guideShown,
            $"recording {page.Recording}, selected {page.Selected?.Node.Path}");
        page.StartRecording(page.Selected!.Node.Path);
        Press([0x5B], Core.Config.KeyName.J);
        await Task.Delay(300);
        Check("a Win chord records instead of reaching Windows", page.Selected?.Node.Path.ToString() == "Cmd+J" && !App.Keys.Recording);

        var terminal = page.All().First(i => i.Title.Contains("Terminal"));
        page.StartRecording(terminal.Node.Path);
        Press([0xA2], Core.Config.KeyName.X);
        await Task.Delay(200);
        var modifier = page.KeyErrorText;
        Press([], Core.Config.KeyName.F);
        await Task.Delay(200);
        var taken = page.KeyErrorText;
        Press([], Core.Config.KeyName.Escape);
        await Task.Delay(200);
        Check("a step records one key with no modifiers, and a clash says why under the field while the recorder stays on",
            modifier == "Keys inside a menu can't use modifiers. Only the leader can." && taken == "F is already used by “File Explorer”." && page.Recording is null,
            $"{modifier} | {taken}");
        Press([], Core.Config.KeyName.Escape);
        App.Editor.Undo();
        App.Editor.Undo();
        await Task.Delay(200);
        Check("undo puts the keys back", (await File.ReadAllTextAsync(App.Config.Path)).Contains("\"keys\": \"Hyper+Space T\""));
        window.AppWindow.Hide();
    }

    /// S-042: Delete removes an entry; a menu with entries asks first, saying how many.
    private static async Task CheckDeleteAsync(MainWindow window)
    {
        window.Open("bindings");
        await Task.Delay(300);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens", false); return; }
        var file = await File.ReadAllTextAsync(App.Config.Path);
        page.Select(page.All().First(i => i.Title == "Commands"));
        var deleting = page.DeleteAsync();
        await Task.Delay(500);
        var asked = page.Dialog is { } d ? $"{d.Title} {d.Content}" : "";
        page.AnswerDialog(yes: true);
        await deleting;
        var after = await File.ReadAllTextAsync(App.Config.Path);
        Check("deleting a menu with entries asks first and says how many go",
            asked == "Delete “Commands”? The 2 entries under it go too. You can undo this with Ctrl+Z.", asked);
        // The starter's one leader can't go, as a config needs an entry: the draft keeps it with the reason.
        Check("deleting the only leader isn't saved, as a config needs an entry", after == file && App.Editor.DraftErrors.Count > 0);
        App.Editor.Undo();
        page.Select(page.All().First(i => i.Title.Contains("Terminal")));
        await page.DeleteAsync();
        var gone = await File.ReadAllTextAsync(App.Config.Path);
        Check("a binding goes at once, with no question, and the next one is selected",
            page.Dialog is null && !gone.Contains("open-terminal\" }") && page.Selected is null or { Title: "Commands" } or { Title: "File Explorer" }, page.Selected?.Title ?? "");
        App.Editor.Undo();
        await Task.Delay(200);
        Check("Ctrl+Z restores what was deleted", await File.ReadAllTextAsync(App.Config.Path) == file);
        window.AppWindow.Hide();
    }

    /// S-043: Alt+Up and Alt+Down reorder a menu, the Guide follows, and a drag into
    /// another menu snaps back.
    private static async Task CheckMoveAsync(MainWindow window)
    {
        window.Open("bindings");
        await Task.Delay(300);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens", false); return; }
        var file = await File.ReadAllTextAsync(App.Config.Path);
        page.Select(page.All().First(i => i.Title.Contains("Terminal")));
        page.Move(-1);
        await Task.Delay(200);
        var guide = string.Join(",", App.Config.Current!.Leaders[0].Children.Select(c => c.Title));
        var text = await File.ReadAllTextAsync(App.Config.Path);
        Check("Move up writes the new order to the file, and the Guide lists the items in that order",
            text.IndexOf("Hyper+Space T", StringComparison.Ordinal) < text.IndexOf("Hyper+Space F", StringComparison.Ordinal) && guide.EndsWith("File Explorer") && page.Selected!.Title.Contains("Terminal"), guide);
        var moved = page.Dropped(page.Selected!, null, 1);
        await Task.Delay(200);
        Check("an item can't be dragged into another menu, which would change its key path",
            !moved && await File.ReadAllTextAsync(App.Config.Path) == text);
        App.Editor.Undo();
        await Task.Delay(200);
        Check("undo puts the order back", await File.ReadAllTextAsync(App.Config.Path) == file);
        window.AppWindow.Hide();
    }

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
            ids.SequenceEqual(Core.Config.ConfigEdits.SortedActionIds(App.Editor.Draft)) && page.Items.All(i => i.Detail == $"Use Application · 1 binding"),
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

    /// S-041: a title or action change rewrites only that entry in the file.
    private static async Task CheckEditEntryAsync(MainWindow window)
    {
        window.Open("bindings");
        await Task.Delay(300);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens", false); return; }
        var file = await File.ReadAllTextAsync(App.Config.Path);
        int Changed(string a, string b) => a.Split('\n').Zip(b.Split('\n')).Count(p => p.First != p.Second);
        page.Select(page.All().First(i => i.Title.Contains("Terminal")));
        page.FocusTitle();
        page.SetTitle("Shell");
        await Task.Delay(200);
        var titled = await File.ReadAllTextAsync(App.Config.Path);
        page.SetAction("open-explorer");
        await Task.Delay(200);
        var acted = await File.ReadAllTextAsync(App.Config.Path);
        Check("changing the title or the action rewrites only that entry in the file",
            Changed(file, titled) == 1 && Changed(titled, acted) == 1 && acted.Contains("{ \"keys\": \"Hyper+Space T\", \"title\": \"Shell\", \"action\": \"open-explorer\" }"));
        page.SetTitle(" ");
        await Task.Delay(100);
        Check("an empty title isn't saved, and says why under the field", page.TitleErrorText is not null && await File.ReadAllTextAsync(App.Config.Path) == acted);
        page.SetAction(Core.Config.ConfigEdits.NewActionChoice);
        await Task.Delay(200);
        Check("New action… makes a Do Nothing action and uses it",
            App.Config.Current!.Config.Action("new-action") is Core.Config.BasicAction { Kind: "noop" } && page.Selected?.Node.Action == "new-action");
        while (App.Editor.UndoName is not null && await File.ReadAllTextAsync(App.Config.Path) != file) App.Editor.Undo();
        Check("undo restores the entry", await File.ReadAllTextAsync(App.Config.Path) == file);
        window.AppWindow.Hide();
    }

    /// S-049: clicking an error selects the row it names and focuses the field with the problem.
    private static async Task CheckErrorLinksAsync(MainWindow window)
    {
        window.Open("bindings");
        await Task.Delay(300);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens", false); return; }
        var file = await File.ReadAllTextAsync(App.Config.Path);
        page.Select(page.All().First(i => i.Title == "Commands"));
        page.NewBinding();
        page.StopRecording();
        page.Select(null);
        await Task.Delay(200);
        var link = page.ErrorLinks.FirstOrDefault();
        if (link is not null) new Microsoft.UI.Xaml.Automation.Peers.HyperlinkButtonAutomationPeer(link).Invoke();
        await Task.Delay(300);
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot) as FrameworkElement;
        Check("clicking “Choose an action” selects the new binding and focuses its Action list",
            page.Selected is { NeedsAction: true } && focused?.Name == "ActionBox", $"{page.Selected?.Node.Path}, focus {focused?.Name}");
        App.Editor.Undo();

        // A file with a key used twice: the row it names is selected, with its key field focused.
        await File.WriteAllTextAsync(App.Config.Path, file.Replace("\"keys\": \"Hyper+Space F\"", "\"keys\": \"Hyper+Space T\""));
        App.Config.Reload();
        await Task.Delay(300);
        link = page.ErrorLinks.FirstOrDefault();
        if (link is not null) new Microsoft.UI.Xaml.Automation.Peers.HyperlinkButtonAutomationPeer(link).Invoke();
        await Task.Delay(300);
        focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot) as FrameworkElement;
        Check("clicking an error in the file selects the row it names, with the tree focused, since the fix goes in the file",
            page.Selected?.Node.Path.ToString() == "Hyper+Space T" && focused is TreeViewItem or TreeView, $"{page.Selected?.Node.Path}, focus {focused?.Name}");
        await File.WriteAllTextAsync(App.Config.Path, file);
        App.Config.Reload();
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
        Check("Settings has the sections Appearance, Guide, Keys, Feedback, Windows and About, and no accent picker",
            string.Join(",", sections) == "Appearance,Guide,Keys,Feedback,Windows,About", string.Join(",", sections));

        Named<ComboBox>("ThemeBox").SelectedIndex = 1;
        Named<NumberBox>("DelayBox").Value = 350;
        Named<NumberBox>("TimeoutBox").Value = -200;
        Named<NumberBox>("GapBox").Value = 9;
        Named<ComboBox>("PositionBox").SelectedIndex = 1;
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
        Check("a cancel key with modifiers is refused and says why", modifier == "Use one key with no modifiers.", modifier ?? "");
        while (App.Editor.UndoName is not null && await File.ReadAllTextAsync(App.Config.Path) != file) App.Editor.Undo();
        Check("Ctrl+Z undoes each change", await File.ReadAllTextAsync(App.Config.Path) == file && App.ThemeMode == "system");
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
