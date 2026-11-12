using Xunit;

namespace UnnamedManager.Core.Tests;

public class LogFilesTests
{
    [Fact]
    public void OneFileADayNamedByItsDate() =>
        Assert.Equal("unnamed-manager-2026-11-02.log", LogFiles.FileName(new DateTime(2026, 11, 2, 23, 59, 0)));

    [Fact]
    public void ALineStartsWithItsTime() =>
        Assert.Equal("14:02:09 ACTION failed", LogFiles.Line(new DateTime(2026, 11, 12, 14, 2, 9), "ACTION failed"));

    [Fact]
    public void OnlyTheLastSevenDaysAreKept()
    {
        var names = Enumerable.Range(1, 10).Select(d => LogFiles.FileName(new DateTime(2026, 11, d)))
            .Append("notes.txt").Append("unnamed-manager-latest.log").Reverse().ToList();
        Assert.Equal(["unnamed-manager-2026-11-03.log", "unnamed-manager-2026-11-02.log", "unnamed-manager-2026-11-01.log"], LogFiles.ToDelete(names));
        Assert.Empty(LogFiles.ToDelete(names.Take(5)));
    }
}
