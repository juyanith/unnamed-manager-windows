using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using Xunit;
using static UnnamedManager.Core.Keyboard.GuideCommand;

namespace UnnamedManager.Core.Tests;

/// The Guide's schedule: when it shows, updates and hides (M4).
public class GuideScheduleTests
{
    private static readonly LoadedConfig Apps = ConfigLoader.Load("""
        { "version": 1, "entries": [
            { "keys": "Ctrl+Alt+Space", "title": "Commands" },
            { "keys": "Ctrl+Alt+Space A", "title": "Applications" },
            { "keys": "Ctrl+Alt+Space A T", "title": "Terminal", "action": "x" }
          ], "actions": { "x": { "type": "noop" } } }
        """).Loaded!;

    private static readonly BindingNode Commands = Apps.Leaders[0];
    private static readonly BindingNode Applications = Commands.Children[0];

    internal static SequenceEffect Menu(BindingNode menu) => new SequenceEffect.ShowMenu(menu, [menu.Title]);
    internal static SequenceEffect Run => new SequenceEffect.Run(Applications.Children[0]);

    internal static GuideSchedule Schedule(int delay = 0, int timeout = 0, bool showHud = true, bool flash = true, bool bell = true) =>
        new(new RuntimeSettings { HudDelayMs = delay, TimeoutMs = timeout, ShowHud = showHud }, new UISettings { ErrorFlash = flash, ErrorBell = bell });

    [Fact]
    public void WithNoDelayAMenuShowsAtOnceAndTheEndHidesIt()
    {
        var s = Schedule();
        Assert.IsType<Show>(Assert.Single(s.Handle(Menu(Commands), 0)));
        Assert.True(s.IsVisible);
        Assert.Equal(Applications, Assert.IsType<Show>(Assert.Single(s.Handle(Menu(Applications), 10))).Menu);
        Assert.Equal(new Hide(), Assert.Single(s.Handle(Run, 20)));
        Assert.False(s.IsVisible);
    }

    [Fact]
    public void CancelHidesItAndSaysSo()
    {
        var s = Schedule();
        s.Handle(Menu(Commands), 0);
        Assert.Equal(new Hide(Cancelled: true), Assert.Single(s.Handle(new SequenceEffect.Cancelled(), 5)));
    }

    [Fact]
    public void WithShowHudOffNothingShows()
    {
        var s = Schedule(showHud: false);
        Assert.Empty(s.Handle(Menu(Commands), 0));
        Assert.False(s.IsVisible);
    }
}
