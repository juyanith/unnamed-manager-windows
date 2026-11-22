using UnnamedManager.Core.Config;
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
        await File.WriteAllTextAsync(App.Config.Path, Core.Config.ConfigLoader.StarterJson.Replace("\"action\": \"open-terminal\"", "\"action\": \"quick-note\""));
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
            asked == "Delete “Commands”? The 8 entries under it go too. You can undo this with Ctrl+Z.", asked);
        Check("after the question, the menu and everything under it go from the file",
            !after.Contains("\"Hyper+Space") && after.Contains("\"Hyper+H\""));
        App.Editor.Undo();
        page.Select(page.All().First(i => i.Title.Contains("Terminal")));
        await page.DeleteAsync();
        var gone = await File.ReadAllTextAsync(App.Config.Path);
        Check("a binding goes at once, with no question, and the next one is selected",
            page.Dialog is null && !gone.Contains("open-terminal\" }") && page.Selected is null or { Title: "Commands" } or { Title: "File Explorer" } or { Title: "Windows" }, page.Selected?.Title ?? "");
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
            text.IndexOf("Hyper+Space T", StringComparison.Ordinal) < text.IndexOf("Hyper+Space F", StringComparison.Ordinal) && guide.Split(',')[1] == "File Explorer" && page.Selected!.Title.Contains("Terminal"), guide);
        var moved = page.Dropped(page.Selected!, null, 1);
        await Task.Delay(200);
        Check("an item can't be dragged into another menu, which would change its key path",
            !moved && await File.ReadAllTextAsync(App.Config.Path) == text);
        App.Editor.Undo();
        await Task.Delay(200);
        Check("undo puts the order back", await File.ReadAllTextAsync(App.Config.Path) == file);
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
        var edited = page.Selected!.Node.Path;
        var withoutNew = await File.ReadAllTextAsync(App.Config.Path);
        page.SetAction(Core.Config.ConfigEdits.NewActionChoice);
        await Task.Delay(600);
        Check("New action… makes a Do Nothing action and uses it",
            App.Config.Current!.Config.Action("new-action") is Core.Config.BasicAction { Kind: "noop" } && App.Config.Current.Config.Entry(edited)?.Action == "new-action");
        Check("and opens the Actions page on it, with its Id focused",
            window.CurrentPage is ActionsPage { Selected.Id: "new-action", IdIsFocused: true });
        App.Editor.Undo();
        await Task.Delay(300);
        Check("one Ctrl+Z takes out the action and the binding's use of it", await File.ReadAllTextAsync(App.Config.Path) == withoutNew);
        window.Show("bindings");
        await Task.Delay(400);
        while (App.Editor.UndoName is not null && await File.ReadAllTextAsync(App.Config.Path) != file) App.Editor.Undo();
        Check("undo restores the entry", await File.ReadAllTextAsync(App.Config.Path) == file);
        window.AppWindow.Hide();
    }

    /// S-091: a repeat-key clash is fixed from its error.
    private static async Task CheckRepeatFixAsync(MainWindow window)
    {
        var original = await File.ReadAllTextAsync(App.Config.Path);
        await File.WriteAllTextAsync(App.Config.Path, """
            { "version": 1, "entries": [ { "keys": "Alt+Space", "title": "Held" }, { "keys": "Alt+Space Space", "title": "Clash", "action": "noop" } ] }
            """);
        App.Config.Reload();
        await Task.Delay(300);
        window.Open("bindings");
        await Task.Delay(500);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens for a repeat key", false); return; }
        page.Refresh();
        page.Select(page.All().First(i => i.Title == "Held"));
        var file = await File.ReadAllTextAsync(App.Config.Path);
        page.SetHold(true);
        await Task.Delay(300);
        var fixes = page.KeyFixes.Where(b => b.Content as string == "Change the key").ToList();
        Check("Hold to choose over a clashing key leaves the file untouched and offers Change the key",
            fixes.Count == 1 && await File.ReadAllTextAsync(App.Config.Path) == file, $"{fixes.Count} buttons");
        var clash = KeyPath.Parse("Alt+Space Space")!;
        page.ChangeClashingKey(clash);
        await Task.Delay(200);
        Check("Change the key selects that row with its recorder on", Equals(page.Selected?.Node.Path, clash) && Equals(page.Recording, clash));
        page.OnRecorded(new Chord(Modifiers.None, KeyName.X));
        await Task.Delay(300);
        var saved = App.Config.Current!.Config;
        Check("the recorded key clears the error and saves both changes",
            App.Editor.DraftErrors.Count == 0 && !page.KeyFixes.Any(b => b.Content as string == "Change the key")
            && saved.Entries[0].Hold && saved.Entries[1].Keys == "Alt+Space X", saved.Entries[1].Keys);
        await File.WriteAllTextAsync(App.Config.Path, original);
        App.Config.Reload();
        await Task.Delay(300);
        window.AppWindow.Hide();
    }

    /// S-092: Contents on a menu with keys under it.
    private static async Task CheckContentsAsync(MainWindow window)
    {
        var original = await File.ReadAllTextAsync(App.Config.Path);
        await File.WriteAllTextAsync(App.Config.Path, """
            { "version": 1, "entries": [ { "keys": "Hyper+M", "title": "Menu" }, { "keys": "Hyper+M A", "title": "Only", "action": "noop" } ] }
            """);
        App.Config.Reload();
        await Task.Delay(300);
        window.Open("bindings");
        await Task.Delay(500);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens for Contents", false); return; }
        page.Refresh();
        var menu = KeyPath.Parse("Hyper+M")!;
        page.Select(page.Find(menu));
        page.SetList(new ListSpec());
        Check("a menu with keys under it shows Keys you add, says why, and can't be Recent windows",
            page.Contents == (true, 0, false, true) && App.Editor.Draft.Entry(menu)?.List is null, $"{page.Contents}");
        App.Editor.Change("Delete", c => c.RemoveEntry(KeyPath.Parse("Hyper+M A")!, out _));
        await Task.Delay(200);
        page.Refresh(menu);
        Check("with the last key removed, Recent windows can be chosen", page.Contents == (true, 0, true, false), $"{page.Contents}");
        await File.WriteAllTextAsync(App.Config.Path, original);
        App.Config.Reload();
        await Task.Delay(300);
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

    /// "Ctrl+Shift+N" for an accelerator, the way tooltips and menus write it.
    private static string Shortcut(Microsoft.UI.Xaml.Input.KeyboardAccelerator k)
    {
        var parts = new List<string>();
        if (k.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control)) parts.Add("Ctrl");
        if (k.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Menu)) parts.Add("Alt");
        if (k.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(k.Key switch { >= Windows.System.VirtualKey.Number0 and <= Windows.System.VirtualKey.Number9 => ((int)k.Key - 48).ToString(), _ => k.Key.ToString() });
        return string.Join("+", parts);
    }

    /// S-052: every command on every page has a label and a shortcut, its own tooltip
    /// names that shortcut, the context menu uses the same names and shortcuts, and
    /// Narrator reads a row as "key, title, action".
    private static async Task CheckCommandsAsync(MainWindow window)
    {
        window.Open("bindings");
        var shortcuts = new Dictionary<string, string>();
        foreach (var id in new[] { "bindings", "actions", "contexts", "help", "settings" })
        {
            window.Show(id);
            await Task.Delay(400);
            foreach (var button in Find<AppBarButton>(window.RootElement))
            {
                var accelerator = button.KeyboardAccelerators.FirstOrDefault();
                var tip = ToolTipService.GetToolTip(button) as string;
                var shortcut = accelerator is null ? null : Shortcut(accelerator);
                if (shortcut is not null) shortcuts.TryAdd(button.Label, shortcut);
                Check($"{id}: '{button.Label}' has a label and a shortcut, and its tooltip names it",
                    !string.IsNullOrEmpty(button.Label) && shortcut is not null && (tip is null || tip.EndsWith($"({shortcut})")), tip ?? "");
            }
        }
        window.Show("bindings");
        await Task.Delay(400);
        if (window.CurrentPage is not BindingsPage page) return;
        var items = page.MenuItems.ToList();
        var mismatched = items.Where(i => shortcuts.TryGetValue(i.Text, out var s) && s != i.KeyboardAcceleratorTextOverride).Select(i => i.Text).ToList();
        Check("every context menu item has a shortcut, with the command bar's name and shortcut",
            items.All(i => !string.IsNullOrEmpty(i.KeyboardAcceleratorTextOverride)) && mismatched.Count == 0
            && new[] { "New binding", "New menu", "Delete" }.All(n => items.Any(i => i.Text == n)), string.Join(",", mismatched));
        var before = await File.ReadAllTextAsync(App.Config.Path);
        page.Select(page.All().First(i => i.Title.Contains("Terminal")));
        page.Duplicate();
        await Task.Delay(300);
        var copyOf = page.Selected?.Title;
        var written = await File.ReadAllTextAsync(App.Config.Path);
        Check("Duplicate selects the copy and saves it, and is in the context menu with Ctrl+D",
            copyOf?.EndsWith(" copy") == true && written.Contains(copyOf) && items.Any(i => i.Text == "Duplicate" && i.KeyboardAcceleratorTextOverride == "Ctrl+D"), copyOf ?? "none");
        App.Editor.Undo();
        await Task.Delay(300);
        Check("Ctrl+Z after Duplicate restores the file exactly", await File.ReadAllTextAsync(App.Config.Path) == before);
        var sort = Find<CommandBar>(page).First().SecondaryCommands.OfType<AppBarButton>().First(b => b.Label == "Sort alphabetically");
        page.Select(page.All().First(i => !i.IsMenu));
        var offForBinding = !sort.IsEnabled;
        App.Editor.Change("Two out of order", c => c.AddEntry(null, "Sort test", null, out var menu).AddEntry(menu, "First", "open-terminal", out var first)
            .ChangeKey(first, new Core.Config.Chord(Core.Config.Modifiers.None, Core.Config.KeyName.Z), out _).AddEntry(menu, "Second", "open-terminal", out _));
        await Task.Delay(300);
        var unsorted = await File.ReadAllTextAsync(App.Config.Path);
        page.Refresh();
        page.Select(page.All().First(i => i.Title == "Sort test"));
        var onForMenu = sort.IsEnabled;
        page.Sort();
        await Task.Delay(300);
        var sorted = await File.ReadAllTextAsync(App.Config.Path);
        Check("Sort alphabetically is off for a binding, on for a menu, and writes the new order",
            offForBinding && onForMenu && sorted.IndexOf("\"Second\"") is var second and >= 0 && second < sorted.IndexOf("\"First\""), $"off {offForBinding}, on {onForMenu}");
        App.Editor.Undo();
        await Task.Delay(300);
        Check("Ctrl+Z after Sort restores the file exactly", await File.ReadAllTextAsync(App.Config.Path) == unsorted);
        App.Editor.Undo();
        await Task.Delay(300);
        page.Refresh();
        var row = page.All().First(i => i.Title.Contains("Terminal"));
        page.Select(row);
        await Task.Delay(200);
        var container = Find<TreeViewItem>(page).FirstOrDefault(t => (page.TreeControl.NodeFromContainer(t)?.Content as BindingItem) == row);
        var spoken = container is null ? "" : Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(container).GetName();
        Check("Narrator reads a row as key, title, action", spoken == $"T, {row.Title}, open-terminal", spoken);
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

    /// 0.5.1: with a list selected, New binding and New menu add beside it, on a key the
    /// held menu allows.
    private static async Task CheckNewBesideListAsync(MainWindow window)
    {
        var original = await File.ReadAllTextAsync(App.Config.Path);
        await LoadTestConfigAsync("""
            { "version": 1, "entries": [
              { "keys": "CAG+A", "title": "Held", "hold": true },
              { "keys": "CAG+A R", "title": "Recent", "list": { "type": "windows.recent" } },
              { "keys": "Alt+Tab", "title": "Switcher", "hold": true, "list": { "type": "windows.recent" } } ] }
            """);
        window.Open("bindings");
        await Task.Delay(400);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens beside a list", false); return; }
        async Task<string> New(string title, bool menu)
        {
            page.Select(page.All().First(i => i.Title == title));
            if (menu) page.NewMenu(); else page.NewBinding();
            await Task.Delay(200);
            var added = App.Editor.Draft.Entries.Single(e => e.Title == (menu ? "New menu" : "New binding")).Keys;
            page.StopRecording();
            App.Editor.Undo();
            await Task.Delay(200);
            return added;
        }
        var binding = await New("Recent", menu: false);
        Check("New binding on a list goes beside it, not on the held menu's repeat key", binding == "CAG+A B", binding);
        var newMenu = await New("Recent", menu: true);
        Check("New menu on a list goes beside it too", newMenu == "CAG+A B", newMenu);
        var top = await New("Switcher", menu: false);
        Check("New binding on a list that's a leader adds a direct binding", !top.Contains(' '), top);

        await LoadTestConfigAsync(original);
        window.AppWindow.Hide();
    }

    /// What a screen reader's Select does: the selection moves with no ItemInvoked, as
    /// it does for the arrow keys.
    private static async Task SelectRowAsync(BindingsPage page, string title)
    {
        var container = Find<TreeViewItem>(page).FirstOrDefault(t => (page.TreeControl.NodeFromContainer(t)?.Content as BindingItem)?.Title == title);
        if (container is null) return;
        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(container);
        ((Microsoft.UI.Xaml.Automation.Provider.ISelectionItemProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.SelectionItem)).Select();
        await Task.Delay(300);
    }

    /// 0.5.1: the card follows the selection, and the Title box writes only what you typed,
    /// to the row it shows.
    private static async Task CheckTitleBoxAsync(MainWindow window)
    {
        var original = await File.ReadAllTextAsync(App.Config.Path);
        await LoadTestConfigAsync("""
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Commands" },
              { "keys": "Hyper+Space L", "title": "First\nSecond", "action": "noop" },
              { "keys": "Hyper+Space P", "title": " Padded ", "action": "noop" },
              { "keys": "Hyper+Space A", "title": "Alpha", "action": "noop" },
              { "keys": "Hyper+Space D", "title": "Delta", "action": "help" },
              { "keys": "Hyper+Space R", "title": "Recent", "list": { "type": "windows.recent", "count": 12, "minimized": false } },
              { "keys": "Alt+Tab", "title": "Switcher", "hold": true, "list": { "type": "windows.recent" } } ] }
            """);
        window.Open("bindings");
        await Task.Delay(500);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens for the Title box", false); return; }
        var box = Find<TextBox>(page).First(b => b.Header as string == "Title");
        string Saved(string key) => App.Config.Current!.Config.Entries.First(e => e.Keys == $"Hyper+Space {key}").Title;
        async Task Leave()
        {
            page.TreeControl.Focus(FocusState.Programmatic);
            await Task.Delay(300);
        }
        async Task TabThrough()
        {
            page.FocusTitle();
            await Task.Delay(150);
            await Leave();
        }

        page.Select(page.All().First(i => i.Key == "L"));
        await Task.Delay(200);
        var shown = box.Text.ReplaceLineEndings("|");
        Check("the Title box shows every line of a title", shown == "First|Second", shown);
        await TabThrough();
        Check("tabbing through a title with a line break changes nothing", Saved("L") == "First\nSecond", Saved("L"));

        page.Select(page.All().First(i => i.Key == "L"));
        page.FocusTitle();
        await Task.Delay(150);
        box.Text = "First\rThird";
        await Leave();
        Check("editing a title with a line break keeps the break", Saved("L") == "First\nThird", Saved("L"));

        page.Select(page.All().First(i => i.Key == "P"));
        await TabThrough();
        Check("tabbing through a title with spaces around it changes nothing", Saved("P") == " Padded ", Saved("P"));

        page.Select(page.All().First(i => i.Key == "A"));
        await Task.Delay(200);
        await SelectRowAsync(page, "Delta");
        Check("the card follows a selection made without a click", box.Text == "Delta" && !box.AcceptsReturn && page.DetailActionText.StartsWith("Show Help"),
            $"{box.Text}, {page.DetailActionText}");
        await TabThrough();
        Check("tabbing through the Title box never gives a row another row's title", Saved("D") == "Delta", Saved("D"));

        var before = await File.ReadAllTextAsync(App.Config.Path);
        foreach (var title in page.All().Select(i => i.Title).ToList()) await SelectRowAsync(page, title);
        foreach (var title in page.All().Select(i => i.Title).Reverse().ToList()) await SelectRowAsync(page, title);
        Check("moving the selection through every row changes nothing", await File.ReadAllTextAsync(App.Config.Path) == before);

        page.Select(page.All().First(i => i.Key == "A"));
        page.FocusTitle();
        await Task.Delay(150);
        box.Text = "Alpha two";
        await SelectRowAsync(page, "Delta");
        await Leave();
        Check("a title typed before the selection moved goes to the row it was typed for",
            Saved("A") == "Alpha two" && Saved("D") == "Delta", $"A {Saved("A")}, D {Saved("D")}");

        await LoadTestConfigAsync(original);
        window.AppWindow.Hide();
    }
}
