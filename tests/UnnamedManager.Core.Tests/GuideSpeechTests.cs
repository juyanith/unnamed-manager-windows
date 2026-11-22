using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using Xunit;

namespace UnnamedManager.Core.Tests;

// S-099, S-100, S-101: what Narrator hears.
public class GuideSpeechTests
{
    private static readonly LoadedConfig Config = ConfigLoader.Load("""
        { "version": 1, "entries": [
          { "keys": "Alt+A", "title": "Apps", "hold": true },
          { "keys": "Alt+A T", "title": "Terminal", "action": "noop" },
          { "keys": "Alt+A B", "title": "Browser", "action": "noop" },
          { "keys": "Alt+A M", "title": "Mail", "action": "noop" },
          { "keys": "Alt+A N", "title": "Notes", "action": "noop" },
          { "keys": "Alt+Tab", "title": "Recent", "hold": true, "list": { "type": "windows.recent", "count": 20 } } ] }
        """).Loaded!;

    private static BindingNode Menu => Config.Leaders[0];

    private static BindingNode List(int count, ListItem? front = null) =>
        Config.Leaders[1].Filled([.. Enumerable.Range(1, count).Select(i => new ListItem(i, $"Window {i}"))], front);

    [Fact]
    public void OpeningSaysTheMenuTheItemAndItsPlace() => Assert.Equal("Apps, Terminal, 1 of 4", GuideSpeech.Opened(Menu, 1));

    [Fact]
    public void AMoveSaysOnlyTheItemAndItsPlace() => Assert.Equal("Browser, 2 of 4", GuideSpeech.Moved(Menu, 2));

    [Fact]
    public void CancelIsntCounted()
    {
        Assert.Equal("Cancel", GuideSpeech.Moved(Menu, 0));
        Assert.Equal("Cancel, Code", GuideSpeech.Moved(List(3, new ListItem(99, "Code")), 0));
    }

    [Fact]
    public void AListCountsAcrossItsPages() => Assert.Equal("Window 10, 10 of 20", GuideSpeech.Moved(List(20).OnPage(1), 1));

    [Fact]
    public void AnEmptyListSaysSoInsteadOfNoKeys() => Assert.Equal("Recent, No recent windows", GuideSpeech.Opened(List(0), null));

    [Fact]
    public void TheHighlightNeverRestsOnMore()
    {
        var engine = new SequenceEngine(Config);
        engine.Handle(KeyEvent.Down(KeyName.Tab, Modifiers.Alt));
        var shown = engine.Fill([.. Enumerable.Range(1, 20).Select(i => new ListItem(i, $"Window {i}"))], null).OfType<SequenceEffect.ShowHeld>().Single();
        for (var i = 0; i < 25; i++)
        {
            Assert.InRange(shown.Highlight, 0, shown.Menu.Children.Count);
            shown = Assert.IsType<SequenceEffect.ShowHeld>(engine.Handle(KeyEvent.Down(KeyName.Tab, Modifiers.Alt)).Effect);
        }
    }
}
