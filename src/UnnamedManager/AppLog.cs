using UnnamedManager.Core;

namespace UnnamedManager;

/// The app's own log (S-016): failures and other things worth knowing later, one file
/// a day in AppPaths.Logs, the last 7 kept. Writing never throws: a log that can't be
/// written isn't worth stopping an action for.
public static class AppLog
{
    private static readonly object Lock = new();
    private static DateTime _pruned;

    /// Today's file.
    public static string TodayFile => Path.Combine(AppPaths.Logs, LogFiles.FileName(DateTime.Now));

    public static void Write(string text)
    {
        var now = DateTime.Now;
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Logs);
                File.AppendAllText(Path.Combine(AppPaths.Logs, LogFiles.FileName(now)), LogFiles.Line(now, text) + Environment.NewLine);
                if (_pruned.Date != now.Date) Prune(now);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// Deletes day files past the last 7.
    public static void Prune(DateTime now)
    {
        _pruned = now;
        var names = Directory.EnumerateFiles(AppPaths.Logs).Select(Path.GetFileName).OfType<string>();
        foreach (var name in LogFiles.ToDelete(names)) File.Delete(Path.Combine(AppPaths.Logs, name));
    }
}
