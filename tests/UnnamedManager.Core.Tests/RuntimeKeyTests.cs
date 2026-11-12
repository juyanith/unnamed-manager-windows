using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using Xunit;
using static UnnamedManager.Core.Keyboard.SequenceEffect;

namespace UnnamedManager.Core.Tests;

/// S-037: Choose my own cancel and back keys.
public class RuntimeKeyTests
{
    private static LoadResult Load(string runtime, string extra = "") => ConfigLoader.Load($$"""
        { "version": 1, "runtime": { {{runtime}} }, "entries": [
            { "keys": "Ctrl+Alt+Space", "title": "Commands" },
            { "keys": "Ctrl+Alt+Space A", "title": "Applications" },
            { "keys": "Ctrl+Alt+Space A T", "title": "Terminal", "action": "x" } {{extra}}
          ], "actions": { "x": { "type": "noop" } } }
        """);

    private static SequenceEngine InApplications(LoadedConfig config)
    {
        var engine = new SequenceEngine(config);
        engine.Handle(KeyEvent.Down(KeyName.Space, Modifiers.Ctrl | Modifiers.Alt));
        engine.ModifiersChanged(Modifiers.None);
        engine.Handle(KeyEvent.Down(KeyName.A));
        return engine;
    }

    [Fact]
    public void TheSetCancelAndBackKeysWork()
    {
        var config = Load("\"cancelKey\": \"Q\", \"backKey\": \"Left\"").Loaded!;
        var engine = InApplications(config);
        Assert.Equal(["Commands"], Assert.IsType<ShowMenu>(engine.Handle(KeyEvent.Down(KeyName.Left)).Effect).Path);
        Assert.IsType<Unbound>(engine.Handle(KeyEvent.Down(KeyName.Escape)).Effect);
        Assert.IsType<Unbound>(engine.Handle(KeyEvent.Down(KeyName.Backspace)).Effect);
        Assert.IsType<Cancelled>(engine.Handle(KeyEvent.Down(KeyName.Q)).Effect);
        Assert.False(engine.IsActive);
    }

    [Fact]
    public void AnEmptyBackKeyTurnsBackOff()
    {
        var config = Load("\"backKey\": \"\"").Loaded!;
        Assert.Null(config.BackKey);
        var engine = InApplications(config);
        Assert.IsType<Unbound>(engine.Handle(KeyEvent.Down(KeyName.Backspace)).Effect);
        Assert.Equal(["Commands", "Applications"], engine.Path);
        Assert.IsType<Cancelled>(engine.Handle(KeyEvent.Down(KeyName.Escape)).Effect);
    }

    [Theory]
    [InlineData("\"cancelKey\": \"Q\"", "Q")]
    [InlineData("\"backKey\": \"Q\"", "Q")]
    [InlineData("", "Escape")]
    [InlineData("", "Backspace")]
    public void AStepOnEitherKeyFailsValidation(string runtime, string key)
    {
        var result = Load(runtime, $$""", { "keys": "Ctrl+Alt+Space {{key}}", "title": "Clash", "action": "x" }""");
        Assert.Null(result.Loaded);
        Assert.Contains(result.Errors, e => e.Rule == Rule.StepIsRuntimeKey);
    }

    [Fact]
    public void WithBackOffBackspaceIsAnOrdinaryStepKey() =>
        Assert.NotNull(Load("\"backKey\": \"\"", """, { "keys": "Ctrl+Alt+Space Backspace", "title": "Erase", "action": "x" }""").Loaded);

    [Fact]
    public void TheSameKeyForBothOrAKeyThatIsntOneIsAnError()
    {
        Assert.Contains(Load("\"cancelKey\": \"Q\", \"backKey\": \"Q\"").Errors, e => e.Rule == Rule.SameCancelBack);
        Assert.Contains(Load("\"cancelKey\": \"Esc2\"").Errors, e => e.Rule == Rule.BadRuntimeKey);
    }
}
