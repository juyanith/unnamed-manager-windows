using System.Globalization;

namespace UnnamedManager.Core;

/// The app's log (S-016): one file a day in the log folder, the last 7 kept.
public static class LogFiles
{
    public const int DaysKept = 7;

    private const string Prefix = "unnamed-manager-";

    /// "unnamed-manager-2026-11-12.log": names sort by date.
    public static string FileName(DateTime day) => $"{Prefix}{day:yyyy-MM-dd}.log";

    /// One line: the time, then the text.
    public static string Line(DateTime at, string text) => $"{at.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {text}";

    /// The day files to delete so only the newest `DaysKept` stay. Other files are left alone.
    public static IEnumerable<string> ToDelete(IEnumerable<string> fileNames) => fileNames
        .Where(IsDayFile)
        .OrderByDescending(n => n, StringComparer.Ordinal)
        .Skip(DaysKept);

    private static bool IsDayFile(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(".log", StringComparison.Ordinal)
        && DateTime.TryParseExact(name[Prefix.Length..^4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
