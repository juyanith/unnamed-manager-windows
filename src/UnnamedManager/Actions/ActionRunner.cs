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

    /// The last failure, for the Guide and the log (S-016).
    public ActionFailure? LastFailure { get; private set; }

    public event Action<ActionFailure>? Failed;

    /// Runs on the UI thread; a started app is waited for without blocking it.
    public async Task RunAsync(BindingNode binding, LoadedConfig config)
    {
        if (binding.Action is not { } id || config.Config.Action(id) is not { } action) return;
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
                if (app.Mode == AppMode.Focus) return null;
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
            case ActionPlan.Send send:
                var strokes = Core.Keyboard.ShortcutEvents.Build(send.Chords, HeldModifierKeys());
                return Keyboard.KeyboardHook.Inject(strokes) ? null : "Windows didn't take the keys: the app in front may be running as administrator.";
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
        Failed?.Invoke(LastFailure);
    }
}
