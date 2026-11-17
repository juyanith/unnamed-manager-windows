using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Windows;

/// The grid Arrange Windows makes, and which window goes in which cell (spec v8, with the
/// focused window's cell from spec v9). Pure, so the app and the tests can both ask it.
public static class ArrangeGrid
{
    /// One cell, as fractions of the work area. Neighbours get the same value for the edge
    /// they share, so they round to the same pixel.
    public readonly record struct Cell(double MinX, double MaxX, double MinY, double MaxY)
    {
        /// The cell in the work area, before the gap. Edges round to whole pixels.
        public PixelRect Rect(PixelRect work)
        {
            static int R(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
            return PixelRect.FromEdges(work.X + R(MinX * work.Width), work.Y + R(MinY * work.Height),
                work.X + R(MaxX * work.Width), work.Y + R(MaxY * work.Height));
        }
    }

    /// How many windows each group holds, for `count` windows in `groups` columns or rows.
    /// The plain grid has `Per` cells in every group, and the spare ones have no window.
    /// The focused window, in the group `focused`, gets the spare cells: its cell spans
    /// `Span` cells there. With more spare cells than its group has, the other groups
    /// share the rest, the ones with a single window on the right or at the top.
    public static (IReadOnlyList<int> Sizes, int Per, int? Focused, int Span) Shape(int count, int groups, bool rows, int focused)
    {
        int per = (count + groups - 1) / groups, spare = groups * per - count;
        if (spare <= 0) return (Enumerable.Repeat(per, groups).ToList(), per, null, 1);
        int span = Math.Min(spare + 1, per), own = per - span + 1;
        int others = groups - 1, each = (count - own) / others, extra = (count - own) % others;
        var sizes = Enumerable.Range(0, others).Select(i => each + ((rows ? i >= others - extra : i < extra) ? 1 : 0)).ToList();
        sizes.Insert(focused, own);
        return (sizes, per, focused, span);
    }

    /// Where each window goes. `windows` are the frames of the candidates on the display,
    /// the focused one first, then front to back. The result has an index into `windows`
    /// and its cell, for the windows that are arranged; the others stay as they are.
    /// `tolerance` is how close two centers must be to count as the same (2 points).
    public static IReadOnlyList<(int Window, Cell Cell)> Plan(ArrangeAction action, int maxColumns, int maxRows, PixelRect usable,
        IReadOnlyList<PixelRect> windows, int tolerance)
    {
        if (windows.Count == 0) return [];
        var count = action.Count ?? Math.Max(1, Math.Min(windows.Count, Math.Min(ArrangeAction.MaxCount, maxColumns * maxRows)));
        var rows = action.Layout == "rows" || (action.Layout == "auto" && usable.Height > usable.Width);
        var groups = Math.Min(count, Math.Max(1, action.Groups ?? (rows ? maxRows : maxColumns)));
        var used = Enumerable.Range(0, Math.Min(count, windows.Count)).Select(i => (int?)i).ToList();

        // Windows keep the order they're in: by center along the groups, or across one.
        // Centers within the tolerance are the same; what's still tied goes by use, the
        // window used more recently further right, or further up.
        bool Before(int a, int b, bool along)
        {
            var byX = along != rows;
            foreach (var key in byX ? new Func<PixelRect, double>[] { r => r.CenterX, r => r.CenterY } : [r => r.CenterY, r => r.CenterX])
                if (Math.Abs(key(windows[a]) - key(windows[b])) > tolerance) return key(windows[a]) < key(windows[b]);
            return byX ? a > b : a < b;
        }

        // A missing window (null) counts as furthest left and furthest down, so the empty cells end there.
        List<int?> Ordered(IEnumerable<int?> members, bool along)
        {
            var list = members.ToList();
            var real = new List<int>();
            foreach (var m in list.OfType<int>())
            {
                var at = real.FindIndex(r => Before(m, r, along));
                real.Insert(at < 0 ? real.Count : at, m);
            }
            var missing = Enumerable.Repeat<int?>(null, list.Count - real.Count);
            var present = real.Select(r => (int?)r);
            return along != rows ? [.. missing, .. present] : [.. present, .. missing];
        }

        // The focused window stays in the group its center is in.
        var center = rows ? (windows[0].CenterY - usable.Y) / usable.Height : (windows[0].CenterX - usable.X) / usable.Width;
        var home = double.IsFinite(center) ? Math.Clamp((int)Math.Floor(center * groups), 0, groups - 1) : 0;
        var shape = Shape(count, groups, rows, home);
        var order = Ordered(used.Concat(Enumerable.Repeat<int?>(null, count - used.Count)), along: true);
        // The focused window is dealt into its own group; the others fill the groups in order.
        if (shape.Focused is not null) order.RemoveAll(m => m == 0);
        var next = 0;
        var result = new List<(int, Cell)>();
        for (var group = 0; group < shape.Sizes.Count; group++)
        {
            var own = group == shape.Focused;
            var taken = order.Skip(next).Take(own ? shape.Sizes[group] - 1 : shape.Sizes[group]).ToList();
            next += taken.Count;
            // The focused window's group keeps the plain grid's cells; another group splits equally.
            var parts = own ? shape.Per : shape.Sizes[group];
            var start = 0;
            foreach (var member in Ordered(own ? [.. taken, 0] : taken, along: false))
            {
                var length = own && member == 0 ? shape.Span : 1;
                if (member is { } m)
                {
                    double a0 = (double)group / groups, a1 = (double)(group + 1) / groups;
                    double c0 = (double)start / parts, c1 = (double)(start + length) / parts;
                    result.Add((m, rows ? new Cell(c0, c1, a0, a1) : new Cell(a0, a1, c0, c1)));
                }
                start += length;
            }
        }
        return result;
    }
}
