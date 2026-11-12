using UnnamedManager.Core;
using UnnamedManager.UI;
using Microsoft.UI.Xaml;

namespace UnnamedManager;

// UnnamedManager
//   UnnamedManager.exe                         the app
//   APP_STORE=<file> UnnamedManager.exe        use another database (tests, experiments); must be in %TEMP%
//   APP_SNAPSHOT=<dir> UnnamedManager.exe      save a PNG of every page and Settings in light and dark, then quit
//   APP_SNAPSHOT_PAGES=all,settings          limit the snapshot pages
//   APP_SEED=1 UnnamedManager.exe              fill an empty %TEMP% store with sample items (tools/snap.ps1)
//   UnnamedManager.exe --selftest              run the self-test on a %TEMP% store, exit 0 or 1 (tools/selftest.ps1)
//   APP_ACCENT=<id> UnnamedManager.exe         compare another accent from Theme.All in snapshots (UI/Theme.cs);
//                                              the app itself always follows the Windows accent
//   APP_GRID=1 UnnamedManager.exe              draw the 8 epx grid and the layout probes over the window
public partial class App : Application
{
    public static AppModel Model { get; private set; } = null!;
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
        var store = new ItemStore(AppPaths.Store);
        SeedData.InsertIfRequested(store);
        Model = new AppModel(store);
        MainWindow = new MainWindow(Model);
        MainWindow.Activate();
        // Agent modes run once the window has drawn its first frame.
        MainWindow.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            await SelfTest.RunIfRequestedAsync(MainWindow, Model);
            await Snapshot.RunIfRequestedAsync(MainWindow, Model);
        });
    }
}
