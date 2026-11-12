using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UnnamedManager.Tray;

/// Open config file and Open file location (S-008).
public static partial class ConfigFiles
{
    /// The program set for .json files, or Notepad when there is none.
    public static string Editor()
    {
        Span<char> buffer = stackalloc char[1024];
        var size = (uint)buffer.Length;
        if (AssocQueryStringW(ASSOCF_NOTRUNCATE, ASSOCSTR_EXECUTABLE, ".json", "open", buffer, ref size) == 0 && size > 1)
        {
            var exe = new string(buffer[..((int)size - 1)]);
            if (File.Exists(exe)) return exe;
        }
        return Path.Combine(Environment.SystemDirectory, "notepad.exe");
    }

    public static void OpenFile(string path)
    {
        var editor = Editor();
        if (Path.GetFileName(editor).Equals("notepad.exe", StringComparison.OrdinalIgnoreCase))
            Process.Start(new ProcessStartInfo(editor, $"\"{path}\"") { UseShellExecute = true })?.Dispose();
        else
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    /// File Explorer with the config file selected.
    public static ProcessStartInfo LocationCommand(string path) =>
        new("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true };

    public static void OpenLocation(string path) => Process.Start(LocationCommand(path))?.Dispose();

    /// Open log folder (S-016): the log folder in File Explorer, made first if needed.
    public static void OpenLogFolder()
    {
        Directory.CreateDirectory(AppPaths.Logs);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Logs}\"") { UseShellExecute = true })?.Dispose();
    }

    private const uint ASSOCF_NOTRUNCATE = 0x20, ASSOCSTR_EXECUTABLE = 2;

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AssocQueryStringW(uint flags, uint str, string assoc, string? extra, Span<char> output, ref uint size);
}
