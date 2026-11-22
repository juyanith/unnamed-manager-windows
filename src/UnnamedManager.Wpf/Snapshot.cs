using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;

namespace UnnamedManager;

/// The WPF app's snapshots (S-106): each page as a PNG in light and dark, from the real engine.
///   tools/dev.ps1 snap -App wpf [-Pages wpf-guide-held]
/// A story that changes the WPF Guide or tray adds its page here.
public static class Snapshot
{
    private static string? Folder => Environment.GetEnvironmentVariable("APP_SNAPSHOT");

    public static bool Requested => !string.IsNullOrEmpty(Folder);

    private static readonly string[] Pages = ["wpf-guide", "wpf-guide-held", "wpf-guide-home", "wpf-guide-error", "wpf-guide-icons", "wpf-guide-home-wide", "wpf-tray"];

    private static readonly LoadedConfig Held = ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "CAG+A", "title": "Apps", "hold": true },
          { "keys": "CAG+A F", "title": "Fill", "action": "window-fill" },
          { "keys": "CAG+A M", "title": "Minimize", "action": "window-minimize" },
          { "keys": "CAG+A R", "title": "Arrange", "action": "window-arrange" },
          { "keys": "CAG+A H", "title": "Help", "action": "help" } ] }
        """).Loaded!;

    private static readonly LoadedConfig OwnIcons = ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "Hyper+Space", "title": "Commands" },
          { "keys": "Hyper+Space H", "title": "Home folder", "icon": "U+E80F", "action": "x" },
          { "keys": "Hyper+Space S", "title": "Search", "icon": "U+E721", "action": "x" },
          { "keys": "Hyper+Space M", "title": "Music", "icon": "U+E8D6" },
          { "keys": "Hyper+Space M P", "title": "Play", "action": "x" },
          { "keys": "Hyper+Space G", "title": "Games" },
          { "keys": "Hyper+Space G C", "title": "Chess", "action": "x" },
          { "keys": "Hyper+Space N", "title": "Notes file", "icon": "C:\\Windows\\win.ini", "action": "x" },
          { "keys": "Hyper+Space X", "title": "No such glyph", "icon": "U+0E01", "action": "x" },
          { "keys": "Hyper+Space L", "title": "Left half", "action": "window-place-left-half" } ],
          "actions": { "x": { "type": "noop" } } }
        """).Loaded!;

    private static readonly LoadedConfig WideHome = ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "Hyper+Space", "title": "Commands" }, { "keys": "Hyper+Space F", "title": "Find", "action": "x" },
          { "keys": "Hyper+E", "title": "Editor", "action": "x" }, { "keys": "Hyper+B", "title": "Browser", "action": "x" },
          { "keys": "Hyper+M", "title": "Music", "action": "x" }, { "keys": "Hyper+C", "title": "Calendar", "action": "x" },
          { "keys": "Hyper+Z", "title": "Zoom", "action": "x" },
          { "keys": "Meh+Space", "title": "Windows" }, { "keys": "Meh+Space M", "title": "Maximize", "action": "x" },
          { "keys": "Meh+Left", "title": "Left half", "action": "x" }, { "keys": "Meh+Right", "title": "Right half", "action": "x" },
          { "keys": "Meh+Up", "title": "Top half", "action": "x" }, { "keys": "Meh+Down", "title": "Bottom half", "action": "x" },
          { "keys": "Ctrl+Alt+K", "title": "Keyboard layout", "action": "x" }, { "keys": "Ctrl+Alt+V", "title": "Clipboard history", "action": "x" },
          { "keys": "F13", "title": "Notes", "action": "x" }, { "keys": "F14", "title": "Scratchpad", "action": "x" } ],
          "actions": { "x": { "type": "noop" } } }
        """).Loaded!;

    public static async Task RunAsync()
    {
        var failed = false;
        try
        {
            Directory.CreateDirectory(Folder!);
            var only = Environment.GetEnvironmentVariable("APP_SNAPSHOT_PAGES")?.Split(',');
            if (App.Config.Current is not { } sample) throw new InvalidOperationException("the config didn't load");
            Guide.GuideIconCache.Prefetch(sample);
            Guide.GuideIconCache.Prefetch(OwnIcons);
            await Task.Delay(1500);
            foreach (var page in Pages)
            {
                if (only is not null && !only.Contains(page)) continue;
                foreach (var theme in new[] { "light", "dark" })
                {
                    var saved = await CaptureAsync(page, theme, sample, Path.Combine(Folder!, $"{page}-{theme}.png"));
                    AgentLog.Line(saved is null ? $"FAIL snapshot {page} {theme}: nothing to show" : $"SNAPSHOT {saved}");
                    failed |= saved is null;
                }
            }
        }
        catch (Exception e)
        {
            AgentLog.Line($"FAIL snapshots: {e.ToString().ReplaceLineEndings(" ")}");
            failed = true;
        }
        App.Shutdown(exitProcess: false);
        AgentLog.Exit(failed ? 1 : 0);
    }

    private static LoadedConfig Themed(LoadedConfig config, string theme) =>
        config with { Config = config.Config with { UI = config.Config.UI with { Theme = theme } } };

    private static async Task<string?> CaptureAsync(string page, string theme, LoadedConfig sample, string path)
    {
        if (page.StartsWith("wpf-tray"))
        {
            App.ApplyTheme(theme);
            App.TrayMenu.Open(0, 0, App.TrayStatusNow, App.TrayCommandsNow, keyboard: false);
            await Task.Delay(700);
            var saved = App.TrayMenu.SavePng(path);
            App.TrayMenu.Close();
            await Task.Delay(200);
            return saved;
        }

        var config = Themed(page switch { "wpf-guide-held" => Held, "wpf-guide-icons" => OwnIcons, "wpf-guide-home-wide" => WideHome, _ => sample }, theme);
        var engine = new SequenceEngine(config);
        SequenceEffect? shown;
        if (page == "wpf-guide-held")
        {
            engine.Handle(KeyEvent.Down(KeyName.A, Modifiers.Cag));
            shown = engine.Handle(KeyEvent.Down(KeyName.A, Modifiers.Cag)).Effect;
        }
        else
        {
            shown = engine.Handle(KeyEvent.Down(KeyName.Space, Modifiers.Hyper)).Effect;
            engine.ModifiersChanged(Modifiers.None);
            if (page != "wpf-guide-icons")
            {
                var next = engine.Handle(KeyEvent.Down(page.StartsWith("wpf-guide-home") ? KeyName.Backspace : KeyName.A)).Effect;
                if (next is SequenceEffect.ShowMenu or SequenceEffect.ShowHome) shown = next;
            }
        }

        switch (shown)
        {
            case not null when page == "wpf-guide-error":
                App.Guide.ShowFailure("Can't find Code.exe. Check that it's installed, or use its full path.", config);
                break;
            case SequenceEffect.ShowHeld held:
                App.Guide.Show(held.Menu, held.Path, config, null, held.Highlight);
                break;
            case SequenceEffect.ShowHome home:
                App.Guide.ShowHome(home.Home, home.Path, config, home.Front);
                break;
            case SequenceEffect.ShowMenu menu:
                App.Guide.Show(menu.Menu, menu.Path, config, menu.Front);
                break;
            default:
                return null;
        }
        await Task.Delay(600);
        var png = App.Guide.SavePng(path);
        App.Guide.Hide();
        return png;
    }
}
