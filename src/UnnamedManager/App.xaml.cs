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
        // Agent modes run once the window has drawn its first frame.
        MainWindow.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            await SelfTest.RunIfRequestedAsync(MainWindow);
            await Snapshot.RunIfRequestedAsync(MainWindow);
        });
    }
}
