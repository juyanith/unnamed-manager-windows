namespace UnnamedManager.Core.Windows;

/// Where the Guide sits (S-034): on the monitor of the window in front, centered
/// across it, and at its center, top or bottom. Physical pixels in, physical pixels
/// out; the Guide's own sizes are in points at that monitor's scale.
public static class GuidePlacement
{
    /// At most this share of the work area's width.
    public const double MaxWidthShare = 0.8;

    /// The monitor for the window in front: the one it overlaps most, else the primary
    /// one (the first). With no window in front, the primary one.
    public static Screen? ScreenFor(PixelRect? front, IReadOnlyList<Screen> screens) =>
        front is { } frame ? WindowGeometry.ScreenFor(frame, screens) : screens.FirstOrDefault();

    /// The widest the Guide's content may be, in points.
    public static double MaxWidthPoints(Screen screen) => screen.Work.Width / screen.Scale * MaxWidthShare;

    /// The top left corner for a Guide of this outer size. `position` is center, top or
    /// bottom, any case; top and bottom sit `edgePoints` from the work area's edge.
    public static (int X, int Y) Origin(int width, int height, Screen screen, string position, double edgePoints)
    {
        var work = screen.Work;
        var edge = screen.Pixels(edgePoints);
        var x = work.X + (work.Width - width) / 2;
        var y = position.ToLowerInvariant() switch
        {
            "top" => work.Y + edge,
            "bottom" => work.Bottom - height - edge,
            _ => work.Y + (work.Height - height) / 2,
        };
        return (x, y);
    }
}
