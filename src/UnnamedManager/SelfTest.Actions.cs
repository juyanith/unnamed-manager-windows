using UnnamedManager.Core.Config;
using UnnamedManager.Keyboard;
using UnnamedManager.UI;
using UnnamedManager.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace UnnamedManager;

/// The self-test's Actions checks; SelfTest.cs runs them in order.
public static partial class SelfTest
{
    /// S-006 against the app's own window, so nothing outside the app is started or moved.
    private static async Task CheckUseApplicationAsync(MainWindow window)
    {
        var self = Environment.ProcessPath!;
        var byName = new UseApplicationAction(Path.GetFileName(self), null);
        var byPath = new UseApplicationAction(self, null);
        Check("a running app is found by its exe file name", Actions.AppSwitcher.FindWindow(byName) == window.Handle());
        Check("a running app is found by its full path", Actions.AppSwitcher.FindWindow(byPath) == window.Handle(),
            $"{self} vs {Actions.AppSwitcher.ProcessPath((uint)Environment.ProcessId)}");
        Check("a path to another copy doesn't match", Actions.AppSwitcher.FindWindow(new UseApplicationAction(@"C:\Elsewhere\UnnamedManager.exe", null)) == 0);
        Check("the Guide never counts as the app's window", Actions.AppSwitcher.Windows(byName).All(w => w != App.Guide.Handle()));

        window.AppWindow.Hide();
        window.AppWindow.Show();
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Minimize();
        await Task.Delay(400);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var failure = await Actions.ActionRunner.PerformAsync(new ActionPlan.UseApp(byName));
        watch.Stop();
        await Task.Delay(300);
        CheckFront("a minimized app is restored and brought to the front", failure is null
            && window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: not Microsoft.UI.Windowing.OverlappedPresenterState.Minimized },
            Win32.GetForegroundWindow() == window.Handle(), $"failure {failure}, foreground {Win32.GetForegroundWindow()}");
        Check("switching to a running app takes under 100 ms", watch.ElapsedMilliseconds < 100, $"{watch.ElapsedMilliseconds} ms");

        // S-080: its only window on another desktop. Pretend, so your desktop never switches.
        var hwnd = window.Handle();
        var savedPresence = Actions.AppSwitcher.ReadPresence;
        Actions.AppSwitcher.ReadPresence = h => h == hwnd ? Core.Windows.WindowPresence.Elsewhere : Core.Windows.WindowPresence.Nowhere;
        try
        {
            Check("an app whose only window is on another desktop is found, and counts as there before a start",
                Actions.AppSwitcher.FindWindow(byPath) == hwnd && Actions.AppSwitcher.Windows(byPath).Contains(hwnd));
            foreach (var mode in new[] { AppMode.FocusOrStart, AppMode.Focus })
            {
                var began = System.Diagnostics.Stopwatch.StartNew();
                var why = await Actions.ActionRunner.PerformAsync(new ActionPlan.UseApp(new UseApplicationAction(self, null, mode)));
                // Starting one would wait up to 10 seconds for its window.
                Check($"{mode} goes to it, and neither starts it nor says it isn't running",
                    why?.Contains("running") != true && began.ElapsedMilliseconds < 3000, $"{why} {began.ElapsedMilliseconds} ms");
            }
        }
        finally { Actions.AppSwitcher.ReadPresence = savedPresence; }

        Check("an exe file name is found through App Paths and PATH", Actions.AppSwitcher.Resolve("notepad.exe") is { } notepad && File.Exists(notepad));
        Check("a full path is used as is", Actions.AppSwitcher.Resolve(self) == self);
        Check("an exe that isn't installed isn't found", Actions.AppSwitcher.Resolve("unnamed-manager-missing.exe") is null);
        Check("an appId in Start is found", Actions.AppSwitcher.IsInstalled("Microsoft.Windows.Explorer"));
        Check("an appId that isn't installed isn't found", !Actions.AppSwitcher.IsInstalled("Contoso.Missing_0000000000000!App"));
        // S-017: Focus never starts an app. charmap.exe is in every Windows and nobody has it open.
        var focusOnly = await Actions.ActionRunner.PerformAsync(new ActionPlan.UseApp(new UseApplicationAction("charmap.exe", null, AppMode.Focus)));
        Check("Focus on an app that isn't running fails, and starts nothing",
            focusOnly == "Character Map isn't running." && System.Diagnostics.Process.GetProcessesByName("charmap").Length == 0, focusOnly ?? "none");

        var missing = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "Missing", "action": "m" },
              { "keys": "Ctrl+Alt+J", "title": "Missing store app", "action": "s" } ],
              "actions": { "m": { "type": "application", "exe": "unnamed-manager-missing.exe" },
                           "s": { "type": "application", "appId": "Contoso.Missing_0000000000000!App" } } }
            """).Loaded!;
        foreach (var binding in missing.Leaders)
        {
            var runner = new Actions.ActionRunner();
            await runner.RunAsync(binding, missing);
            Check($"an app that isn't installed is recorded as a failure ({binding.Title})",
                runner.LastFailure is { } f && f.Binding == binding.Path.ToString() && f.Message.StartsWith("Can't find"), runner.LastFailure?.Message ?? "none");
        }
    }

    /// S-014: the watcher follows the window in front, and the runner picks the variant for it.
    private static async Task CheckContextAwareAsync(MainWindow window)
    {
        Win32.ForceForeground(window.Handle());
        await Task.Delay(300);
        var self = Environment.ProcessPath!;
        var front = Actions.AppSwitcher.FrontAppOf(window.Handle());
        Check("the app in front is known by its exe", front?.ExePath is { } path
            && string.Equals(Actions.AppSwitcher.RealPath(path), Actions.AppSwitcher.RealPath(self), StringComparison.OrdinalIgnoreCase), front?.ExePath ?? "none");

        var config = ConfigLoader.Load($$"""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "Here", "action": "here" } ],
              "actions": { "here": { "type": "contextual",
                "variants": { "this-app": { "type": "open", "target": "C:\\unnamed-manager-missing\\for-this-app" } } } },
              "contexts": [ { "id": "this-app", "apps": [{{System.Text.Json.JsonSerializer.Serialize(Path.GetFileName(self))}}] } ] }
            """).Loaded!;
        var runner = new Actions.ActionRunner();
        await runner.RunAsync(config.Leaders[0], config, front);
        Check("Context Aware runs the variant for the app in front", runner.LastFailure?.Message.Contains("for-this-app") == true, runner.LastFailure?.Message ?? "none");
        var elsewhere = new Actions.ActionRunner();
        await elsewhere.RunAsync(config.Leaders[0], config, new FrontApp(@"C:\Windows\explorer.exe", null, "File Explorer"));
        Check("with no variant and no fallback, nothing runs", elsewhere.LastFailure is null);
        Check("the Guide dims a row with nothing to run, and says why",
            new Guide.GuideRow(KeyName.L, "Address bar", false, "File Explorer") is { IsAvailable: false, UnavailableNote: "not available in File Explorer" } row
            && row.Spoken.EndsWith(", not available in File Explorer"));
    }

    /// S-016: a failed action shows in the Guide for 2 seconds and is written to today's log.
    private static async Task CheckFailureAsync()
    {
        var config = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "Missing app", "action": "m" } ],
              "actions": { "m": { "type": "application", "exe": "unnamed-manager-missing.exe" } } }
            """).Loaded!;
        await App.Runner.RunAsync(config.Leaders[0], config);
        await Task.Delay(300);
        var message = App.Runner.LastFailure?.Message ?? "";
        Check("the Guide shows why an action failed, on a warning line", App.Guide.IsShown && App.Guide.View.FailureMessage == message, App.Guide.View.FailureMessage ?? "none");
        var log = File.Exists(AppLog.TodayFile) ? await File.ReadAllTextAsync(AppLog.TodayFile) : "";
        Check("the failure is written to today's log file", log.Contains("Ctrl+Alt+K (Missing app): " + message), AppLog.TodayFile);
        await Task.Delay(Motion.ErrorMessageHold);
        Check("the Guide closes 2 seconds later", !App.Guide.IsShown);
        Check("the tray menu has Open log folder", MenuTexts(App.TrayStatusNow).Contains("Open log folder"));
    }

    /// S-011, without opening anything on screen: how each target goes to the shell, and a missing one.
    private static async Task CheckOpenAsync()
    {
        var home = PlanContext.Current;
        var folder = (ActionPlan.Open)ActionRegistry.Standard.Family("open")!.Plan(new OpenAction("~"), home)!;
        Check("~ opens the home folder", folder.Target == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        foreach (var target in new[] { folder.Target, Environment.ProcessPath!, "https://example.com" })
        {
            var start = Actions.ActionRunner.OpenStartInfo(new ActionPlan.Open(target));
            Check($"{(target.StartsWith("https") ? "a URL" : Directory.Exists(target) ? "a folder" : "a file")} opens through the shell",
                start.UseShellExecute && start.FileName == target && start.Verb == "open");
        }
        var missing = await Actions.ActionRunner.PerformAsync(new ActionPlan.Open(@"C:\unnamed-manager-missing\notes.txt"));
        Check("a file that isn't there is a failure", missing == @"Couldn't open C:\unnamed-manager-missing\notes.txt: it isn't there.", missing ?? "none");
    }

    /// S-012 with real processes, their console windows kept closed.
    private static async Task CheckRunCommandAsync()
    {
        var output = Path.Combine(Path.GetTempPath(), $"um-exec-{Environment.ProcessId}.txt");
        File.Delete(output);
        var family = ActionRegistry.Standard.Family("exec")!;
        var echo = (ActionPlan.Run)family.Plan(new RunCommandAction(
            ["cmd.exe", "/d", "/c", "echo %CD%;%UM_TEST%>%UM_OUT%"], null,
            [KeyValuePair.Create("UM_TEST", "from env"), KeyValuePair.Create("UM_OUT", output)]), PlanContext.Current)!;
        Check("a command starts", Actions.ActionRunner.Start(echo, hidden: true) is null);
        for (var i = 0; i < 40 && !File.Exists(output); i++) await Task.Delay(50);
        await Task.Delay(100);
        var written = File.Exists(output) ? (await File.ReadAllTextAsync(output)).Trim() : "";
        Check("cwd defaults to the home folder, and env is added to the inherited environment",
            written == $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)};from env", written);
        File.Delete(output);

        // No stopwatch: starting a process can take over a second on a busy runner. The
        // command looks for a signal for 30 s and answers when it sees it, and the signal
        // is only written once Start returns. Had Start waited, the command would be over
        // by then and there'd be no answer. Relative names, so cmd needs no quotes.
        var temp = Path.GetTempPath();
        string signal = $"um-signal-{Environment.ProcessId}.txt", answer = $"um-answer-{Environment.ProcessId}.txt";
        string signalFile = Path.Combine(temp, signal), answerFile = Path.Combine(temp, answer);
        File.Delete(signalFile);
        File.Delete(answerFile);
        var waiting = (ActionPlan.Run)family.Plan(new RunCommandAction(
            ["cmd.exe", "/d", "/c", $"for /l %i in (1,1,30) do @(if exist {signal} (echo answered>{answer}& exit) else ping -n 2 127.0.0.1 >nul)"],
            temp, []), PlanContext.Current)!;
        var started = Actions.ActionRunner.Start(waiting, hidden: true);
        await File.WriteAllTextAsync(signalFile, "");
        for (var i = 0; i < 300 && !File.Exists(answerFile); i++) await Task.Delay(50);
        Check("the app doesn't wait for a command that starts", started is null && File.Exists(answerFile),
            started ?? "the command didn't answer a signal written after Start returned");
        await Task.Delay(100);
        File.Delete(signalFile);
        File.Delete(answerFile);

        var missing = Actions.ActionRunner.Start(new ActionPlan.Run("unnamed-manager-missing", [], PlanContext.Current.Home, []), hidden: true);
        Check("a program that can't be found is a failure",
            missing == "Couldn't run unnamed-manager-missing: it isn't installed, or isn't on the PATH.", missing ?? "none");
    }
}
