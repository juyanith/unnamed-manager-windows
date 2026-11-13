using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UnnamedManager.Platform;

/// Windows' Office shortcuts (S-057): Hyper alone opens the Microsoft 365 app, and Hyper
/// with W, X, P, O, T, D, N, Y or L an Office app or site, all through the `ms-officeapp`
/// protocol. Turning them off gives that protocol, for this user only, a handler that
/// does nothing (`rundll32` with no arguments); turning them on removes it again. With
/// `APP_STORE` set it uses a key of its own under `HKCU\Software\UnnamedManager-Test`.
public static partial class OfficeShortcuts
{
    private const string Handler = "rundll32";

    private static string ProtocolKey => (AppPaths.IsOverridden ? Startup.TestRoot : @"Software") + @"\Classes\ms-officeapp";

    private static string CommandKey => ProtocolKey + @"\Shell\Open\Command";

    /// On: Windows' own handler. Off: this app's handler that does nothing. Null: the
    /// user set a handler of their own, which the app leaves alone.
    public static bool? On
    {
        get
        {
            using var protocol = Registry.CurrentUser.OpenSubKey(ProtocolKey);
            if (protocol is null) return true;
            using var command = Registry.CurrentUser.OpenSubKey(CommandKey);
            return command?.GetValue("") as string == Handler ? false : null;
        }
    }

    public static void Set(bool on)
    {
        if (On is null || On == on) return;
        try
        {
            if (on) Registry.CurrentUser.DeleteSubKeyTree(ProtocolKey, throwOnMissingSubKey: false);
            else
            {
                using (var protocol = Registry.CurrentUser.CreateSubKey(ProtocolKey))
                {
                    protocol.SetValue("", "URL:ms-officeapp");
                    protocol.SetValue("URL Protocol", "");
                }
                using var command = Registry.CurrentUser.CreateSubKey(CommandKey);
                command.SetValue("", Handler);
            }
            // Tells the shell its protocol handlers changed.
            if (!AppPaths.IsOverridden) SHChangeNotify(SHCNE_ASSOCCHANGED, 0, 0, 0);
            AppLog.Write(on ? "Office shortcuts turned on" : "Office shortcuts turned off");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            AppLog.Write($"Office shortcuts couldn't change: {e.Message}");
        }
    }

    /// Removes the test key (self-test only).
    public static void ClearTestKey()
    {
        if (AppPaths.IsOverridden) Registry.CurrentUser.DeleteSubKeyTree(ProtocolKey, throwOnMissingSubKey: false);
    }

    /// Self-test only: a handler of the user's own.
    public static void SetCustomForTest(string command)
    {
        if (!AppPaths.IsOverridden) return;
        using var key = Registry.CurrentUser.CreateSubKey(CommandKey);
        key.SetValue("", command);
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
