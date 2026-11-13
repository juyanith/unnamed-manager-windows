using Xunit;

namespace UnnamedManager.Core.Tests;

// S-054: Run at startup.
public class StartupEntryTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\Programs\UnnamedManager\UnnamedManager.exe";

    [Fact]
    public void TheRunValueQuotesThePath() => Assert.Equal($"\"{Exe}\"", StartupEntry.Command(Exe));

    [Theory]
    [InlineData(@"""C:\A B\x.exe"" --flag", @"C:\A B\x.exe")]
    [InlineData(@"C:\x.exe --flag", @"C:\x.exe")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void TheExeIsReadBackWithoutQuotesOrArguments(string? command, string? exe) => Assert.Equal(exe, StartupEntry.Exe(command));

    [Fact]
    public void TaskManagerTurnsItOffWithAnOddFirstByte()
    {
        Assert.Equal(StartupState.Off, StartupEntry.State(null, null));
        Assert.Equal(StartupState.On, StartupEntry.State(Exe, null));
        Assert.Equal(StartupState.On, StartupEntry.State(Exe, [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(StartupState.On, StartupEntry.State(Exe, [6, 0, 0, 0]));
        Assert.Equal(StartupState.TurnedOffInTaskManager, StartupEntry.State(Exe, [3, 0, 0, 0, 0x10, 0x20, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(StartupState.Off, StartupEntry.State(null, [3, 0]));
    }

    [Fact]
    public void OnlyAValueNamingAMissingExeIsRepaired()
    {
        Assert.True(StartupEntry.NeedsRepair(@"""C:\old\UnnamedManager.exe""", Exe, _ => false));
        Assert.False(StartupEntry.NeedsRepair(@"""C:\dev\UnnamedManager.exe""", Exe, _ => true));
        Assert.False(StartupEntry.NeedsRepair(StartupEntry.Command(Exe), Exe, _ => false));
        Assert.False(StartupEntry.NeedsRepair(null, Exe, _ => false));
    }
}
