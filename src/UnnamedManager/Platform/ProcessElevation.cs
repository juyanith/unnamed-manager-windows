using System.Runtime.InteropServices;

namespace UnnamedManager.Platform;

/// Whether a window belongs to an app running as administrator while this app isn't
/// (S-056): then Windows keeps this app from moving it or typing into it.
public static partial class ProcessElevation
{
    /// This app runs as administrator: nothing is above it.
    public static readonly bool Self = IsElevated((uint)Environment.ProcessId) == true;

    public static bool Above(nint hwnd)
    {
        if (Self || hwnd == 0) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        // A token this app may not even read is one above it.
        return IsElevated(pid) ?? true;
    }

    /// Null when the process's token can't be read.
    private static bool? IsElevated(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return null;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return null;
            try
            {
                return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(uint), out _) ? elevated != 0 : null;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int infoClass, out uint info, int length, out int returned);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
