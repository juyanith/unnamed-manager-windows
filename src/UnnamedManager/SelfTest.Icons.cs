using Microsoft.UI.Xaml;
using UnnamedManager.Core.Config;
using UnnamedManager.Guide;

namespace UnnamedManager;

/// The self-test's Guide icon checks (S-060).
public static partial class SelfTest
{
    private static async Task WaitForIconsAsync()
    {
        for (var i = 0; i < 100 && !GuideIconCache.Idle; i++) await Task.Delay(50);
    }

    /// Icons come from the exe, the Start entry, the shell and the URL's app; nothing
    /// found is no icon and no error; they load with the config and the Guide never waits.
    private static async Task CheckGuideIconsAsync()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        IconSource[] found =
        [
            new IconSource.Exe("notepad.exe"),
            new IconSource.AppId("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"),
            new IconSource.File(Path.Combine(windows, "win.ini")),
            new IconSource.File(windows),
            new IconSource.Url("https"),
        ];
        IconSource[] missing = [new IconSource.Exe("nope-not-here.exe"), new IconSource.File(@"C:\nope\not-here.txt"), new IconSource.Url("nope-scheme")];
        GuideIconCache.Load([.. found, .. missing]);
        await WaitForIconsAsync();
        Check("an exe's icon comes from the file, an appId's from Start, a file's and folder's from the shell, a URL's from its app",
            found.All(s => GuideIconCache.Cached(s) is not null), string.Join(", ", found.Where(s => GuideIconCache.Cached(s) is null).Select(s => s.Key)));
        Check("nothing found means no icon and no error", missing.All(s => GuideIconCache.Known(s) && GuideIconCache.Cached(s) is null));

        // Loaded when the config loads, before any Guide shows.
        var config = App.Config;
        var before = await File.ReadAllTextAsync(config.Path);
        var charmap = new IconSource.Exe("charmap.exe");
        var known = GuideIconCache.Known(charmap);
        await File.WriteAllTextAsync(config.Path, """
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Commands" },
              { "keys": "Hyper+Space C", "title": "Character Map", "action": "charmap" } ],
              "actions": { "charmap": { "type": "application", "exe": "charmap.exe" } } }
            """);
        config.Reload();
        await WaitForIconsAsync();
        Check("icons load when the config loads", !known && GuideIconCache.Cached(charmap) is not null,
            $"known before {known}, looked up {GuideIconCache.Known(charmap)}, resolves to {Actions.AppSwitcher.Resolve("charmap.exe") ?? "nothing"}, errors {config.Errors.Count}");
        await File.WriteAllTextAsync(config.Path, before);
        config.Reload();

        // An icon not loaded yet is asked for, and the Guide doesn't wait for it.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var first = GuideIconCache.Get(new UseApplicationAction("calc.exe", null));
        var took = watch.Elapsed.TotalMilliseconds;
        await WaitForIconsAsync();
        Check("opening the Guide never waits on an icon: it shows next time", first is null && took < 5 && GuideIconCache.Get(new UseApplicationAction("calc.exe", null)) is not null, $"{took:0.0} ms");

        var icon = GuideIconCache.Cached(found[0]);
        var columns = GuideView.Columns([new GuideRow(KeyName.A, "Notepad", false) { Icon = icon }, new GuideRow(KeyName.B, "Menu", true)]);
        var rows = columns[0].Rows;
        Check("when any row in a column has an icon, every row keeps the icon space",
            rows.All(r => r.IconVisibility == Visibility.Visible) && rows[0].ImageVisibility == Visibility.Visible && rows[1].GlyphVisibility == Visibility.Visible);
    }
}
