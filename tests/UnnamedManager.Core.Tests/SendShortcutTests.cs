using System.Text.Json.Nodes;
using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using Xunit;

namespace UnnamedManager.Core.Tests;

public class SendShortcutTests
{
    private static ActionPlan.Send Plan(string keys)
    {
        var action = ConfigLoader.Load($$"""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ],
              "actions": { "a": { "type": "send", "keys": "{{keys}}" } } }
            """).Loaded!.Config.Action("a")!;
        return (ActionPlan.Send)ActionRegistry.Standard.Family("send")!.Plan(action, PlanContext.Current)!;
    }

    private static string Events(string keys, params int[] held) =>
        string.Join(" ", ShortcutEvents.Build(Plan(keys).Chords, held));

    [Fact]
    public void SendParsesOneOrMoreChordsAndPrimaryIsCtrl()
    {
        var plan = Plan("Primary+S Primary+Shift+S F5");
        Assert.Equal(["Ctrl+S", "Ctrl+Shift+S", "F5"], plan.Chords.Select(c => c.ToString()));
    }

    [Fact]
    public void ItWritesTheChordsAsTheAppWritesKeyPaths()
    {
        var action = new SendShortcutAction(KeyPath.Parse("primary+alt+shift+t ctrl+f")!);
        Assert.Equal("Meh+T Ctrl+F", ActionRegistry.Standard.Family("send")!.Write(action)[0].Value.GetValue<string>());
    }

    [Fact]
    public void TheSchemasKeyPatternIsTheSpecs()
    {
        var spec = JsonNode.Parse(File.ReadAllText(Path.Combine(SpecFixtureTests.SpecFolder, "config.schema.json")))!;
        var specPattern = spec["properties"]!["entries"]!["items"]!["properties"]!["keys"]!["pattern"]!.GetValue<string>();
        Assert.Equal(specPattern, KeyPattern.Path);
    }

    [Fact]
    public void AChordIsItsModifiersDownTheKeyThenItsModifiersUp() =>
        Assert.Equal("vkA2↓ sc21↓ sc21↑ vkA2↑", Events("Ctrl+F"));

    [Fact]
    public void HeldLeaderModifiersGoUpFirstSoTheAppSeesOnlyTheChordsOwn() =>
        // Hyper still held from the leader: Win and Alt are masked before they go up.
        Assert.Equal("vkE8↓ vkE8↑ vkA2↑ vkA4↑ vkA0↑ vk5B↑ vkA2↓ sc21↓ sc21↑ vkA2↑",
            Events("Ctrl+F", ShortcutEvents.LCtrl, ShortcutEvents.LAlt, ShortcutEvents.LShift, ShortcutEvents.LWin));

    [Fact]
    public void AChordWithWinSendsTheMaskingKeySoStartDoesntOpen() =>
        Assert.Equal("vk5B↓ sc20↓ sc20↑ vkE8↓ vkE8↑ vk5B↑", Events("Cmd+D"));

    [Fact]
    public void KeysGoByScanCodeWithTheExtendedFlag() =>
        Assert.Equal("vkA2↓ sc4Be↓ sc4Be↑ vkA2↑ sc1E↓ sc1E↑", Events("Ctrl+Left A"));
}
