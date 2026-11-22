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
//   APP_CI=1 with --selftest                   on a CI runner: skip the checks its desktop can't pass (SelfTest.CheckFront)
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
    public static Core.Config.ConfigEditor Editor { get; private set; } = null!;

    /// Edits in the Bindings window wait while the file has errors, so the file you're
    /// fixing is never written over.
    public static bool CanEdit => !Config.State.HasErrors;
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

        // S-054: a Run value left by an install that moved names this exe again.
        if (!agent) Platform.Startup.RepairIfMoved();
        Config = new ConfigStore(AppPaths.ConfigFile);
        Config.Load();
        var ui = DispatcherQueue.GetForCurrentThread();
        Config.Watch(ui);
        // The draft the Bindings window and Settings edit; a valid one is saved at once (S-048).
        Editor = new Core.Config.ConfigEditor(Config.Current?.Config ?? new Core.Config.Config(), Config.Save);
        Config.Changed += () => { if (Config.Current is { } c && !Config.State.HasErrors) Editor.Reload(c.Config); };
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
        if (!Snapshot.Requested && Prefs.Current.Enabled && Config.Current is { } loaded) Keys.Start(_started = loaded);
        // S-060: the Guide's icons load now, so opening it never waits on one.
        if (Config.Current is { } withIcons) UnnamedManager.Guide.GuideIconCache.Prefetch(withIcons);
        UpdateTray();

        MainWindow = new MainWindow();
        Actions.ActionRunner.ShowHelp = () => MainWindow.Open("help");
        if (!agent) ShowWelcomeIfNeeded();
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

    /// The config the keys last started on from the store.
    private static Core.Config.LoadedConfig? _started;

    /// A read that didn't change the config in use (an error, or the same file) leaves
    /// the hook alone.
    private static void OnConfigChanged()
    {
        if (Prefs.Current.Enabled && Config.Current is { } loaded && !ReferenceEquals(loaded, _started) && !Snapshot.Requested)
        {
            _started = loaded;
            Keys.Start(loaded);
            // The sequence ended with the old config. Queued, so the effects of keys
            // pressed just before the swap show first and then go too.
            _ui.TryEnqueue(CloseEndedSequence);
        }
        if (!Snapshot.Requested) MainWindow?.ApplyTheme(ThemeMode);
        if (Config.Current is { } withIcons) UnnamedManager.Guide.GuideIconCache.Prefetch(withIcons);
        UpdateTray();
    }

    /// Turns a key recorder on (S-040). That ends any sequence, so its Guide goes too (S-079):
    /// queued, so the effects of keys pressed just before show first and then go.
    public static void BeginRecording()
    {
        Keys.Recording = true;
        _ui.TryEnqueue(CloseEndedSequence);
    }

    /// Closes what's left of a sequence a new config or the key recorder ended: its Guide, and the delay or
    /// timeout waiting for it. A sequence started since keeps its Guide, and so does a
    /// failure's message, which closes by itself.
    private static void CloseEndedSequence()
    {
        if (Keys.Tracker?.IsActive == true) return;
        Schedule.Reset();
        Arm();
        if (_guide is { View.FailureMessage: null } guide) guide.Hide();
    }

    public static TrayStatus TrayStatusNow => new(
        Prefs.Current.Enabled, Config.CreatedStarter, Config.State.LoadedAt, Config.State.ErrorSummary,
        VersionLine, ThemeMode, Keys.ElevatedApp, Platform.OfficeShortcuts.On);

    /// The config's `theme`, the app's one theme setting (S-035): "system", "light" or "dark".
    /// With "system", every surface follows Windows' app mode as it changes.
    public static string ThemeMode => Config.Current?.Config.UI.Theme.ToLowerInvariant() ?? "system";

    /// Welcome's first step is done once the file was opened from anywhere (S-058).
    public static void OpenConfigFile()
    {
        ConfigFiles.OpenFile(Config.Path);
        NoteConfigOpened();
    }

    public static void NoteConfigOpened()
    {
        if (Prefs.Current.OpenedConfig) return;
        Prefs.Current.OpenedConfig = true;
        Prefs.Current.Save();
    }

    /// The first sequence that runs a binding ends Welcome for good (S-058).
    public static void NoteSequenceRan()
    {
        if (Prefs.Current.RanSequence) return;
        Prefs.Current.RanSequence = true;
        Prefs.Current.Save();
    }

    public static Welcome.WelcomeWindow? Welcome { get; private set; }

    /// Welcome, at start until you've run one sequence. Returns whether it's showing.
    public static bool ShowWelcomeIfNeeded()
    {
        if (!Core.WelcomeStatus.ShowsAtStart(Prefs.Current.RanSequence)) return false;
        if (Welcome is null)
        {
            Welcome = new Welcome.WelcomeWindow();
            Welcome.Closed += (_, _) => Welcome = null;
        }
        Welcome.Activate();
        return true;
    }

    public static TrayCommands TrayCommandsNow => new(SetEnabled, Config.Reload,
        OpenConfigFile, () => ConfigFiles.OpenLocation(Config.Path), ConfigFiles.OpenLogFolder, Shutdown,
        page => MainWindow.Open(page), Platform.OfficeShortcuts.Set);

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
        if (effect is SequenceEffect.Run run)
        {
            _ = Runner.RunAsync(run.Binding, loaded, run.Front);
            NoteSequenceRan();
        }
        // A list opened (spec v18): its items come from the windows now, and the keys
        // pressed meanwhile replay once it's filled.
        if (effect is SequenceEffect.FillList { Menu.List: { } list } && Keys.Tracker is { } tracker)
        {
            var (items, front) = Actions.ActionRunner.Windows.RecentWindows(list);
            UnnamedManager.Guide.GuideIconCache.Load([.. items.Select(i => i.App).Append(front?.App).OfType<string>().Distinct().Select(p => (Core.Config.IconSource)new Core.Config.IconSource.Exe(p))]);
            Keys.Deliver(tracker.Fill(items, front));
        }
        WatchHeld();
    }

    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _heldTimer;

    /// While a held menu waits for its modifiers, or a list is on screen, checks every
    /// 100 ms (spec v18: within 200 ms) for a key-up the hook missed, and for an item's
    /// window that closed.
    private static void WatchHeld()
    {
        var tracker = Keys.Tracker;
        var watching = tracker is not null && (tracker.HoldModifiers != Core.Config.Modifiers.None || tracker.CurrentList is not null);
        if (!watching)
        {
            _heldTimer?.Stop();
            return;
        }
        if (_heldTimer is null)
        {
            _heldTimer = _ui.CreateTimer();
            _heldTimer.Interval = TimeSpan.FromMilliseconds(100);
            _heldTimer.Tick += (_, _) => CheckHeld();
        }
        if (!_heldTimer.IsRunning) _heldTimer.Start();
    }

    /// Whether a key is down now. The self-test swaps it, since its keys are only pretend.
    public static Func<int, bool> IsKeyDown { get; set; } = vk => (UI.Win32.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void CheckHeld()
    {
        if (Keys.Tracker is not { } tracker) return;
        var effects = new List<SequenceEffect>();
        if (tracker.HoldModifiers != Core.Config.Modifiers.None && tracker.Resync(IsKeyDown) is { } chosen)
            effects.Add(chosen);
        foreach (var item in tracker.CurrentList?.Items ?? [])
            if (Actions.ActionRunner.Windows.System.Facts(new Core.Windows.WindowRef((nint)item.Id)) is null && tracker.RemoveItem(item.Id) is { } removed) effects.Add(removed);
        Keys.Deliver(effects);
        WatchHeld();
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
                    Guide.Show(show.Menu, show.Path, loaded, show.Front, show.Highlight);
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
        SequenceEffect.ShowHeld h => $"held {string.Join(" › ", h.Path)} at {h.Highlight}",
        SequenceEffect.FillList f => $"list {string.Join(" › ", f.Path)}",
        SequenceEffect.Run r => $"run {r.Binding.Path} ({r.Binding.Title})",
        SequenceEffect.Unbound u => $"unbound in {string.Join(" › ", u.Path)}",
        SequenceEffect.ShowHome h => $"home {string.Join(" › ", h.Path)}",
        SequenceEffect.Unavailable a => $"unavailable {a.Binding.Path}",
        _ => "cancelled",
    };
}
