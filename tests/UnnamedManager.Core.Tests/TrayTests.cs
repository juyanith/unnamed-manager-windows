using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

public class TrayTests
{
    private const string Valid = """{ "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "k" } ], "actions": { "k": { "type": "noop" } } }""";

    [Fact]
    public void AReloadWithErrorsKeepsTheLastValidConfigRunning()
    {
        var state = new ConfigState();
        var first = DateTimeOffset.Parse("2026-11-12T14:02:00Z");
        Assert.True(state.Apply(ConfigLoader.Load(Valid), first));
        var running = state.Current;

        Assert.False(state.Apply(ConfigLoader.Load("""{ "version": 1, "entries": [] }"""), first.AddMinutes(5)));
        Assert.Same(running, state.Current);
        Assert.Equal(first, state.LoadedAt);
        Assert.True(state.HasErrors);
        Assert.Equal(Rule.NoEntries, state.Errors[0].Rule);

        Assert.True(state.Apply(ConfigLoader.Load(Valid), first.AddMinutes(9)));
        Assert.False(state.HasErrors);
        Assert.NotSame(running, state.Current);
    }

    [Fact]
    public void TheErrorSummaryIsTheFirstErrorAndHowManyMore()
    {
        var state = new ConfigState();
        state.Apply(ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Commands" },
              { "keys": "Hyper+Space Q", "title": "Q", "action": "a" },
              { "keys": "Hyper+Space W", "title": "W", "action": "b" },
              { "keys": "Hyper+Space E", "title": "E", "action": "c" } ] }
            """), DateTimeOffset.Now);
        var (first, more) = state.ErrorSummary!.Value;
        Assert.Equal("Binding “Hyper+Space Q” uses action “a”, which isn't defined.", first);
        Assert.Equal(2, more);
        Assert.Null(new ConfigState().ErrorSummary);
    }

    [Theory]
    [InlineData("0.1.3", "Version 0.1.3")]
    [InlineData("0.1.0+main.ee74e2d", "Version 0.1 (main, ee74e2d)")]
    [InlineData("0.1.0+main.ee74e2d.modified", "Version 0.1 (main, ee74e2d, modified)")]
    [InlineData("0.1.0+release.0.2.abc1234", "Version 0.1 (release.0.2, abc1234)")]
    [InlineData("0.1.0+abc1234", "Version 0.1.0")]
    [InlineData(null, "Version unknown")]
    public void TheVersionLineSaysWhichBuildRuns(string? informational, string line) =>
        Assert.Equal(line, AppVersion.Line(informational));

    /// S-105: WPF can't fall behind, since neither app has a version of its own.
    [Fact]
    public void BothAppsTakeTheirVersionFromOnePlace()
    {
        var root = Path.GetDirectoryName(SpecFixtureTests.SpecFolder)!;
        Assert.Contains("<Version>", File.ReadAllText(Path.Combine(root, "Directory.Build.props")));
        foreach (var app in new[] { "UnnamedManager", "UnnamedManager.Wpf" })
            Assert.DoesNotContain("<Version>", File.ReadAllText(Path.Combine(root, "src", app, $"{app}.csproj")));
    }
}
