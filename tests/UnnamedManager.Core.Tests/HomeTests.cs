using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using Xunit;
using static UnnamedManager.Core.Keyboard.SequenceEffect;

namespace UnnamedManager.Core.Tests;

/// S-021, the Mac's S-050 rules.
public class HomeTests
{
    private const Modifiers Hyper = Modifiers.Hyper;

    private static readonly LoadedConfig Config = ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "Meh+Space", "title": "Windows" },
          { "keys": "Meh+Space M", "title": "Maximize", "action": "x" },
          { "keys": "Hyper+Space", "title": "Commands" },
          { "keys": "Hyper+Space F", "title": "Find", "action": "x" },
          { "keys": "Meh+T", "title": "Terminal", "action": "x" },
          { "keys": "Hyper+E", "title": "Editor", "action": "x" },
          { "keys": "F13", "title": "Notes", "action": "x" },
          { "keys": "Hyper+N", "title": "Only here", "action": "here" } ],
          "actions": { "x": { "type": "noop" },
            "here": { "type": "contextual", "variants": { "editors": { "type": "noop" } } } },
          "contexts": [ { "id": "editors", "apps": ["Code.exe"] } ] }
        """).Loaded!;

    private static SequenceEngine AtHome(LoadedConfig config, out Home home)
    {
        var engine = new SequenceEngine(config);
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        engine.ModifiersChanged(Modifiers.None);
        home = Assert.IsType<ShowHome>(engine.Handle(KeyEvent.Down(KeyName.Backspace)).Effect).Home;
        return engine;
    }

    [Fact]
    public void BackInALeadersMenuOpensHomeWithTwoOrMoreTopLevelEntries()
    {
        var engine = AtHome(Config, out _);
        Assert.True(engine.IsActive);
        Assert.Equal(["Home"], engine.Path);

        var one = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Hyper+Space", "title": "Commands" },
              { "keys": "Hyper+Space F", "title": "Find", "action": "x" } ], "actions": { "x": { "type": "noop" } } }
            """).Loaded!;
        var alone = new SequenceEngine(one);
        alone.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        Assert.IsType<Cancelled>(alone.Handle(KeyEvent.Down(KeyName.Backspace)).Effect);
        Assert.False(alone.IsActive);
    }

    [Theory]
    [InlineData(KeyName.Backspace)]
    [InlineData(KeyName.Escape)]
    public void BackOrCancelInHomeEndsTheSequence(KeyName key)
    {
        var engine = AtHome(Config, out _);
        Assert.IsType<Cancelled>(engine.Handle(KeyEvent.Down(key)).Effect);
        Assert.False(engine.IsActive);
    }

    [Fact]
    public void HomeGroupsByExactModifiersTheGroupYouCameFromFirst()
    {
        AtHome(Config, out var home);
        Assert.Equal(["Hyper", "Meh", "No modifiers"], home.Groups.Select(g => g.Title));
        Assert.Equal(["Commands", "Editor", "Only here"], home.Groups[0].Entries.Select(e => e.Title));
        Assert.Equal(["Windows", "Terminal"], home.Groups[1].Entries.Select(e => e.Title));
    }

    [Fact]
    public void InTheCurrentGroupTheKeyAlonePicksOthersNeedTheirExactChord()
    {
        AtHome(Config, out var home);
        Assert.Equal("Editor", home.Entry(new Chord(Modifiers.None, KeyName.E))!.Title);
        Assert.Equal("Editor", home.Entry(new Chord(Modifiers.Ctrl | Modifiers.Alt, KeyName.E))!.Title);
        Assert.Equal("Editor", home.Entry(new Chord(Hyper, KeyName.E))!.Title);
        Assert.Null(home.Entry(new Chord(Modifiers.None, KeyName.T)));
        Assert.Equal("Terminal", home.Entry(new Chord(Modifiers.Meh, KeyName.T))!.Title);
        Assert.Equal("Notes", home.Entry(new Chord(Modifiers.None, KeyName.F13))!.Title);
        Assert.Null(home.Entry(new Chord(Modifiers.Shift, KeyName.T)));
    }

    [Fact]
    public void PickingALeaderOpensItsMenu()
    {
        var engine = AtHome(Config, out _);
        var menu = Assert.IsType<ShowMenu>(engine.Handle(KeyEvent.Down(KeyName.Space, Modifiers.Meh)).Effect);
        Assert.Equal(["Home", "Windows"], menu.Path);
        engine.ModifiersChanged(Modifiers.None);
        Assert.Equal("Maximize", Assert.IsType<Run>(engine.Handle(KeyEvent.Down(KeyName.M)).Effect).Binding.Title);
    }

    [Fact]
    public void PickingADirectBindingRunsItForTheAppThatWasInFront()
    {
        var engine = new SequenceEngine(Config);
        var front = new FrontApp(@"C:\Windows\notepad.exe", null, "Notepad");
        engine.SetFrontApp(front);
        engine.Handle(KeyEvent.Down(KeyName.Space, Hyper));
        engine.ModifiersChanged(Modifiers.None);
        engine.Handle(KeyEvent.Down(KeyName.Backspace));
        engine.SetFrontApp(null);
        var run = Assert.IsType<Run>(engine.Handle(KeyEvent.Down(KeyName.E)).Effect);
        Assert.Equal("Editor", run.Binding.Title);
        Assert.Equal(front, run.Front);
        Assert.False(engine.IsActive);
    }

    [Fact]
    public void AnUnavailableDirectBindingInHomeStaysInHome()
    {
        var engine = AtHome(Config, out _);
        Assert.IsType<Unavailable>(engine.Handle(KeyEvent.Down(KeyName.N)).Effect);
        Assert.True(engine.IsActive);
    }
}
