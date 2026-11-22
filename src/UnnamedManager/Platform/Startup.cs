using Microsoft.Win32;
using UnnamedManager.Core;

namespace UnnamedManager.Platform;

/// Run at startup (S-054): the `HKCU\…\Run` value, and Task Manager's switch for it in
/// `StartupApproved\Run`. With `APP_STORE` set (self-test, snapshots, dev runs) it uses
/// keys of its own under `HKCU\Software\UnnamedManager-Test`, never the real ones.
public static class Startup
{
    private const string RealRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RealApproved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string TestRoot = @"Software\UnnamedManager-Test";

    private static string RunKey => AppPaths.IsOverridden ? TestRoot + @"\Run" : RealRun;
    public static string ApprovedKey => AppPaths.IsOverridden ? TestRoot + @"\StartupApproved\Run" : RealApproved;

    private static string Exe => Environment.ProcessPath!;

    public static string? RunValue
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(StartupEntry.Name) as string;
        }
    }

    private static byte[]? Approved
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return key?.GetValue(StartupEntry.Name) as byte[];
        }
    }

    public static StartupState State => StartupEntry.State(RunValue, Approved, Exe);

    /// On writes the Run value and clears Task Manager's "off", so the switch means what
    /// it says; off removes the value, unless it's the other app's (S-109).
    public static void Set(bool on)
    {
        try
        {
            if (!on && StartupEntry.IsOtherApp(RunValue, Exe)) return;
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) run.SetValue(StartupEntry.Name, StartupEntry.Command(Exe));
                else run.DeleteValue(StartupEntry.Name, throwOnMissingValue: false);
            }
            if (on)
            {
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(StartupEntry.Name, throwOnMissingValue: false);
            }
            AppLog.Write($"Run at startup turned {(on ? "on" : "off")}");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            AppLog.Write($"Run at startup couldn't change: {e.Message}");
        }
    }

    /// At launch: a Run value that names an exe that's gone now names this one.
    public static void RepairIfMoved()
    {
        if (StartupEntry.NeedsRepair(RunValue, Exe, File.Exists)) Set(true);
    }

    /// Removes the test keys (self-test only).
    public static void ClearTestKeys() => Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);
}
