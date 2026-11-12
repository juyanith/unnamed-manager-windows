using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using Xunit;
using static UnnamedManager.Core.Keyboard.SequenceEffect;

namespace UnnamedManager.Core.Tests;

/// S-019: a top-level chord with an action runs it at once.
public class DirectBindingTests
{
    private const Modifiers Hyper = Modifiers.Hyper;

    private static readonly LoadedConfig Config = ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "Hyper+Space", "title": "Commands" },
          { "keys": "Hyper+Space F", "title": "Find", "action": "x" },
          { "keys": "Hyper+T", "title": "Terminal", "action": "x" } ],
          "actions": { "x": { "type": "noop" } } }
        """).Loaded!;

    [Fact]
    public void ATopLevelEntryWithAnActionIsADirectBindingWithNothingUnderIt()
    {
        var direct = Config.Leaders.Single(l => l.Title == "Terminal");
        Assert.False(direct.IsMenu);
        Assert.Empty(direct.Children);
        var under = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Hyper+T", "title": "Terminal", "action": "x" },
              { "keys": "Hyper+T A", "title": "Under", "action": "x" } ], "actions": { "x": { "type": "noop" } } }
            """);
        Assert.Equal(Rule.ParentIsBinding, Assert.Single(under.Errors).Rule);
    }

    [Fact]
    public void ItsChordRunsTheActionForTheAppInFrontAndStartsNoSequence()
    {
        var engine = new SequenceEngine(Config);
        var front = new FrontApp(@"C:\Windows\notepad.exe", null, "Notepad");
        engine.SetFrontApp(front);
        var run = Assert.IsType<Run>(engine.Handle(KeyEvent.Down(KeyName.T, Hyper)).Effect);
        Assert.Equal("Terminal", run.Binding.Title);
        Assert.Equal(front, run.Front);
        Assert.False(engine.IsActive);
    }

    [Fact]
    public void HoldingItRunsOnce()
    {
        var engine = new SequenceEngine(Config);
        Assert.IsType<Run>(engine.Handle(KeyEvent.Down(KeyName.T, Hyper)).Effect);
        for (var i = 0; i < 5; i++)
        {
            var repeat = engine.Handle(KeyEvent.Down(KeyName.T, Hyper, repeat: true));
            Assert.True(repeat.Consume);
            Assert.Null(repeat.Effect);
        }
        Assert.True(engine.Handle(KeyEvent.Up(KeyName.T)).Consume);
    }

    [Fact]
    public void InsideASequenceItsAKeyThatIsntBound()
    {
        var engine = new SequenceEngine(Config);
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        engine.ModifiersChanged(Modifiers.None);
        var output = engine.Handle(KeyEvent.Down(KeyName.T, Hyper));
        Assert.True(output.Consume);
        Assert.IsType<Unbound>(output.Effect);
        Assert.True(engine.IsActive);
    }
}
