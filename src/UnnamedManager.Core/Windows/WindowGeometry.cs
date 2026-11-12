namespace UnnamedManager.Core.Windows;

/// A rectangle in physical pixels, in virtual-screen coordinates: the origin is the top
/// left of the primary monitor, y grows down, and monitors left of or above it are negative.
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    /// The area this shares with `other`, 0 when they don't overlap.
    public long Overlap(PixelRect other)
    {
        long w = Math.Min(Right, other.Right) - Math.Max(X, other.X), h = Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

/// One monitor: its whole area, its work area (without the taskbar), and its scale
/// (1.5 at 150 %). Spec points are effective pixels: physical pixels / scale.
public sealed record Screen(PixelRect Bounds, PixelRect Work, double Scale = 1)
{
    /// Spec points in this monitor's physical pixels, rounded.
    public int Pixels(double points) => (int)Math.Round(points * Scale, MidpointRounding.AwayFromZero);
}

/// The geometry the window actions share (spec v2, Window actions). It works in
/// physical pixels; spec points become pixels at the monitor's scale.
public static class WindowGeometry
{
    /// How close two edges must be to count as the same (spec: 2 points).
    public const double TolerancePoints = 2;

    /// The monitor that holds the largest part of the window, or the first (the primary)
    /// when the window is on none.
    public static Screen? ScreenFor(PixelRect window, IReadOnlyList<Screen> screens)
    {
        Screen? best = null;
        long most = 0;
        foreach (var screen in screens)
        {
            var area = screen.Bounds.Overlap(window);
            if (area > most) (best, most) = (screen, area);
        }
        return best ?? screens.FirstOrDefault();
    }

    /// Whether every edge of `a` is within the tolerance of the same edge of `b`.
    public static bool Matches(PixelRect a, PixelRect b, Screen screen)
    {
        var t = screen.Pixels(TolerancePoints);
        return Math.Abs(a.X - b.X) <= t && Math.Abs(a.Y - b.Y) <= t && Math.Abs(a.Right - b.Right) <= t && Math.Abs(a.Bottom - b.Bottom) <= t;
    }
}
