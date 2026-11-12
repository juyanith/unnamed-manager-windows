using UnnamedManager.Actions;
using UnnamedManager.Core.Windows;
using UnnamedManager.UI;

namespace UnnamedManager;

/// The self-test's window action checks (M3). Real changes only touch the app's own
/// window; everything else runs against `FakeWindowSystem`.
public static partial class SelfTest
{
    private static async Task CheckWindowsAsync(MainWindow window)
    {
        var hwnd = window.Handle();
        window.AppWindow.Show();
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p) p.Restore();
        await Task.Delay(300);
        var real = new Win32WindowSystem(() => hwnd);
        var control = new WindowControl(real);
        var before = real.Facts(new WindowRef(hwnd));

        // S-022: the window's own maximize, then its own restore.
        var maximized = control.Run(new WindowCommand.Maximize(true)) is null;
        await Task.Delay(300);
        var facts = real.Facts(new WindowRef(hwnd));
        Check("Maximize maximizes a window that can, with SW_MAXIMIZE", maximized && facts is { IsMaximized: true, CanMaximize: true }, $"{facts}");
        control.Run(new WindowCommand.Maximize(true));
        await Task.Delay(300);
        facts = real.Facts(new WindowRef(hwnd));
        Check("pressing Maximize again restores it to where it was", facts is { IsMaximized: false } && facts.Frame == before?.Frame,
            $"{before?.Frame} then {facts?.Frame}");

        var fake = new FakeWindowSystem { FrontAppName = "Notepad" };
        Check("Maximize with no window in front fails with the app's name",
            new WindowControl(fake).Run(new WindowCommand.Maximize(true)) == "Notepad has no window to maximize.");

        // S-023, against a fake window.
        var notepad = fake.Add(1, new(100, 100, 800, 600));
        Check("Minimize minimizes the window in front", new WindowControl(fake).Run(new WindowCommand.Minimize()) is null && notepad.IsMinimized);
        fake.Front = null;
        Check("Minimize with no window in front fails with the app's name",
            new WindowControl(fake).Run(new WindowCommand.Minimize()) == "Notepad has no window to minimize.");

        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter q) q.Minimize();
    }
}
