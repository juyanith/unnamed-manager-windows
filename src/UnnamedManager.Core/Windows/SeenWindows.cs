namespace UnnamedManager.Core.Windows;

/// A window Focus Window and Swap Window can pick (spec v12): its frame, the part of it
/// you can see (the frame minus the windows in front of it, as rects that don't
/// overlap), and the process that owns it.
public sealed record SeenWindow(PixelRect Frame, IReadOnlyList<PixelRect> Parts, int Owner = 1)
{
    /// With no parts given, the whole frame shows.
    public SeenWindow(PixelRect frame, int owner = 1) : this(frame, [frame], owner) { }

    /// The box around the part you can see; the frame when none shows.
    public PixelRect Box => Parts.Count == 0 ? Frame : Parts.Skip(1).Aggregate(Parts[0], PixelRect.Union);
}

/// The distances Focus Window and Swap Window use (spec v12), in physical pixels.
public readonly record struct SeenDistances(int MinVisible, int RowTolerance, int TieTolerance)
{
    public static SeenDistances From(Config.WindowSettings settings, Screen screen) =>
        new(screen.Pixels(settings.MinVisible), screen.Pixels(settings.RowTolerance), screen.Pixels(settings.TieTolerance));

    /// The defaults at 100 %: 20, 20 and 2.
    public static SeenDistances Default => From(new Config.WindowSettings(), new Screen(default, default));
}

/// What Swap Window does: trade places with a window, stay (the target is the focused
/// window), or fail with what to say.
public abstract record SwapPick
{
    public sealed record With(int Index) : SwapPick;
    public sealed record Stay : SwapPick;
    public sealed record None(string Failure) : SwapPick;
}

public static partial class WindowGeometry
{
    /// Each window's visible part, for windows front to back (spec v12): its frame minus
    /// every window in front of it.
    public static IReadOnlyList<IReadOnlyList<PixelRect>> VisibleParts(IReadOnlyList<PixelRect> frames)
    {
        var result = new List<IReadOnlyList<PixelRect>>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            IEnumerable<PixelRect> parts = [frames[i]];
            for (var j = 0; j < i; j++)
            {
                var cover = frames[j];
                parts = parts.SelectMany(p => Subtract(cover, p)).ToList();
            }
            result.Add(parts.ToList());
        }
        return result;
    }

    /// `rect` without `cover`: up to four rects, above, below, left and right of it.
    public static IEnumerable<PixelRect> Subtract(PixelRect cover, PixelRect rect)
    {
        int left = Math.Max(rect.X, cover.X), right = Math.Min(rect.Right, cover.Right);
        int top = Math.Max(rect.Y, cover.Y), bottom = Math.Min(rect.Bottom, cover.Bottom);
        if (right <= left || bottom <= top) return [rect];
        return new[]
        {
            PixelRect.FromEdges(rect.X, rect.Y, rect.Right, top),
            PixelRect.FromEdges(rect.X, bottom, rect.Right, rect.Bottom),
            PixelRect.FromEdges(rect.X, top, left, bottom),
            PixelRect.FromEdges(right, top, rect.Right, bottom),
        }.Where(r => r.Width > 0 && r.Height > 0);
    }

    /// Whether `parts` hold a square `side` pixels on a side (`windows.minVisible`); with
    /// 0, whether any part shows. A square that fits slides left and up until it meets
    /// the left and top edges of parts, so only those corners need trying.
    public static bool Shows(IReadOnlyList<PixelRect> parts, int side)
    {
        if (side <= 0) return parts.Count > 0;
        long need = (long)side * side;
        foreach (var x in parts.Select(p => p.X).Distinct())
            foreach (var y in parts.Select(p => p.Y).Distinct())
            {
                var square = new PixelRect(x, y, side, side);
                if (parts.Sum(p => p.Overlap(square)) >= need) return true;
            }
        return false;
    }

    /// A box in doubles, turned so a direction points to growing x.
    private readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double MidX => (MinX + MaxX) / 2;
        public double MidY => (MinY + MaxY) / 2;
        public static Box Union(Box a, Box b) => new(Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));
    }

    /// Left mirrors x, down swaps the axes, up does both.
    private static Box Turned(PixelRect r, string direction) => direction switch
    {
        "left" => new(-r.Right, r.Y, -r.X, r.Bottom),
        "down" => new(r.Y, r.X, r.Bottom, r.Right),
        "up" => new(-r.Bottom, r.X, -r.Y, r.Right),
        _ => new(r.X, r.Y, r.Right, r.Bottom),
    };

    /// The length the spans cover together.
    private static double Covered(IEnumerable<(double Low, double High)> spans)
    {
        double total = 0, end = double.NegativeInfinity;
        foreach (var (low, high) in spans.OrderBy(s => s.Low))
        {
            if (high <= end) continue;
            total += high - Math.Max(low, end);
            end = high;
        }
        return total;
    }

    /// The window Focus Window picks in a direction (S-028, spec v12), among `windows`
    /// front to back. Only the visible part past the current window's center counts.
    /// Windows that share `MinVisible` pixels with it across the direction are in line
    /// and come first: the smallest gap from its edge wins, gaps within `TieTolerance`
    /// tying, then the one sharing more, then the center nearest across, then the one in
    /// front. With none in line, the nearest center within 45° of the direction. Null
    /// when there's none that way.
    public static int? Neighbor(PixelRect current, string direction, IReadOnlyList<SeenWindow> windows, SeenDistances distances)
    {
        if (!WindowTarget.Directions.Contains(direction)) return null;
        var here = Turned(current, direction);
        var candidates = new List<(int Index, double Gap, double Shared, double Across, double Along)>();
        for (var i = 0; i < windows.Count; i++)
        {
            var past = windows[i].Parts.Select(p => Turned(p, direction))
                .Select(p => p with { MinX = Math.Max(p.MinX, here.MidX) })
                .Where(p => p.MaxX > p.MinX).ToList();
            if (past.Count == 0) continue;
            var box = past.Skip(1).Aggregate(past[0], Box.Union);
            var shared = Covered(past.Select(p => (Math.Max(p.MinY, here.MinY), Math.Min(p.MaxY, here.MaxY))).Where(s => s.Item2 > s.Item1));
            candidates.Add((i, Math.Max(0, box.MinX - here.MaxX), shared, Math.Abs(box.MidY - here.MidY), box.MidX - here.MidX));
        }
        var inLine = candidates.Where(c => c.Shared > 0 && c.Shared >= distances.MinVisible).ToList();
        if (inLine.Count > 0)
        {
            var nearest = inLine.Min(c => c.Gap);
            return inLine.Where(c => c.Gap <= nearest + distances.TieTolerance)
                .OrderBy(c => -c.Shared).ThenBy(c => c.Across).ThenBy(c => c.Index).First().Index;
        }
        var cone = candidates.Where(c => c.Across <= c.Along).ToList();
        return cone.Count == 0 ? null : cone.OrderBy(c => Math.Sqrt(c.Along * c.Along + c.Across * c.Across)).ThenBy(c => c.Index).First().Index;
    }

    /// The order Next and Previous go through, and First, Last and numbers count in
    /// (S-029, spec v12): by monitor, left to right, then in rows as text reads, by the
    /// top left of the box around each window's visible part. Tops within `RowTolerance`
    /// of a row's highest share its row. Owners and sizes break the ties that are left,
    /// so the same windows always give the same order, whatever order they come in.
    public static IReadOnlyList<int> CycleOrder(IReadOnlyList<SeenWindow> windows, IReadOnlyList<Screen> screens, SeenDistances distances)
    {
        var monitors = screens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToList();
        var boxes = windows.Select(w => w.Box).ToList();
        var shown = windows.Select(w => ScreenFor(w.Frame, screens) is { } s ? monitors.IndexOf(s) : 0).ToList();
        var rows = new int[windows.Count];
        foreach (var group in Enumerable.Range(0, windows.Count).GroupBy(i => shown[i]))
        {
            int row = 0;
            long top = long.MinValue;
            foreach (var i in group.OrderBy(i => boxes[i].Y))
            {
                if (boxes[i].Y > top + distances.RowTolerance)
                {
                    row++;
                    top = boxes[i].Y;
                }
                rows[i] = row;
            }
        }
        return Enumerable.Range(0, windows.Count)
            .OrderBy(i => shown[i]).ThenBy(i => rows[i]).ThenBy(i => boxes[i].X).ThenBy(i => boxes[i].Y)
            .ThenBy(i => windows[i].Owner).ThenBy(i => boxes[i].Width).ThenBy(i => boxes[i].Height).ThenBy(i => i)
            .ToList();
    }

    /// The window Swap Window trades places with (spec v10), among `windows` (Focus
    /// Window's candidates, for its scope, front to back). `current` is the focused
    /// window's index among them, null when it isn't one; `frame` is its frame.
    public static SwapPick SwapPartner(WindowTarget target, int? current, PixelRect frame, IReadOnlyList<SeenWindow> windows,
        IReadOnlyList<Screen> screens, SeenDistances distances)
    {
        var order = CycleOrder(windows, screens, distances);
        SwapPick Pick(int? index, string failure) => index is not { } i ? new SwapPick.None(failure) : i == current ? new SwapPick.Stay() : new SwapPick.With(i);
        switch (target.Word)
        {
            case "next" or "previous":
                return Pick(Step(current, order, target.Word == "previous"), "No other window to swap with.");
            case "first":
                return Pick(order.Count > 0 ? order[0] : null, "No window to swap with.");
            case "last":
                return Pick(order.Count > 0 ? order[^1] : null, "No window to swap with.");
            case { } direction:
                var others = Enumerable.Range(0, windows.Count).Where(i => i != current).ToList();
                return Pick(Neighbor(frame, direction, [.. others.Select(i => windows[i])], distances) is { } n ? others[n] : null, target.Failure);
            default:
                return Pick(target.Number <= order.Count ? order[target.Number - 1] : null, WindowTarget.PastTheEnd(target.Number, order.Count));
        }
    }
}
