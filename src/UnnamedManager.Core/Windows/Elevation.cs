namespace UnnamedManager.Core.Windows;

/// Apps running as administrator (S-056). Windows keeps an app that isn't elevated from
/// moving their windows or typing into them, and SendInput doesn't say when it was
/// blocked, so the app checks the window in front first and says why.
public static class Elevation
{
    public static string Message(string app) => $"{app} runs as administrator, so Unnamed Manager can't change it.";

    /// Why Send Shortcut can't type into the window in front, or null when it can.
    public static string? BlocksSend(ForegroundWindow? front) => front is { Elevated: true } ? Message(front.AppName) : null;

    /// The tray menu's two lines while one is in front.
    public const string TrayLine = "An app running as administrator is in front";
    public static string TrayWhy(string app) => $"Windows keeps Unnamed Manager from moving {app} or typing into it.";
}
