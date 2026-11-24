namespace UnnamedManager.Core.Windows;

/// The windows that had focus, the most recent first, each once (spec v13). Focus
/// Window's and Swap Window's `recent` target reads it. Kept while the app runs.
public sealed class FocusHistory
{
    /// The spec asks for at least 10; Recent Windows needs 30, so 9 items are left once
    /// its filters drop some (spec v18).
    public const int Limit = 30;

    private readonly List<WindowRef> _windows = [];

    public IReadOnlyList<WindowRef> Windows => _windows;

    /// Puts a window first, as it gets focus.
    public void Record(WindowRef window)
    {
        _windows.Remove(window);
        _windows.Insert(0, window);
        if (_windows.Count > Limit) _windows.RemoveRange(Limit, _windows.Count - Limit);
    }

    /// Takes back recording `window`, which never got focus (S-084): `before` is the history
    /// from just before, and the windows focused since stay in front of it.
    public void TakeBack(WindowRef window, IReadOnlyList<WindowRef> before)
    {
        var at = _windows.IndexOf(window);
        var since = at > 0 ? _windows.GetRange(0, at) : [];
        _windows.Clear();
        _windows.AddRange(before);
        for (var i = since.Count - 1; i >= 0; i--) Record(since[i]);
    }

    /// The window used before `focused`: the first that isn't it and that `counts`,
    /// which says whether it still exists and can be picked.
    public WindowRef? Recent(WindowRef? focused, Func<WindowRef, bool> counts)
    {
        foreach (var window in _windows)
            if (window != focused && counts(window)) return window;
        return null;
    }
}
