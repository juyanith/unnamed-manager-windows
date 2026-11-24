using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using UnnamedManager.Core;
using UnnamedManager.Core.Keyboard;
using UnnamedManager.Guide;
using UnnamedManager.Keyboard;
using UnnamedManager.Tray;
using UnnamedManager.UI;

namespace UnnamedManager;

// UnnamedManager.Wpf
//   UnnamedManager.Wpf.exe                     the app: a tray icon, no window
//   APP_STORE=<file> UnnamedManager.Wpf.exe    use another config file (tests, experiments); must be in %TEMP%
//   UnnamedManager.Wpf.exe --selftest          the behavior checks on a %TEMP% config: exit 0 or 1 (SelfTest.cs)
//   APP_SNAPSHOT=<dir> UnnamedManager.Wpf.exe  save each page in light and dark, then quit (Snapshot.cs)
//   UnnamedManager.Wpf.exe --check             start, load the config, install the hook, show the Guide
//                                              once, then quit: exit 0 when it all worked (needs APP_STORE)
//
// The WPF port of the WinUI app's App.xaml.cs: the same config, keys, Guide and tray, with
// no Bindings window, Welcome or Settings. It counts as the same app: only
// one of the two runs at a time, and a second start asks the first for its tray menu.
public partial class App : Application
{
    public static ConfigStore Config { get; private set; } = null!;
    public static KeyService Keys { get; private set; } = null!;

    /// The Guide, made the first time it shows: with `showHud` false it never exists (S-036).
    public static GuideWindow Guide => _guide ??= new GuideWindow();
    private static GuideWindow? _guide;

    public static TrayIcon Tray { get; private set; } = null!;
    public static TrayMenu TrayMenu { get; private set; } = null!;
    public static Actions.ActionRunner Runner { get; } = new();

    private static Mutex? _single;
    private static Dispatcher _ui = null!;

    /// Where Help goes: the README, with the config format and every action.
    public const string HelpUrl = "https://github.com/juyanith/unnamed-manager-windows#readme";

    /// Names this copy, so a second start finds it: per user session, and per test config.
    /// The same names as the WinUI app, so the two never run at once.
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
        AppVersion.Line(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) + " (WPF)";

    private static bool CheckRequested => Environment.GetCommandLineArgs().Contains("--check");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => AgentLog.Line($"CRASH {args.Exception}");
        _ui = Dispatcher;

        var agent = CheckRequested || SelfTest.Requested || Snapshot.Requested;
        if (agent && !AppPaths.IsTestStore)
        {
            AgentLog.Line("refused: set APP_STORE to a file in %TEMP%");
            AgentLog.Exit(1);
        }

        // S-010: one copy per user session. A second start asks the first for its tray menu.
        _single = new Mutex(true, $@"Local\{InstanceKey}", out var first);
        if (!first)
        {
            var asked = TrayIcon.AskRunningCopy(InstanceKey);
            AgentLog.Line(asked ? "SINGLE another copy runs: asked it for its tray menu" : "SINGLE another copy runs");
            AgentLog.Exit(0);
        }

        // S-054: a Run value left by an install that moved names this exe again.
        if (!agent) Platform.Startup.RepairIfMoved();
        Config = new ConfigStore(AppPaths.ConfigFile);
        Config.Load();
        ApplyTheme(Config.Current?.Config.UI.Theme ?? "system");
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()!;
        Config.Watch(queue);
        Keys = new KeyService(queue);
        Actions.ActionRunner.HeldModifierKeys = () => Keys.Tracker?.HeldModifierKeys ?? [];
        Actions.ActionRunner.ShowHelp = OpenHelp;
        // S-016: a failed action says why in the Guide; the log already has it.
        Runner.Failed += failure =>
        {
            if ((Keys.Config ?? Config.Current) is { Config.Runtime.ShowHud: true } shown) Guide.ShowFailure(failure.Message, shown);
        };
        Keys.Effect += effect => AgentLog.Line($"SEQUENCE {Describe(effect)}");
        Keys.Effect += OnSequenceEffect;

        Tray = new TrayIcon(InstanceKey, addToShell: !AppPaths.IsOverridden);
        TrayMenu = new TrayMenu();
        Tray.Invoked += (x, y, keyboard) => OpenTrayMenu(x, y, keyboard);
        // No Bindings window yet: a left click opens the menu too.
        Tray.Selected += () => OpenTrayMenuAtCursor(keyboard: false);
        Tray.ShowMenuRequested += () => OpenTrayMenuAtCursor(keyboard: true);
        Tray.Add();
        Config.Changed += OnConfigChanged;
        if (Prefs.Current.Enabled && Config.Current is { } loaded) Keys.Start(_started = loaded);
        // S-060: the Guide's icons load now, so opening it never waits on one.
        if (Config.Current is { } withIcons) GuideIconCache.Prefetch(withIcons);
        UpdateTray();

        if (SelfTest.Requested) Dispatcher.BeginInvoke(() => { _ = SelfTest.RunAsync(); }, DispatcherPriority.ApplicationIdle);
        else if (Snapshot.Requested) Dispatcher.BeginInvoke(() => { _ = Snapshot.RunAsync(); }, DispatcherPriority.ApplicationIdle);
        else if (CheckRequested) Dispatcher.BeginInvoke(RunCheck, DispatcherPriority.ApplicationIdle);
    }

    /// --check: what can break when the app starts on a new PC, in one run.
    private static void RunCheck()
    {
        var ok = true;
        void Check(bool passed, string what)
        {
            AgentLog.Line($"{(passed ? "PASS" : "FAIL")} {what}");
            ok &= passed;
        }
        Check(Config.Current is not null, "the config loads");
        Check(Keys.IsCapturing, "the keyboard hook is installed");
        if (Config.Current is { } loaded && loaded.Leaders.FirstOrDefault(l => l.IsMenu) is { } leader)
        {
            Guide.Show(leader, [leader.Title], loaded);
            Check(Guide.IsShown, "the Guide shows");
            Check(Guide.View.ActualWidth > 0, "the Guide has its rows laid out");
            Check(Current.TryFindResource("TextFillColorPrimaryBrush") is not null, "the Fluent theme's brushes are there");
            foreach (var theme in new[] { "light", "dark" })
            {
                ApplyTheme(theme);
                Guide.Show(leader, [leader.Title], loaded with { Config = loaded.Config with { UI = loaded.Config.UI with { Theme = theme } } });
                AgentLog.Line($"SNAPSHOT {Guide.SavePng(Path.Combine(Path.GetDirectoryName(AppPaths.ConfigFile)!, $"guide-{theme}.png"))}");
            }
            ApplyTheme(loaded.Config.UI.Theme);
            Guide.Hide();
        }
        TrayMenu.Open(0, 0, TrayStatusNow, TrayCommandsNow, keyboard: false);
        Check(TrayMenu.IsOpen, "the tray menu opens");
        TrayMenu.Close();
        AgentLog.Line(ok ? "CHECK passed" : "CHECK failed");
        Shutdown(exitProcess: false);
        AgentLog.Exit(ok ? 0 : 1);
    }

    /// The config's `theme`: "system", "light" or "dark". WPF's Fluent theme follows it,
    /// and with "system" it follows Windows' app mode as it changes.
    public static void ApplyTheme(string theme)
    {
#pragma warning disable WPF0001 // ThemeMode is marked for evaluation in .NET 9 and 10.
        var mode = theme.ToLowerInvariant() switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        if (Current.ThemeMode != mode) Current.ThemeMode = mode;
#pragma warning restore WPF0001
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
        if (Prefs.Current.Enabled && Config.Current is { } loaded && !ReferenceEquals(loaded, _started))
        {
            _started = loaded;
            Keys.Start(loaded);
            // The sequence ended with the old config. Queued, so the effects of keys
            // pressed just before the swap show first and then go too.
            _ui.BeginInvoke(CloseEndedSequence);
        }
        if (Config.Current is { } withTheme) ApplyTheme(withTheme.Config.UI.Theme);
        if (Config.Current is { } withIcons) GuideIconCache.Prefetch(withIcons);
        UpdateTray();
    }

    /// Closes what's left of a sequence a new config ended: its Guide, and the delay or
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
        VersionLine, Platform.Startup.State == StartupState.On, Keys.ElevatedApp, Platform.OfficeShortcuts.On);

    public static TrayCommands TrayCommandsNow => new(SetEnabled, Config.Reload,
        OpenConfigFile, () => ConfigFiles.OpenLocation(Config.Path), ConfigFiles.OpenLogFolder, OpenHelp,
        Platform.Startup.Set, () => Shutdown(exitProcess: true), Platform.OfficeShortcuts.Set);

    public static void OpenConfigFile() => ConfigFiles.OpenFile(Config.Path);

    public static void OpenHelp() => Process.Start(new ProcessStartInfo(HelpUrl) { UseShellExecute = true })?.Dispose();

    public static void OpenTrayMenu(int x, int y, bool keyboard) => TrayMenu.Open(x, y, TrayStatusNow, TrayCommandsNow, keyboard);

    /// For a second start, which has no click to anchor to: the menu opens at the pointer.
    public static void OpenTrayMenuAtCursor(bool keyboard)
    {
        Win32.GetCursorPos(out var point);
        OpenTrayMenu(point.X, point.Y, keyboard);
    }

    /// The first sequence that runs a binding ends Welcome for good, in the WinUI app too (S-058, S-110).
    private static void NoteSequenceRan()
    {
        if (Prefs.Current.RanSequence) return;
        Prefs.Current.RanSequence = true;
        Prefs.Current.Save();
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
    public static void Shutdown(bool exitProcess)
    {
        Keys.Stop();
        Tray.Remove();
        AgentLog.Line("EXIT hook and tray icon removed");
        if (!exitProcess) return;
        Tray.Dispose();
        Current.Shutdown();
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
            GuideIconCache.Load([.. items.Select(i => i.App).Append(front?.App).OfType<string>().Distinct().Select(p => (Core.Config.IconSource)new Core.Config.IconSource.Exe(p))]);
            Keys.Deliver(tracker.Fill(items, front));
        }
        WatchHeld();
    }

    private static DispatcherTimer? _heldTimer;

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
            _heldTimer = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromMilliseconds(100) };
            _heldTimer.Tick += (_, _) => CheckHeld();
        }
        if (!_heldTimer.IsEnabled) _heldTimer.Start();
    }

    /// Whether a key is down now. The self-test swaps it, since its keys are only pretend.
    public static Func<int, bool> IsKeyDown { get; set; } = vk => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;

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

    private static DispatcherTimer? _scheduleTimer;

    /// One timer, armed for the schedule's next deadline (the delay, the timeout).
    private static void Arm()
    {
        if (_scheduleTimer is null)
        {
            _scheduleTimer = new DispatcherTimer(DispatcherPriority.Normal, _ui);
            _scheduleTimer.Tick += (_, _) =>
            {
                _scheduleTimer.Stop();
                if ((Keys.Config ?? Config.Current) is { } loaded) Apply(Schedule.Tick(Environment.TickCount64), loaded);
            };
        }
        _scheduleTimer.Stop();
        if (Schedule.NextDeadline is not { } at) return;
        _scheduleTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, at - Environment.TickCount64));
        _scheduleTimer.Start();
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
