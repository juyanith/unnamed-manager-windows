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

    private static readonly string[] Pages = ["wpf-guide", "wpf-guide-held", "wpf-guide-home", "wpf-guide-error", "wpf-tray"];

    private static readonly LoadedConfig Held = ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "CAG+A", "title": "Apps", "hold": true },
          { "keys": "CAG+A F", "title": "Fill", "action": "window-fill" },
          { "keys": "CAG+A M", "title": "Minimize", "action": "window-minimize" },
          { "keys": "CAG+A R", "title": "Arrange", "action": "window-arrange" },
          { "keys": "CAG+A H", "title": "Help", "action": "help" } ] }
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

        var config = Themed(page == "wpf-guide-held" ? Held : sample, theme);
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
            var next = engine.Handle(KeyEvent.Down(page == "wpf-guide-home" ? KeyName.Backspace : KeyName.A)).Effect;
            if (next is SequenceEffect.ShowMenu or SequenceEffect.ShowHome) shown = next;
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
