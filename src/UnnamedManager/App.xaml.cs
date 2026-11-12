using System.Reflection;
using UnnamedManager.Core;
using UnnamedManager.Core.Keyboard;
using UnnamedManager.Guide;
using UnnamedManager.Keyboard;
using UnnamedManager.Tray;
using UnnamedManager.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace UnnamedManager;

// UnnamedManager
//   UnnamedManager.exe                         the app: a tray icon, no window
//   APP_STORE=<file> UnnamedManager.exe        use another config file (tests, experiments); must be in %TEMP%
//   APP_SNAPSHOT=<dir> UnnamedManager.exe      save a PNG of every page in light and dark, then quit
//   APP_SNAPSHOT_PAGES=settings              limit the snapshot pages
//   UnnamedManager.exe --selftest              run the self-test on a %TEMP% config, exit 0 or 1 (tools/selftest.ps1)
//   APP_ACCENT=<id> UnnamedManager.exe         compare another accent from Theme.All in snapshots (UI/Theme.cs);
//                                              the app itself always follows the Windows accent
//   APP_GRID=1 UnnamedManager.exe              draw the 8 epx grid and the layout probes over the window
// With APP_STORE set, the app never adds its tray icon, and only test runs with the
// same APP_STORE count as another copy (S-010).
public partial class App : Application
{
    public static ConfigStore Config { get; private set; } = null!;
    public static MainWindow MainWindow { get; private set; } = null!;
    public static KeyService Keys { get; private set; } = null!;
    /// The Guide, made the first time it shows: with `showHud` false it never exists (S-036).
    public static GuideWindow Guide => _guide ??= new GuideWindow();
    private static GuideWindow? _guide;

    /// Whether the Guide's window was ever made.
    public static bool GuideCreated => _guide is not null;

    private static Microsoft.UI.Dispatching.DispatcherQueue _ui = null!;
    public static TrayIcon Tray { get; private set; } = null!;
    public static TrayMenu TrayMenu { get; private set; } = null!;
    public static Actions.ActionRunner Runner { get; } = new();

    private static Mutex? _single;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => AgentLog.Line($"CRASH {e.Exception}");
    }

    /// Names this copy, so a second start finds it: per user session, and per test config.
    public static string InstanceKey => AppPaths.IsOverridden
        ? $"UnnamedManager-{StableHash(AppPaths.ConfigFile.ToLowerInvariant()):x8}"
        : "UnnamedManager";

    /// FNV-1a: string.GetHashCode differs in every process, and both copies must agree.
    private static uint StableHash(string text)
    {
        var hash = 2166136261;
        foreach (var c in text) hash = (hash ^ c) * 16777619;
        return hash;
    }

    public static string VersionLine =>
        AppVersion.Line(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Agent modes never read or write the real config.
        var agent = SelfTest.Requested || Snapshot.Requested;
        if (agent && !AppPaths.IsTestStore)
        {
            AgentLog.Line("refused: set APP_STORE to a file in %TEMP%");
            AgentLog.Exit(1);
        }

        // S-010: one copy per user session. A second start asks the first for its tray menu.
        _single = new Mutex(true, $@"Local\{InstanceKey}", out var first);
        if (!first && !Snapshot.Requested)
        {
            var asked = TrayIcon.AskRunningCopy(InstanceKey);
            AgentLog.Line(asked ? "SINGLE another copy runs: asked it for its tray menu" : "SINGLE another copy runs");
            AgentLog.Exit(0);
        }

        // A tray app: it runs with no window until Exit.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        // The accent must be in the resources before the first control loads.
        // Not in the constructor: Application.Current.Resources throws E_UNEXPECTED there.
        Theme.ApplyAccent(Environment.GetEnvironmentVariable("APP_ACCENT") ?? Theme.DefaultId);

        Config = new ConfigStore(AppPaths.ConfigFile);
        Config.Load();
        var ui = DispatcherQueue.GetForCurrentThread();
        Keys = new KeyService(ui);
        Actions.ActionRunner.HeldModifierKeys = () => Keys.Tracker?.HeldModifierKeys ?? [];
        _ui = ui;
        // S-016: a failed action says why in the Guide; the log already has it. With
        // `showHud` false, the log is the only place (S-036).
        Runner.Failed += failure =>
        {
            if ((Keys.Config ?? Config.Current) is { Config.Runtime.ShowHud: true } shown) Guide.ShowFailure(failure.Message, shown);
        };
        Keys.Effect += effect => AgentLog.Line($"SEQUENCE {Describe(effect)}");
        Keys.Effect += OnSequenceEffect;

        Tray = new TrayIcon(InstanceKey, addToShell: !AppPaths.IsOverridden);
        TrayMenu = new TrayMenu();
        Tray.Invoked += (x, y, keyboard) => OpenTrayMenu(x, y, keyboard);
        // S-038: a left click opens the Bindings window, a right click the menu.
        Tray.Selected += () => MainWindow.Open("bindings");
        Tray.ShowMenuRequested += () => OpenTrayMenuAtCursor(keyboard: true);
        Tray.Add();
        Config.Changed += OnConfigChanged;
        // Snapshots need no keys.
        if (!Snapshot.Requested && Prefs.Current.Enabled && Config.Current is { } loaded) Keys.Start(loaded);
        UpdateTray();

        MainWindow = new MainWindow();
        if (agent)
        {
            MainWindow.Activate();
            // Agent modes run once the window has drawn its first frame.
            MainWindow.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
            {
                await SelfTest.RunIfRequestedAsync(MainWindow);
                await Snapshot.RunIfRequestedAsync(MainWindow);
            });
        }
    }

    /// The tray icon's state and tooltip: off, a config error, or ready.
    private static void UpdateTray() =>
        Tray.Show(!Prefs.Current.Enabled ? TrayState.Off : Config.State.HasErrors ? TrayState.Warning : TrayState.Ready);

    private static void OnConfigChanged()
    {
        if (Prefs.Current.Enabled && Config.Current is { } loaded && !Snapshot.Requested) Keys.Start(loaded);
        if (!Snapshot.Requested) MainWindow?.ApplyTheme(ThemeMode);
        UpdateTray();
    }

    public static TrayStatus TrayStatusNow => new(
        Prefs.Current.Enabled, Config.CreatedStarter, Config.State.LoadedAt, Config.State.ErrorSummary,
        VersionLine, ThemeMode);

    /// The config's `theme`, the app's one theme setting (S-035): "system", "light" or "dark".
    /// With "system", every surface follows Windows' app mode as it changes.
    public static string ThemeMode => Config.Current?.Config.UI.Theme.ToLowerInvariant() ?? "system";

    public static TrayCommands TrayCommandsNow => new(SetEnabled, Config.Reload,
        () => ConfigFiles.OpenFile(Config.Path), () => ConfigFiles.OpenLocation(Config.Path), ConfigFiles.OpenLogFolder, Shutdown,
        page => MainWindow.Open(page));

    public static void OpenTrayMenu(int x, int y, bool keyboard) => TrayMenu.Open(x, y, TrayStatusNow, TrayCommandsNow, keyboard);

    /// For a second start, which has no click to anchor to: the menu opens at the pointer.
    public static void OpenTrayMenuAtCursor(bool keyboard)
    {
        Win32.GetCursorPos(out var point);
        OpenTrayMenu(point.X, point.Y, keyboard);
    }

    /// Enabled in the tray menu: off removes the hook, on installs it again (S-009).
    public static void SetEnabled(bool enabled)
    {
        Prefs.Current.Enabled = enabled;
        Prefs.Current.Save();
        if (!enabled) { Keys.Stop(); Schedule.Reset(); Arm(); _guide?.Hide(); }
        else if (Config.Current is { } loaded) Keys.Start(loaded);
        AgentLog.Line(enabled ? "ENABLED on" : "ENABLED off");
        UpdateTray();
    }

    /// Exit: the hook and the tray icon go first, then the app (S-009).
    public static void Shutdown() => Shutdown(exitProcess: true);

    public static void Shutdown(bool exitProcess)
    {
        Keys.Stop();
        Tray.Remove();
        AgentLog.Line("EXIT hook and tray icon removed");
        if (!exitProcess) return;
        Tray.Dispose();
        Current.Exit();
        Environment.Exit(0);
    }

    /// When the Guide shows and hides; Core decides (`GuideSchedule`), this only carries it out.
    public static GuideSchedule Schedule { get; private set; } = new(new(), new());

    /// The Guide follows the sequence through the schedule; a picked binding runs.
    private static void OnSequenceEffect(SequenceEffect effect)
    {
        if ((Keys.Config ?? Config.Current) is not { } loaded) return;
        if (Schedule.Runtime != loaded.Config.Runtime || Schedule.UI != loaded.Config.UI)
            Schedule = new GuideSchedule(loaded.Config.Runtime, loaded.Config.UI);
        Apply(Schedule.Handle(effect, Environment.TickCount64), loaded);
        if (effect is SequenceEffect.Run run) _ = Runner.RunAsync(run.Binding, loaded, run.Front);
    }

    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _scheduleTimer;

    /// One timer, armed for the schedule's next deadline (the delay, the timeout).
    private static void Arm()
    {
        _scheduleTimer ??= CreateScheduleTimer();
        _scheduleTimer.Stop();
        if (Schedule.NextDeadline is not { } at) return;
        _scheduleTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, at - Environment.TickCount64));
        _scheduleTimer.Start();
    }

    private static Microsoft.UI.Dispatching.DispatcherQueueTimer CreateScheduleTimer()
    {
        var timer = _ui.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => { if ((Keys.Config ?? Config.Current) is { } loaded) Apply(Schedule.Tick(Environment.TickCount64), loaded); };
        return timer;
    }

    private static void Apply(IReadOnlyList<GuideCommand> commands, Core.Config.LoadedConfig loaded)
    {
        foreach (var command in commands)
        {
            switch (command)
            {
                case GuideCommand.Show show:
                    Guide.Show(show.Menu, show.Path, loaded, show.Front);
                    break;
                case GuideCommand.ShowHome home:
                    Guide.ShowHome(home.Home, home.Path, loaded, home.Front);
                    break;
                case GuideCommand.Hide hide:
                    if (hide.Cancelled && Guide.IsShown) Guide.View.Announce("Cancelled");
                    Guide.Hide();
                    break;
                case GuideCommand.Flash:
                    Guide.Flash();
                    break;
                case GuideCommand.Bell:
                    Sound.Beep();
                    break;
                case GuideCommand.EndSequence:
                    Keys.Tracker?.Reset();
                    AgentLog.Line("SEQUENCE timed out");
                    break;
            }
        }
        Arm();
    }

    private static string Describe(SequenceEffect effect) => effect switch
    {
        SequenceEffect.ShowMenu m => $"menu {string.Join(" › ", m.Path)}",
        SequenceEffect.Run r => $"run {r.Binding.Path} ({r.Binding.Title})",
        SequenceEffect.Unbound u => $"unbound in {string.Join(" › ", u.Path)}",
        SequenceEffect.ShowHome h => $"home {string.Join(" › ", h.Path)}",
        SequenceEffect.Unavailable a => $"unavailable {a.Binding.Path}",
        _ => "cancelled",
    };
}
