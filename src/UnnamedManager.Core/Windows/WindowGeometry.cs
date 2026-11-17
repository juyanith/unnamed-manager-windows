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

    /// The smallest rect that holds both.
    public static PixelRect Union(PixelRect a, PixelRect b) =>
        FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));

    public double CenterX => X + Width / 2.0;
    public double CenterY => Y + Height / 2.0;

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
public static partial class WindowGeometry
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

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// A frame of fractions in the work area. Edges round to whole pixels and sizes
    /// follow, so neighbouring placements share an edge with no gap or overlap.
    public static PixelRect Rect(Config.FractionFrame f, PixelRect work)
    {
        int left = Round(f.X * work.Width), right = Math.Min(Round((f.X + f.Width) * work.Width), work.Width);
        int top = Round(f.Y * work.Height), bottom = Math.Min(Round((f.Y + f.Height) * work.Height), work.Height);
        return PixelRect.FromEdges(work.X + left, work.Y + top, work.X + right, work.Y + bottom);
    }

    /// A placed frame with the gap around it (spec v3, S-030). The gap rounds to whole
    /// points, then to pixels at the monitor's scale. Edges on the work area's edge move in
    /// by the whole gap, edges inside it by half, the left and top by the larger half, so
    /// neighbours end exactly one gap apart. A window keeps at least 1 pixel each way.
    public static PixelRect WithGap(PixelRect frame, Screen screen, double gapPoints)
    {
        var work = screen.Work;
        var g = screen.Pixels(Math.Max(0, Math.Round(gapPoints, MidpointRounding.AwayFromZero)));
        int larger = (g + 1) / 2, smaller = g / 2;
        var left = frame.X + (frame.X <= work.X ? g : larger);
        var right = frame.Right - (frame.Right >= work.Right ? g : smaller);
        var top = frame.Y + (frame.Y <= work.Y ? g : larger);
        var bottom = frame.Bottom - (frame.Bottom >= work.Bottom ? g : smaller);
        return new PixelRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    /// The work area less the gap on every side, for a window that won't take its placement.
    public static PixelRect Inset(PixelRect work, Screen screen, double gapPoints)
    {
        var g = screen.Pixels(Math.Max(0, Math.Round(gapPoints, MidpointRounding.AwayFromZero)));
        return PixelRect.FromEdges(work.X + g, work.Y + g, Math.Max(work.X + g, work.Right - g), Math.Max(work.Y + g, work.Bottom - g));
    }

    /// Where a window that took `size` instead of the target's goes: on each axis it keeps
    /// the edge the target shares with `area`, or its center when it shares neither or both
    /// (then the left or top edge).
    public static PixelRect Anchored((int Width, int Height) size, PixelRect target, PixelRect area, Screen screen)
    {
        var t = screen.Pixels(TolerancePoints);
        int Origin(int min, int max, int areaMin, int areaMax, int length)
        {
            bool atMin = Math.Abs(min - areaMin) <= t, atMax = Math.Abs(max - areaMax) <= t;
            if (atMin) return min;
            if (atMax) return max - length;
            return Round((min + max - length) / 2.0);
        }
        return new PixelRect(Origin(target.X, target.Right, area.X, area.Right, size.Width),
            Origin(target.Y, target.Bottom, area.Y, area.Bottom, size.Height), size.Width, size.Height);
    }

    /// The window after one step through `widths` (S-025): the smallest width more than
    /// the tolerance wider (grow), or the largest narrower (shrink), or null past the end.
    /// It keeps its left edge unless the new width would run past the work area, when it
    /// keeps its right edge. The height stays.
    public static PixelRect? Step(PixelRect frame, Screen screen, IReadOnlyList<double> widths, bool grow)
    {
        var t = screen.Pixels(TolerancePoints);
        var pixels = widths.Select(w => Round(w * screen.Work.Width)).Distinct().Order().ToList();
        int? width = grow ? pixels.Cast<int?>().FirstOrDefault(w => w > frame.Width + t) : pixels.Cast<int?>().LastOrDefault(w => w < frame.Width - t);
        if (width is not { } w) return null;
        var x = frame.X;
        if (x + w > screen.Work.Right) x = Math.Max(screen.Work.X, frame.Right - w);
        return new PixelRect(x, frame.Y, w, frame.Height);
    }

    /// The size Resize Window asks for with `to` (fractions of the work area) or `by`
    /// (points), never bigger than the work area nor smaller than 1 pixel (S-027).
    public static (int Width, int Height) Size(PixelRect frame, Screen screen, Config.ResizeAction resize)
    {
        var work = screen.Work;
        var (w, h) = resize.Change == "to"
            ? (Round(resize.A * work.Width), Round(resize.B * work.Height))
            : (frame.Width + screen.Pixels(resize.A), frame.Height + screen.Pixels(resize.B));
        return (Math.Clamp(w, 1, work.Width), Math.Clamp(h, 1, work.Height));
    }

    /// Where an anchor sits on a window, as fractions of its width and height.
    public static (double X, double Y) Unit(string anchor) => anchor switch
    {
        "top" => (0.5, 0), "topRight" => (1, 0),
        "left" => (0, 0.5), "center" => (0.5, 0.5), "right" => (1, 0.5),
        "bottomLeft" => (0, 1), "bottom" => (0.5, 1), "bottomRight" => (1, 1),
        _ => (0, 0),
    };

    /// A window of `size` with `anchor` where it was on `frame`, moved back inside the
    /// work area when it runs past it.
    public static PixelRect Resized(PixelRect frame, (int Width, int Height) size, string anchor, PixelRect work)
    {
        var (ax, ay) = Unit(anchor);
        var x = Round(frame.X + ax * (frame.Width - size.Width));
        var y = Round(frame.Y + ay * (frame.Height - size.Height));
        return new PixelRect(Math.Min(Math.Max(x, work.X), Math.Max(work.X, work.Right - size.Width)),
            Math.Min(Math.Max(y, work.Y), Math.Max(work.Y, work.Bottom - size.Height)), size.Width, size.Height);
    }

    /// The window after `current` in `order`, wrapping at the ends; the first (or last for
    /// Previous) when the current one isn't in it. Null when there's no other window.
    public static int? Step(int? current, IReadOnlyList<int> order, bool previous)
    {
        if (!order.Any(i => i != current)) return null;
        var position = current is { } c ? order.ToList().IndexOf(c) : -1;
        if (position < 0) return previous ? order[^1] : order[0];
        return order[(position + (previous ? order.Count - 1 : 1)) % order.Count];
    }

    /// How much of a window's title bar stays on its monitor after a move or resize (spec: 40 points).
    public const double TitleBarKeptPoints = 40;

    /// The window after Move Window (S-026). Its size stays the same. `to` places it at
    /// fractions of the free space; `by` moves it by points, never off screen.
    public static PixelRect Moved(PixelRect frame, Screen screen, Config.MoveAction move)
    {
        var work = screen.Work;
        if (move.IsTo)
        {
            int freeX = Math.Max(0, work.Width - frame.Width), freeY = Math.Max(0, work.Height - frame.Height);
            return frame with { X = work.X + Round(move.ToX!.Value * freeX), Y = work.Y + Round(move.ToY!.Value * freeY) };
        }
        return OnScreen(frame with { X = frame.X + screen.Pixels(move.Dx), Y = frame.Y + screen.Pixels(move.Dy) }, screen);
    }

    /// Keeps at least 40 points of the title bar on the work area: the top edge inside it,
    /// and that much of the top edge's length.
    public static PixelRect OnScreen(PixelRect frame, Screen screen)
    {
        var work = screen.Work;
        int kept = screen.Pixels(TitleBarKeptPoints), keep = Math.Min(kept, frame.Width);
        var x = Math.Min(Math.Max(frame.X, work.X - frame.Width + keep), work.Right - keep);
        var y = Math.Min(Math.Max(frame.Y, work.Y), work.Bottom - kept);
        return frame with { X = x, Y = y };
    }

    /// Whether every edge of `a` is within the tolerance of the same edge of `b`.
    public static bool Matches(PixelRect a, PixelRect b, Screen screen)
    {
        var t = screen.Pixels(TolerancePoints);
        return Math.Abs(a.X - b.X) <= t && Math.Abs(a.Y - b.Y) <= t && Math.Abs(a.Right - b.Right) <= t && Math.Abs(a.Bottom - b.Bottom) <= t;
    }
}
