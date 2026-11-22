namespace UnnamedManager.Core;

/// Run at startup, as Settings shows it (S-054).
public enum StartupState
{
    Off,
    On,
    /// The Run value is there, but Task Manager's Startup apps turned it off.
    TurnedOffInTaskManager,
}

/// The rules for the `Run` value and Task Manager's `StartupApproved\Run` bytes. The
/// registry calls live in the app; these take what they read.
public static class StartupEntry
{
    /// The value's name under both keys.
    public const string Name = "UnnamedManager";

    /// The Run value for an exe: its path in quotes, so a path with spaces starts.
    public static string Command(string exe) => $"\"{exe}\"";

    /// The exe a Run value starts, without quotes or arguments.
    public static string? Exe(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }

    /// The two apps that share the Run value.
    private static readonly string[] Apps = ["UnnamedManager.exe", "UnnamedManager.Wpf.exe"];

    /// Whether a Run value starts the other app (S-109). Only the two names count: a
    /// value with any other exe, or one that can't be read, is this app's, as before.
    public static bool IsOtherApp(string? runValue, string exe) =>
        Exe(runValue) is { } named && Path.GetFileName(named) is var name
        && Apps.Contains(name, StringComparer.OrdinalIgnoreCase)
        && !string.Equals(name, Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase);

    /// Run at startup for this app: the other app's value is this one's off.
    public static StartupState State(string? runValue, byte[]? approved, string exe) =>
        IsOtherApp(runValue, exe) ? StartupState.Off : State(runValue, approved);

    /// Task Manager writes 12 bytes: the first is even when the app may start and odd
    /// when you turned it off (the rest is when). No value means it may start.
    public static bool ApprovedOff(byte[]? approved) => approved is { Length: > 0 } && (approved[0] & 1) == 1;

    public static StartupState State(string? runValue, byte[]? approved) =>
        string.IsNullOrWhiteSpace(runValue) ? StartupState.Off
        : ApprovedOff(approved) ? StartupState.TurnedOffInTaskManager
        : StartupState.On;

    /// A Run value to write again at launch: it's ours, but the exe it names is gone
    /// (the app was installed somewhere else). A value naming another exe that exists
    /// is left alone, so a dev build never takes over the installed one, and so is the
    /// other app's, there or gone.
    public static bool NeedsRepair(string? runValue, string exe, Func<string, bool> exists) =>
        Exe(runValue) is { } named && !IsOtherApp(runValue, exe)
        && !string.Equals(named, exe, StringComparison.OrdinalIgnoreCase) && !exists(named);
}
