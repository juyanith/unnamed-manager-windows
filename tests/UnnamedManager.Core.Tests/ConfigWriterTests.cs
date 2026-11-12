using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-048: the config is written back with a stable layout, so edits make small diffs.
public class ConfigWriterTests
{
    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        var valid = Path.Combine(SpecFixtureTests.SpecFolder, "fixtures", "valid");
        foreach (var file in Directory.GetFiles(valid, "*.json").Order())
            if (ConfigLoader.LoadFile(file).IsValid) data.Add(file);
        data.Add(Path.Combine(SpecFixtureTests.SpecFolder, "..", "tools", "sample-config.json"));
        return data;
    }

    private static Config.Config Read(string json) => new ConfigReader(ActionRegistry.Standard).Read(json);

    [Theory]
    [MemberData(nameof(Files))]
    public void AConfigWrittenBackReadsTheSameAndKeepsItsOrder(string file)
    {
        var text = File.ReadAllText(file);
        var original = Read(text);
        var written = ConfigWriter.Write(original);
        Assert.True(ConfigLoader.Load(written).IsValid, written);
        var again = Read(written);
        Assert.Equal(original.Entries, again.Entries);
        Assert.Equal(original.Actions.Select(a => a.Key), again.Actions.Select(a => a.Key));
        Assert.Equal(original.Contexts.Select(c => $"{c.Id}={string.Join(",", c.Apps)}"), again.Contexts.Select(c => $"{c.Id}={string.Join(",", c.Apps)}"));
        Assert.Equal(original.Runtime, again.Runtime);
        Assert.Equal(original.UI, again.UI);
        Assert.Equal(original.Windows, again.Windows);
        Assert.Equal(written, ConfigWriter.Write(again));
    }

    [Fact]
    public void TheStarterIsWrittenExactlyAsItShips() =>
        Assert.Equal(ConfigLoader.StarterJson, ConfigWriter.Write(Read(ConfigLoader.StarterJson)));

    [Fact]
    public void ChangingOneTitleChangesOneLine()
    {
        var config = Read(ConfigLoader.StarterJson);
        var entries = config.Entries.ToList();
        entries[2] = entries[2] with { Title = "Windows Terminal" };
        var before = ConfigWriter.Write(config).Split('\n');
        var after = ConfigWriter.Write(config with { Entries = entries }).Split('\n');
        Assert.Equal(before.Length, after.Length);
        Assert.Single(before.Zip(after), p => p.First != p.Second);
    }

    [Fact]
    public void DefaultsAreLeftOutAndSettingsKeepTheirOrder()
    {
        var config = Read("""{ "version": 1, "runtime": { "timeoutMs": 900, "cancelKey": "Q" }, "ui": { "theme": "system" }, "entries": [] }""");
        var text = ConfigWriter.Write(config);
        Assert.Contains("""  "runtime": { "cancelKey": "Q", "timeoutMs": 900 },""", text);
        Assert.DoesNotContain("\"ui\"", text);
    }

    [Fact]
    public void TextKeepsSlashesAndNonAsciiAsWritten() =>
        Assert.Equal("""
            "C:\\Tools/é \"x\""
            """, ConfigWriter.Quote("""
            C:\Tools/é "x"
            """));
}
