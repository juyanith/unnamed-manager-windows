using System.Runtime.InteropServices;
using UnnamedManager.Actions;
using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;

namespace UnnamedManager;

/// Checks both apps run on the code they share: this file is linked into the WPF app's
/// self-test too, so it uses no WinUI or WPF.
public static partial class SelfTest
{
    /// S-082 against windows of the test's own: an owner, a dialog, a popup and a tool window.
    private static async Task CheckDialogsAsync()
    {
        var owner = Create("Unnamed Manager test window", 0, TestWindowStyle.Captioned);
        var popup = Create("", owner, TestWindowStyle.Popup);
        var tool = Create("Palette", owner, TestWindowStyle.Tool);
        var dialog = Create("Unnamed Manager test dialog", owner, TestWindowStyle.Captioned);
        var saved = AppSwitcher.ReadPresence;
        try
        {
            await Task.Delay(200);
            Check("a captioned owned window is a dialog, and its owner a standard window",
                AppSwitcher.KindOf(dialog) == WindowKind.Dialog && AppSwitcher.KindOf(owner) == WindowKind.Standard);
            Check("an owned popup with no caption and a tool window are never candidates",
                AppSwitcher.KindOf(popup) == WindowKind.None && AppSwitcher.KindOf(tool) == WindowKind.None
                && !AppSwitcher.IsCandidate(popup) && !AppSwitcher.IsCandidate(tool));
            Check("a window its dialog hasn't disabled isn't blocked", !AppSwitcher.IsBlocked(owner));

            EnableWindow(owner, false);
            await Task.Delay(100);
            Check("a window a modal dialog disabled stands aside for it", AppSwitcher.IsBlocked(owner));

            // Only the test's windows: the app's own others don't count for this check.
            nint[] mine = [owner, popup, tool, dialog];
            AppSwitcher.ReadPresence = h => mine.Contains(h) ? WindowPresence.Here : WindowPresence.Nowhere;
            var self = new UseApplicationAction(Environment.ProcessPath!, null, AppMode.Focus);
            Check("Use Application takes the dialog, not the disabled window behind it", AppSwitcher.FindWindow(self) == dialog);
            var why = await ActionRunner.PerformAsync(new ActionPlan.UseApp(self));
            await Task.Delay(200);
            var front = UI.Win32.GetForegroundWindow() == dialog;
            if (!front && Environment.GetEnvironmentVariable("APP_CI") == "1")
                AgentLog.Line("SKIP Use Application gives the dialog focus: not in front, as on a CI desktop");
            else
                Check("Use Application gives the dialog focus", why is null && front, $"{why}");
        }
        finally
        {
            AppSwitcher.ReadPresence = saved;
            EnableWindow(owner, true);
            foreach (var window in new[] { dialog, tool, popup, owner }) DestroyWindow(window);
        }
    }

    /// S-084: focus that never arrives is said and logged; focus up to half a second late isn't a failure.
    private static async Task CheckFocusArrivalAsync()
    {
        var window = Create("Unnamed Manager test window", 0, TestWindowStyle.Captioned);
        var savedPresence = AppSwitcher.ReadPresence;
        var savedFocus = AppSwitcher.ReadFocus;
        AppSwitcher.ReadPresence = h => h == window ? WindowPresence.Here : WindowPresence.Nowhere;
        try
        {
            var config = ConfigLoader.Load($$"""
                { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "Self", "action": "self" } ],
                  "actions": { "self": { "type": "application", "exe": "{{Path.GetFileName(Environment.ProcessPath)}}", "mode": "focus" } } }
                """).Loaded!;
            AppSwitcher.ReadFocus = _ => false;
            var runner = new ActionRunner();
            await runner.RunAsync(config.Leaders[0], config);
            var said = runner.LastFailure?.Message ?? "";
            var log = File.Exists(AppLog.TodayFile) ? await File.ReadAllTextAsync(AppLog.TodayFile) : "";
            Check("Use Application says when a window couldn't take focus, and logs it",
                said.StartsWith("Couldn't focus ") && log.Contains(said), said);

            var began = Environment.TickCount64;
            AppSwitcher.ReadFocus = _ => Environment.TickCount64 - began >= 450;
            var late = new ActionRunner();
            await late.RunAsync(config.Leaders[0], config);
            Check("focus up to half a second late isn't a failure", late.LastFailure is null, late.LastFailure?.Message ?? "");
        }
        finally
        {
            AppSwitcher.ReadPresence = savedPresence;
            AppSwitcher.ReadFocus = savedFocus;
            DestroyWindow(window);
        }
    }

    /// S-093: an entry's own icon in the Guide and Home, loaded with the config.
    private static async Task CheckEntryIconsAsync()
    {
        var original = await File.ReadAllTextAsync(App.Config.Path);
        var glyph = new IconSource.Glyph("\uE80F");
        var known = Guide.GuideIconCache.Known(glyph);
        await File.WriteAllTextAsync(App.Config.Path, """
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Main" },
              { "keys": "Hyper+Space A", "title": "Glyph", "icon": "U+E80F", "action": "notepad" },
              { "keys": "Hyper+Space B", "title": "No such glyph", "icon": "U+0E01", "action": "notepad" },
              { "keys": "Hyper+Space C", "title": "Gone", "icon": "C:\\unnamed-manager-missing\\gone.ico", "action": "notepad" },
              { "keys": "Hyper+Space D", "title": "File", "icon": "C:\\Windows\\win.ini", "action": "noop" } ],
              "actions": { "notepad": { "type": "application", "exe": "notepad.exe" } } }
            """);
        App.Config.Reload();
        for (var i = 0; i < 100 && !Guide.GuideIconCache.Idle; i++) await Task.Delay(50);
        await Task.Delay(50);
        Check("an entry's icon loads with the config, before any Guide shows",
            !known && Guide.GuideIconCache.Found(glyph), $"known before {known}");

        var loaded = App.Config.Current!;
        App.Guide.Show(loaded.Leaders[0], ["Main"], loaded);
        await Task.Delay(200);
        var rows = App.Guide.View.Rows.ToDictionary(r => r.Title);
        Check("an entry's own glyph wins over its app's icon", rows["Glyph"].Glyph == "\uE80F" && rows["Glyph"].Icon is null);
        Check("a code point the font doesn't have, or a file that's gone, shows no icon and no error",
            rows["No such glyph"] is { Glyph: "", Icon: null } && rows["Gone"] is { Glyph: "", Icon: null } && App.Config.Errors.Count == 0);
        Check("a file's icon shows", rows["File"].Icon is not null);
        App.Guide.Hide();
        await File.WriteAllTextAsync(App.Config.Path, original);
        App.Config.Reload();
        await Task.Delay(200);
    }

    private enum TestWindowStyle { Captioned, Popup, Tool }

    private static nint Create(string title, nint owner, TestWindowStyle style)
    {
        const uint popup = 0x80000000, visible = 0x10000000, caption = 0x00C00000, system = 0x00080000;
        var bits = popup | visible | (style == TestWindowStyle.Popup ? 0 : caption | system);
        var extended = style == TestWindowStyle.Tool ? 0x80u : 0;
        return CreateWindowExW(extended, "STATIC", title, bits, 200, 200, 360, 220, owner, 0, 0, 0);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateWindowExW(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint owner, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EnableWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool enable);
}
