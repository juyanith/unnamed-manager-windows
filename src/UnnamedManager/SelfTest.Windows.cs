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
        // S-030: with a gap of 8, the halves stand 8 points from the edges and from each other.
        var gapped = new WindowControl(real, () => 8);
        gapped.Run(new WindowCommand.Place(Core.Config.PlaceAction.PresetFrame("leftHalf")));
        await Task.Delay(200);
        var leftHalf = real.Facts(me)?.Frame;
        gapped.Run(new WindowCommand.Place(Core.Config.PlaceAction.PresetFrame("rightHalf")));
        await Task.Delay(200);
        var rightHalf = real.Facts(me)?.Frame;
        if (leftHalf is { } l && rightHalf is { } r && WindowGeometry.ScreenFor(r, real.Screens()) is { } on)
        {
            double Points(int pixels) => pixels / on.Scale;
            var gaps = new[] { Points(l.X - on.Work.X), Points(l.Y - on.Work.Y), Points(r.X - l.Right), Points(on.Work.Right - r.Right), Points(on.Work.Bottom - r.Bottom) };
            Check("with a gap of 8, the left and right halves stand 8 points from the edges and from each other",
                gaps.All(g => Math.Abs(g - 8) <= 1 / on.Scale), string.Join(", ", gaps.Select(g => g.ToString("0.#"))));
        }
        else Check("with a gap of 8, the left and right halves stand 8 points from the edges and from each other", false, "no frame");
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
        // S-027: a fixed size, as a dialog has.
        var dialog = fake.Add(3, new(400, 200, 500, 300));
        dialog.IsFixedSize = true;
        var resized = new WindowControl(fake).Run(new WindowCommand.Resize(new Core.Config.ResizeAction("by", 200, 100, null, null, "center")));
        Check("Resize on a fixed-size window reports no failure and leaves it as it was",
            resized is null && dialog.Frame == new PixelRect(400, 200, 500, 300), $"{resized} {dialog.Frame}");
        // S-028: fake windows in a 2×2 grid across two monitors.
        fake.Windows.Clear();
        fake.Monitors.Add(new Screen(new(1920, 0, 2560, 1440), new(1920, 0, 2560, 1392), 1.25));
        fake.Add(1, new(100, 100, 800, 400));
        fake.Add(2, new(100, 600, 800, 400));
        fake.Add(3, new(2100, 100, 800, 400));
        fake.Add(4, new(2100, 600, 800, 400));
        var picks = new List<string>();
        foreach (var (from, direction) in new[] { (1, "right"), (1, "down"), (4, "left"), (4, "up"), (3, "down"), (2, "up") })
        {
            fake.Front = from;
            new WindowControl(fake).Run(new WindowCommand.Focus(direction));
            picks.Add($"{from} {direction} {fake.Front}");
        }
        Check("Focus Window picks the expected window in a 2×2 grid across two monitors",
            string.Join(", ", picks) == "1 right 3, 1 down 2, 4 left 2, 4 up 3, 3 down 4, 2 up 1", string.Join(", ", picks));
        // S-029 on the same grid: the order is 1, 2, 3, 4.
        fake.Front = 4;
        new WindowControl(fake).Run(new WindowCommand.Focus("next"));
        var wrapped = fake.Front;
        new WindowControl(fake).Run(new WindowCommand.Focus("previous"));
        Check("Next from the last window goes to the first, and Previous comes back", (wrapped, fake.Front) == (1, 4), $"{wrapped} then {fake.Front}");
        var focusable = real.Focusable();
        Check("the real candidates leave out this app's own windows and the Guide",
            focusable.All(w => { Win32.GetWindowThreadProcessId(w.Window.Handle, out var pid); return pid != Environment.ProcessId; }) && focusable.All(w => w.Window.Handle != hwnd),
            $"{focusable.Count} windows");
        fake.Monitors.RemoveAt(1);
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
