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

        // S-024: the visible frame after placing matches the target, invisible borders included.
        var me = new WindowRef(hwnd);
        foreach (var preset in new[] { "leftHalf", "rightTwoThirds", "center" })
        {
            var fractions = Core.Config.PlaceAction.PresetFrame(preset);
            control.Run(new WindowCommand.Place(fractions));
            await Task.Delay(200);
            var placed = real.Facts(me)?.Frame;
            var screen = WindowGeometry.ScreenFor(placed ?? default, real.Screens())!;
            var target = WindowGeometry.Rect(fractions, screen.Work);
            Check($"Place {preset} puts the visible frame on its target within 1 pixel", placed is { } got
                && Math.Abs(got.X - target.X) <= 1 && Math.Abs(got.Y - target.Y) <= 1 && Math.Abs(got.Right - target.Right) <= 1 && Math.Abs(got.Bottom - target.Bottom) <= 1,
                $"{placed} for {target}");
        }
        if (before is not null) real.SetFrame(me, before.Frame);

        var fake = new FakeWindowSystem { FrontAppName = "Notepad" };
        var wide = fake.Add(2, new(100, 100, 800, 600));
        wide.MinSize = (1200, 400);
        new WindowControl(fake).Run(new WindowCommand.Place(Core.Config.PlaceAction.PresetFrame("rightHalf")));
        Check("a window with a larger minimum ends anchored to the placement's edge", wide.Frame == new PixelRect(720, 0, 1200, 1032), $"{wide.Frame}");
        // S-026, against a fake window.
        new WindowControl(fake).Run(new WindowCommand.Move(new Core.Config.MoveAction(0.5, 0.5, 0, 0)));
        var centered = wide.Frame;
        new WindowControl(fake).Run(new WindowCommand.Move(new Core.Config.MoveAction(null, null, -30, 20)));
        Check("Move changes where the window is, never its size",
            (centered.Width, centered.Height) == (wide.Frame.Width, wide.Frame.Height) && wide.Frame.X == centered.X - 30 && wide.Frame.Y == centered.Y + 20,
            $"{centered} then {wide.Frame}");
        fake.Windows.Clear();
        fake.Front = null;
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
