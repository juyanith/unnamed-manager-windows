using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using Xunit;
using static UnnamedManager.Core.Keyboard.SequenceEffect;

namespace UnnamedManager.Core.Tests;

public class SequenceEngineTests
{
    private const Modifiers Hyper = Modifiers.Hyper;

    private static LoadedConfig Config(string entries, string actions = "") => ConfigLoader.Load($$"""
        { "version": 1, "entries": [ {{entries}} ],
          "actions": { "x": { "type": "noop" } {{actions}} } }
        """).Loaded ?? throw new InvalidOperationException("config didn't load");

    private static readonly LoadedConfig Apps = Config("""
        { "keys": "Hyper+Space", "title": "Commands" },
        { "keys": "Hyper+Space A", "title": "Applications" },
        { "keys": "Hyper+Space A T", "title": "Terminal", "action": "x" },
        { "keys": "Hyper+Space A E", "title": "Editor", "action": "x" },
        { "keys": "Hyper+Space F", "title": "Find", "action": "x" },
        { "keys": "Ctrl+Alt+K", "title": "Direct", "action": "x" }
        """);

    [Fact]
    public void WhenIdleOnlyLeaderChordsAreConsumed()
    {
        var engine = new SequenceEngine(Apps);
        Assert.False(engine.Handle(KeyEvent.Down(KeyName.Space)).Consume);
        Assert.False(engine.Handle(KeyEvent.Down(KeyName.Space, Modifiers.Ctrl)).Consume);
        Assert.False(engine.Handle(KeyEvent.Down(KeyName.A, Hyper)).Consume);
        Assert.False(engine.Handle(KeyEvent.Up(KeyName.A)).Consume);
        Assert.False(engine.IsActive);

        var start = engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        Assert.True(start.Consume);
        Assert.Equal(["Commands"], Assert.IsType<ShowMenu>(start.Effect).Path);
        Assert.True(engine.IsActive);
    }

    [Fact]
    public void ADirectBindingRunsAtOnceWithNoSequence()
    {
        var engine = new SequenceEngine(Apps);
        var output = engine.Handle(KeyEvent.Down(KeyName.K, Modifiers.Ctrl | Modifiers.Alt));
        Assert.Equal("Direct", Assert.IsType<Run>(output.Effect).Binding.Title);
        Assert.False(engine.IsActive);
        Assert.True(engine.Handle(KeyEvent.Up(KeyName.K)).Consume);
    }

    [Fact]
    public void InASequenceEveryKeyDownRepeatAndKeyUpIsConsumed()
    {
        var engine = new SequenceEngine(Apps);
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        Assert.True(engine.Handle(KeyEvent.Down(KeyName.Space, Hyper, repeat: true)).Consume);
        Assert.True(engine.Handle(KeyEvent.Up(KeyName.Space)).Consume);
        var unbound = engine.Handle(KeyEvent.Down(KeyName.Q));
        Assert.True(unbound.Consume);
        Assert.IsType<Unbound>(unbound.Effect);
        Assert.True(engine.Handle(KeyEvent.Down(KeyName.Q, repeat: true)).Consume);
        Assert.True(engine.Handle(KeyEvent.Up(KeyName.Q)).Consume);
        Assert.True(engine.IsActive);
        // A key with no name is still consumed, with its key-up.
        Assert.True(engine.Handle(new KeyEvent(0x4E, true, false, Modifiers.None)).Consume);
        Assert.True(engine.Handle(new KeyEvent(0x4E, false, false, Modifiers.None)).Consume);
    }

    [Fact]
    public void TheKeyUpOfTheBindingThatEndedTheSequenceIsConsumed()
    {
        var engine = new SequenceEngine(Apps);
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        var run = engine.Handle(KeyEvent.Down(KeyName.F));
        Assert.Equal("Find", Assert.IsType<Run>(run.Effect).Binding.Title);
        Assert.False(engine.IsActive);
        Assert.True(engine.Handle(KeyEvent.Up(KeyName.F)).Consume);
        Assert.False(engine.Handle(KeyEvent.Up(KeyName.F)).Consume);
        Assert.False(engine.Handle(KeyEvent.Down(KeyName.F)).Consume);
    }

    [Fact]
    public void AChildMenuOpensAndAChildBindingEndsTheSequence()
    {
        var engine = new SequenceEngine(Apps);
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        var menu = engine.Handle(KeyEvent.Down(KeyName.A));
        Assert.Equal(["Commands", "Applications"], Assert.IsType<ShowMenu>(menu.Effect).Path);
        Assert.Equal(["Terminal", "Editor"], ((ShowMenu)menu.Effect!).Menu.Children.Select(c => c.Title));
        var run = engine.Handle(KeyEvent.Down(KeyName.T));
        Assert.Equal("Hyper+Space A T", Assert.IsType<Run>(run.Effect).Binding.Path.ToString());
        Assert.False(engine.IsActive);
    }

    [Fact]
    public void LeaderModifiersStillHeldAreIgnoredOnStepKeysButOthersDontMatch()
    {
        var engine = new SequenceEngine(Apps);
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        Assert.IsType<ShowMenu>(engine.Handle(KeyEvent.Down(KeyName.A, Hyper)).Effect);
        // Let go of Shift: Shift now counts as a modifier of its own.
        engine.ModifiersChanged(Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win);
        Assert.IsType<Unbound>(engine.Handle(KeyEvent.Down(KeyName.T, Hyper)).Effect);
        Assert.IsType<Run>(engine.Handle(KeyEvent.Down(KeyName.T, Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win)).Effect);
    }

    [Fact]
    public void AModifierThatIsntTheLeadersMakesTheStepNotMatch()
    {
        var engine = new SequenceEngine(Apps);
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        engine.ModifiersChanged(Modifiers.None);
        Assert.IsType<Unbound>(engine.Handle(KeyEvent.Down(KeyName.F, Modifiers.Shift)).Effect);
        Assert.True(engine.IsActive);
        Assert.IsType<Run>(engine.Handle(KeyEvent.Down(KeyName.F)).Effect);
    }

    [Fact]
    public void MenusNestWithoutLimit()
    {
        var entries = new List<string> { """{ "keys": "Hyper+Space", "title": "L0" }""" };
        var path = "Hyper+Space";
        for (var level = 1; level <= 6; level++)
        {
            path += " M";
            entries.Add($$"""{ "keys": "{{path}}", "title": "L{{level}}" }""");
        }
        entries.Add($$"""{ "keys": "{{path}} X", "title": "Deep", "action": "x" }""");
        var engine = new SequenceEngine(Config(string.Join(",", entries)));
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        engine.ModifiersChanged(Modifiers.None);
        for (var level = 1; level <= 6; level++)
            Assert.Equal($"L{level}", Assert.IsType<ShowMenu>(engine.Handle(KeyEvent.Down(KeyName.M)).Effect).Menu.Title);
        Assert.Equal(7, engine.Path.Count);
        Assert.Equal("Deep", Assert.IsType<Run>(engine.Handle(KeyEvent.Down(KeyName.X)).Effect).Binding.Title);
    }

    [Fact]
    public void CancelEndsFromAnyDepthAndBackGoesUp()
    {
        var engine = new SequenceEngine(Apps);
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        engine.ModifiersChanged(Modifiers.None);
        engine.Handle(KeyEvent.Down(KeyName.A));
        var back = engine.Handle(KeyEvent.Down(KeyName.Backspace));
        Assert.Equal(["Commands"], Assert.IsType<ShowMenu>(back.Effect).Path);
        Assert.IsType<Cancelled>(engine.Handle(KeyEvent.Down(KeyName.Backspace)).Effect);
        Assert.False(engine.IsActive);

        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        engine.Handle(KeyEvent.Down(KeyName.A));
        Assert.IsType<Cancelled>(engine.Handle(KeyEvent.Down(KeyName.Escape)).Effect);
        Assert.False(engine.IsActive);
    }

    [Fact]
    public void ANewConfigEndsTheSequence()
    {
        var engine = new SequenceEngine(Apps);
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        engine.ReplaceConfig(Apps);
        Assert.False(engine.IsActive);
    }
}
