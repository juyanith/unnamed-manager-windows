using UnnamedManager.Actions;
using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using UnnamedManager.Keyboard;
using UnnamedManager.Pages;

namespace UnnamedManager;

/// The self-test's checks for spec v17 to v20 (S-072 to S-077): held menus through the
/// hook, Recent Windows on fake windows, the Bindings page's Hold to choose and Contents,
/// Help's Held menus, and Switch Desktop and Send to Desktop.
public static partial class SelfTest
{
    private const int VkAlt = 0xA4, ScanAlt = 0x38, VkTab = 0x09, ScanTab = 0x0F;

    private static bool Key(KeyboardHook hook, int vk, int scan, bool up) =>
        hook.Decide(new KeyboardHook.KBDLLHOOKSTRUCT { vkCode = (uint)vk, scanCode = (uint)scan, flags = up ? KeyboardHook.LLKHF_UP : 0 });

    private static async Task CheckHeldAsync(MainWindow window)
    {
        var keys = App.Keys;
        if (keys.Hook is not { } hook || keys.Tracker is not { } tracker || App.Config.Current is not { } real) { Check("the hook is there for held menus", false); return; }
        var saved = ActionRunner.Windows;
        var savedKeyDown = App.IsKeyDown;
        var fake = new FakeWindowSystem { DesktopCount = 2 };
        ActionRunner.Windows = new WindowControl(fake);
        var held = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Alt+Tab", "title": "Recent windows", "hold": true, "list": { "type": "windows.recent" } } ] }
            """);
        Check("Alt+Tab can be a held leader with a Recent Windows list", held.IsValid, string.Join(" | ", held.Errors));
        if (!held.IsValid) return;
        foreach (var (h, title) in new[] { (11, "Notes"), (12, "Mail"), (13, "Code") }) fake.Add(h, new PixelRect(100 * h, 100, 800, 600)).Title = title;
        fake.Click(11);
        fake.Click(12);
        fake.Click(13);
        keys.Start(held.Loaded!);
        // The pretend Alt is down until the check lets it go.
        App.IsKeyDown = _ => true;
        try
        {
            // S-073, S-074: Alt+Tab opens the list held, on the window used before, with the focused one as Cancel.
            Key(hook, VkAlt, ScanAlt, up: false);
            var tab = Key(hook, VkTab, ScanTab, up: false);
            Key(hook, VkTab, ScanTab, up: true);
            await Task.Delay(400);
            var view = App.Guide.View;
            var rows = view.Rows.Select(r => r.Title).ToList();
            Check("Alt+Tab is taken from Windows and shows Recent windows held, the focused window as Cancel and the one used before highlighted",
                tab && App.Guide.IsShown && view.Highlight == 1 && string.Join(",", rows) == "Code,Mail,Notes" && view.Rows[1].IsHighlighted && view.Rows[0].Key == "",
                $"{tab} {view.Highlight} {string.Join(",", rows)}");
            Key(hook, VkTab, ScanTab, up: false);
            Key(hook, VkTab, ScanTab, up: true);
            await Task.Delay(200);
            Check("Tab again moves the highlight", view.Highlight == 2, $"{view.Highlight}");
            Key(hook, VkAlt, ScanAlt, up: true);
            await Task.Delay(300);
            Check("letting go of Alt focuses the highlighted window and closes the Guide", fake.Front == 11 && !App.Guide.IsShown && !tracker.IsActive, $"{fake.Front}");

            // A missed key-up is noticed within 200 ms.
            Key(hook, VkAlt, ScanAlt, up: false);
            Key(hook, VkTab, ScanTab, up: false);
            Key(hook, VkTab, ScanTab, up: true);
            await Task.Delay(200);
            App.IsKeyDown = _ => false;
            await Task.Delay(300);
            Check("a key-up the hook missed still chooses, within 200 ms", fake.Front == 13 && !tracker.IsActive, $"{fake.Front}");

            // An item's window closing while the list is up leaves the list.
            App.IsKeyDown = _ => true;
            Key(hook, VkAlt, ScanAlt, up: false);
            Key(hook, VkTab, ScanTab, up: false);
            Key(hook, VkTab, ScanTab, up: true);
            await Task.Delay(300);
            fake.Remove(11);
            await Task.Delay(300);
            var left = string.Join(",", view.Rows.Select(r => r.Title));
            Key(hook, VkAlt, ScanAlt, up: true);
            await Task.Delay(200);
            Check("a window that closes while Recent windows is up leaves it", left == "Code,Mail", left);

            // S-076: a list that swaps trades places with the chosen window, and focus stays.
            var swap = ConfigLoader.Load("""
                { "version": 1, "entries": [ { "keys": "Alt+Tab", "title": "Swap with", "hold": true, "list": { "type": "windows.recent", "choose": "swap" } } ] }
                """).Loaded!;
            keys.Start(swap);
            PixelRect mail = fake.Windows[12].Frame, code = fake.Windows[13].Frame;
            Key(hook, VkAlt, ScanAlt, up: false);
            Key(hook, VkTab, ScanTab, up: false);
            Key(hook, VkTab, ScanTab, up: true);
            await Task.Delay(300);
            Key(hook, VkAlt, ScanAlt, up: true);
            await Task.Delay(300);
            Check("a list with When chosen set to Swap trades places with the chosen window, and focus stays",
                fake.Windows[13].Frame == mail && fake.Windows[12].Frame == code && fake.Front == 12, $"{fake.Front}");
        }
        finally
        {
            App.IsKeyDown = savedKeyDown;
            tracker.Reset();
            keys.Start(real);
            ActionRunner.Windows = saved;
            App.Guide.Hide();
        }
        await CheckDesktopsAsync();
    }

    /// S-072, S-077: Switch Desktop and Send to Desktop through ActionRunner, on fake
    /// windows; and whether Windows lists its desktops for the real ones.
    private static async Task CheckDesktopsAsync()
    {
        var saved = ActionRunner.Windows;
        var fake = new FakeWindowSystem { DesktopCount = 3 };
        ActionRunner.Windows = new WindowControl(fake);
        static Task<string?> Run(string name) => ActionRunner.PerformAsync(ActionRegistry.Standard.Family(BuiltInActions.Action(name)!.Type)!.Plan(BuiltInActions.Action(name)!, PlanContext.Current)!);
        try
        {
            fake.Add(1, new PixelRect(0, 0, 800, 600));
            fake.Add(2, new PixelRect(100, 0, 800, 600));
            await Run("space-switch-right");
            Check("space-switch-right goes to the desktop on the right", fake.CurrentDesktop == 1 && fake.Switches == 1, $"{fake.CurrentDesktop}");
            fake.CurrentDesktop = 0;
            fake.Click(1);
            fake.Click(2);
            var beeps = UI.Sound.Played;
            var sent = await Run("space-send-3");
            Check("space-send-3 puts the window on desktop 3, stays here, and focuses the window used before",
                sent is null && fake.Windows[2].Desktop == 2 && fake.CurrentDesktop == 0 && fake.Front == 1, $"{sent} {fake.Front}");
            var past = await ActionRunner.PerformAsync(new Core.Config.ActionPlan.Window(new WindowCommand.SendToDesktop(SpaceTarget.Of(5), false)));
            Check("a desktop past the last says so with the alert sound", past == "No desktop 5. There are 3." && UI.Sound.Played == beeps + 1, past ?? "");
        }
        finally { ActionRunner.Windows = saved; }

        var real = new Win32WindowSystem().Desktops();
        if (real is null && Ci) AgentLog.Line("SKIP Windows lists its virtual desktops: not on this runner's Windows");
        else Check("Windows lists its virtual desktops and says which is current", real is { Count: >= 1, Current: >= 1 } && real.Value.Current <= real.Value.Count, $"{real}");
    }

    /// S-073, S-074, S-076: Hold to choose and Contents on the Bindings page, and Help's Held menus.
    public static async Task CheckHoldEditingAsync(MainWindow window)
    {
        window.Show("bindings");
        await Task.Delay(400);
        if (window.CurrentPage is not BindingsPage page) { Check("the Bindings page opens for Hold to choose", false); return; }
        var file = await File.ReadAllTextAsync(App.Config.Path);
        var leader = page.Items.FirstOrDefault(i => i.IsMenu && i.Node.Path.IsLeader);
        if (leader is null) { Check("the test config has a leader menu", false); return; }
        page.Select(leader);
        page.SetHold(true);
        await Task.Delay(300);
        var text = await File.ReadAllTextAsync(App.Config.Path);
        var repeat = BindingsPage.KeyProblemText(leader.Children[0].Node.Path, new Chord(Modifiers.None, leader.Node.Path.Leader.Key));
        Check("Hold to choose saves hold on the leader, and its key is refused for a key under it",
            text.Contains($"\"keys\": \"{leader.Node.Path}\", \"title\": \"{leader.Title}\", \"hold\": true") && repeat?.Contains("moves the highlight") == true, $"{App.CanEdit} {page.Selected?.Node.Path} {repeat} {text.Split('\n').FirstOrDefault(l => l.Contains(leader.Title))}");

        // A new menu's Contents: Recent windows, then Swap with the window, which leaves out minimized.
        page.Select(leader);
        App.Editor.Change("New menu", c => c.AddEntry(leader.Node.Path, "Recent", null, out _));
        await Task.Delay(200);
        page.Refresh();
        var menu = page.Items.SelectMany(Flatten).FirstOrDefault(i => i.Title == "Recent" && i.Children.Count == 0);
        if (menu is null) { Check("a new empty menu is on the Bindings page", false); return; }
        page.Select(menu);
        page.SetList(new ListSpec());
        await Task.Delay(300);
        page.SetList(new ListSpec(Choose: "swap", Scope: "display"));
        await Task.Delay(300);
        text = await File.ReadAllTextAsync(App.Config.Path);
        Check("Contents makes a menu Recent windows, and When chosen Swap saves choose",
            text.Contains("\"title\": \"Recent\", \"list\": { \"type\": \"windows.recent\", \"scope\": \"display\", \"choose\": \"swap\" }")
            && page.DetailActionText.StartsWith("A list of the windows"), text.Split('\n').FirstOrDefault(l => l.Contains("Recent")) ?? "");
        while (App.Editor.UndoName is not null && await File.ReadAllTextAsync(App.Config.Path) != file) App.Editor.Undo();
        Check("Ctrl+Z undoes Hold to choose and Contents", await File.ReadAllTextAsync(App.Config.Path) == file);

        window.Show("help");
        await Task.Delay(400);
        if (window.CurrentPage is HelpPage help)
        {
            var rows = string.Join(" | ", help.HeldText);
            Check("Help explains held menus: hold, the repeat key, Shift, letting go and Cancel",
                rows.Contains("Repeat key") && rows.Contains("Let go") && rows.Contains("Shift") && rows.Contains("Cancel"), rows);
        }
        else Check("the Help page opens for Held menus", false);
        window.AppWindow.Hide();
    }

    private static IEnumerable<BindingItem> Flatten(BindingItem item) => [item, .. item.Children.SelectMany(Flatten)];
}
