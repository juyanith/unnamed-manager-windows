using UnnamedManager.Guide;
using UnnamedManager.Keyboard;
using UnnamedManager.UI;
using Microsoft.UI.Xaml;

namespace UnnamedManager;

// UnnamedManager
//   UnnamedManager.exe                         the app
//   APP_STORE=<file> UnnamedManager.exe        use another config file (tests, experiments); must be in %TEMP%
//   APP_SNAPSHOT=<dir> UnnamedManager.exe      save a PNG of every page in light and dark, then quit
//   APP_SNAPSHOT_PAGES=settings              limit the snapshot pages
//   UnnamedManager.exe --selftest              run the self-test on a %TEMP% config, exit 0 or 1 (tools/selftest.ps1)
//   APP_ACCENT=<id> UnnamedManager.exe         compare another accent from Theme.All in snapshots (UI/Theme.cs);
//                                              the app itself always follows the Windows accent
//   APP_GRID=1 UnnamedManager.exe              draw the 8 epx grid and the layout probes over the window
public partial class App : Application
{
    public static ConfigStore Config { get; private set; } = null!;
    public static MainWindow MainWindow { get; private set; } = null!;
    public static KeyService Keys { get; private set; } = null!;
    public static GuideWindow Guide { get; private set; } = null!;
    public static Actions.ActionRunner Runner { get; } = new();

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => AgentLog.Line($"CRASH {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The accent must be in the resources before the first control loads.
        // Not in the constructor: Application.Current.Resources throws E_UNEXPECTED there.
        Theme.ApplyAccent(Environment.GetEnvironmentVariable("APP_ACCENT") ?? Theme.DefaultId);
        // Agent modes never read or write the real config.
        if ((SelfTest.Requested || Snapshot.Requested) && !AppPaths.IsTestStore)
        {
            AgentLog.Line("refused: set APP_STORE to a file in %TEMP%");
            AgentLog.Exit(1);
        }
        Config = new ConfigStore(AppPaths.ConfigFile);
        Config.Load();
        MainWindow = new MainWindow();
        MainWindow.Activate();
        Keys = new KeyService(MainWindow.DispatcherQueue);
        Guide = new GuideWindow();
        Keys.Effect += effect => AgentLog.Line($"SEQUENCE {Describe(effect)}");
        Keys.Effect += OnSequenceEffect;
        // Snapshots need no keys; everything else captures leaders once the config loads.
        if (!Snapshot.Requested && Config.Current is { } loaded) Keys.Start(loaded);
        // Agent modes run once the window has drawn its first frame.
        MainWindow.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            await SelfTest.RunIfRequestedAsync(MainWindow);
            await Snapshot.RunIfRequestedAsync(MainWindow);
        });
    }

    /// The Guide follows the sequence: a menu shows, and the end of a sequence hides it.
    private static void OnSequenceEffect(Core.Keyboard.SequenceEffect effect)
    {
        switch (effect)
        {
            case Core.Keyboard.SequenceEffect.ShowMenu show when Config.Current is { } loaded && loaded.Config.Runtime.ShowHud:
                Guide.Show(show.Menu, show.Path, loaded);
                break;
            case Core.Keyboard.SequenceEffect.Run run:
                Guide.Hide();
                if (Config.Current is { } config) _ = Runner.RunAsync(run.Binding, config);
                break;
            case Core.Keyboard.SequenceEffect.Cancelled:
                if (Guide.IsShown) Guide.View.Announce("Cancelled");
                Guide.Hide();
                break;
        }
    }

    private static string Describe(Core.Keyboard.SequenceEffect effect) => effect switch
    {
        Core.Keyboard.SequenceEffect.ShowMenu m => $"menu {string.Join(" › ", m.Path)}",
        Core.Keyboard.SequenceEffect.Run r => $"run {r.Binding.Path} ({r.Binding.Title})",
        Core.Keyboard.SequenceEffect.Unbound u => $"unbound in {string.Join(" › ", u.Path)}",
        _ => "cancelled",
    };
}
