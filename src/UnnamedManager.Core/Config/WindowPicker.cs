using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// What Use Application needs to know about one top-level window (S-018).
public readonly record struct WindowFacts(
    nint Handle,
    bool BelongsToApp,
    bool Visible,
    bool Minimized,
    bool ToolWindow,
    WindowPresence Presence,
    WindowKind Kind,
    bool Ours,
    bool Hung,
    bool Blocked = false);

/// Picks the window Use Application brings forward: the one used last.
public static class WindowPicker
{
    /// The app's window used last wins, on this virtual desktop or another (S-080): first
    /// by `history` (most recent first), then by `zOrder`, which runs front to back as
    /// EnumWindows gives it. A minimized one, which gets restored, only when the app has no
    /// other. A dialog counts, and a window it blocks stands aside for it (spec v21). Tool
    /// windows, windows that are nowhere you can go (a suspended Store app's), other owned
    /// windows, Unnamed Manager's own and windows that don't answer are skipped.
    public static nint Pick(IEnumerable<WindowFacts> zOrder, IReadOnlyList<WindowRef>? history = null)
    {
        var used = history ?? [];
        int Place(nint handle)
        {
            for (var i = 0; i < used.Count; i++) if (used[i].Handle == handle) return i;
            return int.MaxValue;
        }
        // OrderBy is stable, so windows the history doesn't hold stay front to back.
        return zOrder.Where(CanGoTo).OrderBy(w => w.Minimized).ThenBy(w => Place(w.Handle)).FirstOrDefault().Handle;
    }

    private static bool CanGoTo(WindowFacts w) =>
        w.BelongsToApp && w.Visible && !w.ToolWindow && w.Presence != WindowPresence.Nowhere && w.Kind != WindowKind.None && !w.Blocked && !w.Ours && !w.Hung;
}
