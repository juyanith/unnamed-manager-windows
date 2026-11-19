using UnnamedManager.Core.Config;

namespace UnnamedManager.Actions;

/// A failed action: what ran, and why it failed, in one sentence (shown in S-016).
public sealed record ActionFailure(string Binding, string Message, DateTimeOffset At);

/// Runs a binding's action: the family plans it, and this carries the plan out.
/// The engine and the Guide know no action type by name; only this file maps plans
/// to what Windows does.
public sealed class ActionRunner
{
    /// How long a started app gets to show a window before it's left to come up on its own.
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(10);

    /// The modifier keys still physically held, from the hook's tracker (S-013).
    public static Func<IReadOnlyCollection<int>> HeldModifierKeys { get; set; } = () => [];

    /// Opens the Help page in front; the app sets it once the window exists.
    public static Action ShowHelp { get; set; } = () => { };

    /// Carries out the window actions (M3). The self-test swaps in a fake window system.
    public static Core.Windows.WindowControl Windows { get; set; } = new(new Win32WindowSystem(), () => App.Config.Current?.Config.Windows ?? new WindowSettings());

    /// The last failure, for the Guide and the log (S-016).
    public ActionFailure? LastFailure { get; private set; }

    public event Action<ActionFailure>? Failed;

    /// Runs on the UI thread; a started app is waited for without blocking it.
    /// `front` is the app that was in front when the leader was pressed: Context Aware
    /// runs its variant for that app (S-014).
    public async Task RunAsync(BindingNode binding, LoadedConfig config, FrontApp? front = null)
    {
        // A list's item (spec v18, v19): focus its window, or swap with it.
        if (binding.Item is { } item)
        {
            var command = new Core.Windows.WindowCommand.Choose(new Core.Windows.WindowRef((nint)item.Id), binding.ItemOf?.Swaps == true);
            if (await PerformAsync(new ActionPlan.Window(command)) is { } why) Fail(binding, why);
            return;
        }
        if (binding.Action is not { } id || config.Config.Resolve(id) is not { } chosen) return;
        // Nothing to run for this app: the engine already gave error feedback.
        if (ContextResolver.Resolve(chosen, front, config.Config.Contexts) is not { } action) return;
        var plan = ActionRegistry.Standard.Family(action.Type)?.Plan(action, PlanContext.Current);
        if (plan is null)
        {
            Fail(binding, $"{action.DisplayName} isn't built yet in Unnamed Manager for Windows.");
            return;
        }
        try
        {
            if (await PerformAsync(plan) is { } failure) Fail(binding, failure);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Fail(binding, $"“{binding.Title}” didn't run: {e.Message}");
        }
    }

    /// Carries out a plan. Returns why it failed, or null.
    public static async Task<string?> PerformAsync(ActionPlan plan)
    {
        switch (plan)
        {
            case ActionPlan.UseApp { App: var app }:
                var window = app.Mode == AppMode.Start ? 0 : AppSwitcher.FindWindow(app);
                if (window != 0)
                {
                    AppSwitcher.Focus(window);
                    return null;
                }
                if (app.Mode == AppMode.Focus) return $"{AppSwitcher.Name(app)} isn't running.";
                var before = AppSwitcher.Windows(app).ToHashSet();
                if (AppSwitcher.Start(app) is { } why) return why;
                // Started apps often can't take the foreground themselves: hand it over once a new window shows.
                var deadline = DateTimeOffset.Now + StartWait;
                while (DateTimeOffset.Now < deadline)
                {
                    await Task.Delay(100);
                    if (AppSwitcher.Windows(app).FirstOrDefault(w => !before.Contains(w)) is var fresh and not 0)
                    {
                        AppSwitcher.Focus(fresh);
                        break;
                    }
                }
                return null;
            case ActionPlan.Window { Command: var command }:
                var failed = Windows.Run(command);
                // Focus Window or Swap Window finding no window plays the alert sound (spec v10).
                if (failed is { Alerts: true } && (App.Config.Current?.Config.UI.ErrorBell ?? true)) UI.Sound.Beep();
                return failed?.Message;
            case ActionPlan.Nothing:
                return null;
            case ActionPlan.ShowHelp:
                // The Help page in front (S-051). Closing it gives focus back to the app before it.
                ShowHelp();
                return null;
            case ActionPlan.Send send:
                if (Core.Windows.Elevation.BlocksSend(Windows.System.Foreground()) is { } blocked) return blocked;
                var strokes = Core.Keyboard.ShortcutEvents.Build(send.Chords, HeldModifierKeys());
                return Keyboard.KeyboardHook.Inject(strokes) ? null : "Windows didn't take the keys.";
            case ActionPlan.Run run:
                return Start(run, hidden: false);
            case ActionPlan.Open open:
                if (!open.IsUrl && !File.Exists(open.Target) && !Directory.Exists(open.Target))
                    return $"Couldn't open {open.Target}: it isn't there.";
                System.Diagnostics.Process.Start(OpenStartInfo(open))?.Dispose();
                return null;
            default:
                return $"Unnamed Manager can't do {plan.GetType().Name} yet.";
        }
    }

    /// Starts the program and returns at once: the app never waits for a command.
    /// `hidden` keeps a console program's window closed (the self-test).
    public static string? Start(ActionPlan.Run run, bool hidden)
    {
        if (!Directory.Exists(run.WorkingDirectory))
            return $"Couldn't run {run.Program}: the folder {run.WorkingDirectory} isn't there.";
        var start = run.StartInfo();
        start.CreateNoWindow = hidden;
        try
        {
            System.Diagnostics.Process.Start(start)?.Dispose();
            return null;
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode is 2 or 3)
        {
            return $"Couldn't run {run.Program}: it isn't installed, or isn't on the PATH.";
        }
    }

    /// Through the shell, as double-clicking it would: files and folders open in their
    /// default app, URLs in the browser.
    public static System.Diagnostics.ProcessStartInfo OpenStartInfo(ActionPlan.Open open) =>
        new(open.Target) { UseShellExecute = true, Verb = "open" };

    private void Fail(BindingNode binding, string message)
    {
        LastFailure = new ActionFailure(binding.Path.ToString(), message, DateTimeOffset.Now);
        AgentLog.Line($"ACTION failed {binding.Path}: {message}");
        AppLog.Write($"Action failed: {binding.Path} ({binding.Title}): {message}");
        Failed?.Invoke(LastFailure);
    }
}
