namespace UnnamedManager.Core.Windows;

/// Where a visible top-level window is, for who may list it or go to it (S-078).
public enum WindowPresence
{
    /// On the desktop shown.
    Here,
    /// On another virtual desktop: Windows says so.
    Elsewhere,
    /// Not a window you can go to: a suspended Store app's, or one hidden by its app.
    Nowhere,
}

/// Tells a window on another virtual desktop from one that only looks like it.
public static class Presence
{
    /// DWM_CLOAKED_SHELL: the shell hides the window, for another desktop or a suspended Store app.
    public const int CloakedByShell = 2;

    /// `cloaked` is DWMWA_CLOAKED's value, and `onCurrentDesktop` what IVirtualDesktopManager
    /// says, null when it doesn't. Cloaked by the shell isn't enough to be on another
    /// desktop: Windows has to say it is, and "doesn't say" isn't a yes.
    public static WindowPresence Of(int cloaked, bool? onCurrentDesktop) => cloaked switch
    {
        0 => onCurrentDesktop == false ? WindowPresence.Elsewhere : WindowPresence.Here,
        CloakedByShell when onCurrentDesktop == false => WindowPresence.Elsewhere,
        _ => WindowPresence.Nowhere,
    };
}
