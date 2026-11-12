namespace UnnamedManager.Core.Config;

/// What Use Application needs to know about one top-level window (S-018).
public readonly record struct WindowFacts(
    nint Handle,
    bool BelongsToApp,
    bool Visible,
    bool Minimized,
    bool ToolWindow,
    bool Cloaked,
    bool Owned,
    bool Ours,
    bool Hung);

/// Picks the window Use Application brings forward: the one used last.
public static class WindowPicker
{
    /// `zOrder` runs front to back, as EnumWindows gives it. The app's highest window
    /// that isn't minimized wins, so focus returns to the one used last; with none, its
    /// highest minimized one, which gets restored. Tool windows, cloaked windows (another
    /// desktop, a suspended Store app), owned windows (dialogs, palettes), Unnamed
    /// Manager's own windows and windows that don't answer are skipped.
    public static nint Pick(IEnumerable<WindowFacts> zOrder)
    {
        nint minimized = 0;
        foreach (var w in zOrder)
        {
            if (!w.BelongsToApp || !w.Visible || w.ToolWindow || w.Cloaked || w.Owned || w.Ours || w.Hung) continue;
            if (!w.Minimized) return w.Handle;
            if (minimized == 0) minimized = w.Handle;
        }
        return minimized;
    }
}
