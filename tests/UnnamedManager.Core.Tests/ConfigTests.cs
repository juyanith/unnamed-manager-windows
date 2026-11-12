using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

public class ConfigTests
{
    private static LoadedConfig LoadValid(string json)
    {
        var result = ConfigLoader.Load(json);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        return result.Loaded!;
    }

    [Fact]
    public void OneLeaderWithOneBindingLoadsWithEveryDefault()
    {
        var loaded = LoadValid("""
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Commands" },
              { "keys": "Hyper+Space E", "title": "Editor", "action": "e" } ],
              "actions": { "e": { "type": "application", "exe": "Code.exe" } } }
            """);
        var c = loaded.Config;
        Assert.Equal(new RuntimeSettings(), c.Runtime);
        Assert.Equal(new UISettings(), c.UI);
        Assert.Equal(8, c.Windows.Gap);
        Assert.Equal(KeyName.Escape, loaded.CancelKey);
        Assert.Equal(KeyName.Backspace, loaded.BackKey);
        Assert.Empty(loaded.Warnings);
        var leader = Assert.Single(loaded.Leaders);
        Assert.Equal("Hyper+Space", leader.Path.ToString());
        Assert.Equal("Editor", Assert.Single(leader.Children).Title);
        Assert.Equal(new UseApplicationAction("Code.exe", null), c.Action("e"));
    }

    [Fact]
    public void EntriesAndActionsKeepFileOrder()
    {
        var loaded = LoadValid("""
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Commands" },
              { "keys": "Hyper+Space Z", "title": "Zed", "action": "z" },
              { "keys": "Hyper+Space A", "title": "Apps" },
              { "keys": "Hyper+Space A M", "title": "Mail", "action": "m" },
              { "keys": "Hyper+Space A B", "title": "Browser", "action": "b" },
              { "keys": "Ctrl+Alt+K", "title": "Direct", "action": "z" } ],
              "actions": { "z": { "type": "noop" }, "m": { "type": "noop" }, "b": { "type": "help" } } }
            """);
        Assert.Equal(["Hyper+Space", "Ctrl+Alt+K"], loaded.Leaders.Select(l => l.Path.ToString()));
        Assert.Equal(["Zed", "Apps"], loaded.Leaders[0].Children.Select(c => c.Title));
        Assert.Equal(["Mail", "Browser"], loaded.Leaders[0].Children[1].Children.Select(c => c.Title));
        Assert.Equal(["z", "m", "b"], loaded.Config.Actions.Select(a => a.Key));
    }

    [Fact]
    public void ErrorsNameTheEntryAndTheRule()
    {
        var result = ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Commands" },
              { "keys": "Hyper+Space Q", "title": "Quit", "action": "open-foo" } ] }
            """);
        var issue = Assert.Single(result.Errors);
        Assert.Equal(Rule.UndefinedAction, issue.Rule);
        Assert.Equal("Hyper+Space Q", issue.Subject);
        Assert.Equal("Binding “Hyper+Space Q” uses action “open-foo”, which isn't defined.", issue.Message);
    }

    [Fact]
    public void UnusedActionsAndContextsAreWarningsOnly()
    {
        var loaded = LoadValid("""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "k" } ],
              "actions": { "k": { "type": "noop" }, "spare": { "type": "noop" } },
              "contexts": [ { "id": "lonely", "apps": ["Code.exe"] } ] }
            """);
        Assert.Equal([Rule.UnusedAction, Rule.UnusedContext], loaded.Warnings.Select(w => w.Rule));
        Assert.All(loaded.Warnings, w => Assert.Equal(Severity.Warning, w.Severity));
    }

    [Theory]
    [InlineData("hyper+space a", "Hyper+Space A")]
    [InlineData("Ctrl+Alt+Shift+T", "Meh+T")]
    [InlineData("Primary+F", "Ctrl+F")]
    [InlineData("Command+Ctrl+Option+Left", "CAG+Left")]
    [InlineData("Meh+Cmd+pageup", "Hyper+PageUp")]
    public void KeyPathsReadInAnyCaseAndWriteCanonically(string text, string canonical) =>
        Assert.Equal(canonical, KeyPath.Parse(text)!.ToString());

    [Theory]
    [InlineData("⌘+Hyperr")]
    [InlineData("Hyper+Spaec")]
    [InlineData("Super+A")]
    [InlineData("")]
    public void UnknownNamesDoNotParse(string text) => Assert.Null(KeyPath.Parse(text));

    [Theory]
    [InlineData("U+E756", true)]
    [InlineData("u+e71d", true)]
    [InlineData(@"C:\Windows\System32\cmd.exe", true)]
    [InlineData("~/Pictures/x.png", true)]
    [InlineData(@"\\server\share\app.ico", true)]
    [InlineData("terminal", false)]
    [InlineData("U+E7", false)]
    public void WindowsIconsAreCodePointsOrPaths(string icon, bool valid) =>
        Assert.Equal(valid, ConfigValidator.IsWindowsIcon(icon));

    [Fact]
    public void TheSnapshotsSampleConfigLoadsWithNoWarnings()
    {
        var path = Path.Combine(SpecFixtureTests.SpecFolder, "..", "tools", "sample-config.json");
        var result = ConfigLoader.LoadFile(path);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Empty(result.Loaded!.Warnings);
        Assert.Equal(14, result.Loaded.Leaders[0].Children.Single(c => c.Title == "Websites").Children.Count);
    }

    [Fact]
    public void TheStarterConfigLoads()
    {
        var loaded = LoadValid(ConfigLoader.StarterJson);
        var leader = Assert.Single(loaded.Leaders);
        Assert.Equal("Hyper+Space", leader.Path.ToString());
        Assert.Equal("Commands", leader.Title);
        Assert.Equal(["F File Explorer", "T Terminal"], leader.Children.Select(c => $"{c.Path.LastStep!.Value} {c.Title}"));
        Assert.Equal(new UseApplicationAction("explorer.exe", null), loaded.Config.Action("open-explorer"));
        Assert.Equal(new UseApplicationAction(null, "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"), loaded.Config.Action("open-terminal"));
        Assert.Equal("./config.schema.json", loaded.Config.Schema);
    }

    [Fact]
    public void TheStarterIsWrittenWithTheSchemaAndNeverOverAFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "um-starter-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "config.json");
        try
        {
            Assert.True(ConfigLoader.WriteStarterIfMissing(path));
            Assert.Equal(ConfigLoader.StarterJson, File.ReadAllText(path));
            Assert.Contains("\"title\"", File.ReadAllText(Path.Combine(dir, ConfigLoader.SchemaFileName)));

            File.WriteAllText(path, "{ mine }");
            Assert.False(ConfigLoader.WriteStarterIfMissing(path));
            Assert.Equal("{ mine }", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
