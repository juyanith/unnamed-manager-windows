namespace UnnamedManager.Core.Windows;

/// What a top-level window is to the window actions (spec v21).
public enum WindowKind
{
    /// Never a candidate: a tooltip, a menu, a palette, a hidden window.
    None,
    Standard,
    /// Focused and listed as a window is, never swapped or arranged.
    Dialog,
}

public static class WindowKinds
{
    /// A standard window has no owner and isn't a tool window. A dialog on Windows has an
    /// owner, is enabled, has a caption and isn't a tool window (spec v21, Dialogs).
    public static WindowKind Of(bool visible, bool owned, bool toolWindow, bool enabled, bool caption) =>
        !visible || toolWindow ? WindowKind.None
        : !owned ? WindowKind.Standard
        : enabled && caption ? WindowKind.Dialog
        : WindowKind.None;
}
