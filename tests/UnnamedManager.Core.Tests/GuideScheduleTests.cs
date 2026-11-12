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

    // S-031: Finish a known sequence with nothing on screen.

    [Fact]
    public void AFastSequenceNeverShowsTheGuide()
    {
        var s = Schedule(delay: 500);
        Assert.Empty(s.Handle(Menu(Commands), 0));
        Assert.Equal(500, s.NextDeadline);
        Assert.Empty(s.Tick(499));
        Assert.Empty(s.Handle(Run, 300));
        Assert.False(s.IsVisible);
        Assert.Null(s.NextDeadline);
    }

    [Fact]
    public void TheGuideShowsOnlyAfterTheDelayPassesWithNoKey()
    {
        var s = Schedule(delay: 500);
        s.Handle(Menu(Commands), 0);
        Assert.Equal(Commands, Assert.IsType<Show>(Assert.Single(s.Tick(500))).Menu);
        Assert.True(s.IsVisible);
        Assert.Null(s.NextDeadline);
    }

    [Fact]
    public void EveryKeyBackIncludedRestartsTheDelayAndTheGuideShowsTheLatestMenu()
    {
        var s = Schedule(delay: 500);
        s.Handle(Menu(Commands), 0);
        s.Handle(Menu(Applications), 400);
        Assert.Empty(s.Tick(500));
        s.Handle(Menu(Commands), 800); // Back
        Assert.Equal(1300, s.NextDeadline);
        Assert.Empty(s.Tick(1299));
        Assert.Equal(Commands, Assert.IsType<Show>(Assert.Single(s.Tick(1300))).Menu);
    }

    [Fact]
    public void OnceShownTheGuideStaysUntilTheSequenceEnds()
    {
        var s = Schedule(delay: 500);
        s.Handle(Menu(Commands), 0);
        s.Tick(500);
        Assert.Equal(Applications, Assert.IsType<Show>(Assert.Single(s.Handle(Menu(Applications), 600))).Menu);
        Assert.Null(s.NextDeadline);
        Assert.True(s.IsVisible);
        Assert.Equal(new Hide(Cancelled: true), Assert.Single(s.Handle(new SequenceEffect.Cancelled(), 5000)));
    }

    // S-032: Let an abandoned sequence time out.

    [Fact]
    public void AForgottenGuideClosesAndTheSequenceEnds()
    {
        var s = Schedule(timeout: 5000);
        s.Handle(Menu(Commands), 0);
        Assert.Equal(5000, s.NextDeadline);
        Assert.Empty(s.Tick(4999));
        Assert.Equal([new Hide(Cancelled: true), new EndSequence()], s.Tick(5000));
        Assert.False(s.IsVisible);
        Assert.Null(s.NextDeadline);
    }

    [Fact]
    public void TheTimeoutRunsOnlyWhileTheGuideIsVisible()
    {
        var s = Schedule(delay: 500, timeout: 1000);
        s.Handle(Menu(Commands), 0);
        Assert.Equal(500, s.NextDeadline);
        s.Tick(500);
        Assert.Equal(1500, s.NextDeadline);
    }

    [Fact]
    public void EveryKeyRestartsTheTimeoutUnboundKeysIncluded()
    {
        var s = Schedule(timeout: 1000);
        s.Handle(Menu(Commands), 0);
        s.Handle(Menu(Applications), 800);
        Assert.Equal(1800, s.NextDeadline);
        s.Handle(new SequenceEffect.Unbound(["Commands"]), 1500);
        Assert.Equal(2500, s.NextDeadline);
        Assert.Empty(s.Tick(1800));
    }

    [Fact]
    public void WithShowHudOffTheTimeoutNeverFires()
    {
        var s = Schedule(showHud: false, timeout: 1000);
        s.Handle(Menu(Commands), 0);
        s.Handle(new SequenceEffect.Unbound(["Commands"]), 10);
        Assert.Null(s.NextDeadline);
        Assert.Empty(s.Tick(10_000));
    }

    [Fact]
    public void WithNoTimeoutTheGuideStays()
    {
        var s = Schedule();
        s.Handle(Menu(Commands), 0);
        Assert.Null(s.NextDeadline);
    }

    // S-033: Get feedback for a key that isn't bound.

    [Fact]
    public void AnUnboundKeyLeavesTheSequenceWhereItWasAndGivesAnError()
    {
        var engine = new SequenceEngine(Apps);
        engine.Handle(KeyEvent.Down(KeyName.Space, Modifiers.Ctrl | Modifiers.Alt));
        engine.Handle(KeyEvent.Down(KeyName.A));
        var wrong = engine.Handle(KeyEvent.Down(KeyName.Z));
        Assert.True(wrong.Consume);
        Assert.Equal(["Commands", "Applications"], Assert.IsType<SequenceEffect.Unbound>(wrong.Effect).Path);
        Assert.Equal(["Commands", "Applications"], engine.Path);
        Assert.IsType<SequenceEffect.Run>(engine.Handle(KeyEvent.Down(KeyName.T)).Effect);
    }

    [Fact]
    public void AnErrorBeepsAndFlashesTheGuide()
    {
        var s = Schedule();
        s.Handle(Menu(Commands), 0);
        Assert.Equal([new Bell(), new Flash()], s.Handle(new SequenceEffect.Unbound(["Commands"]), 10));
        Assert.Equal([new Bell(), new Flash()], s.Handle(new SequenceEffect.Unavailable(Applications.Children[0], ["Commands"]), 20));
        Assert.True(s.IsVisible);
    }

    [Fact]
    public void WithoutErrorBellNothingPlaysAndWithoutErrorFlashNothingFlashes()
    {
        var quiet = Schedule(bell: false);
        quiet.Handle(Menu(Commands), 0);
        Assert.Equal([new Flash()], quiet.Handle(new SequenceEffect.Unbound(["Commands"]), 10));
        var still = Schedule(flash: false);
        still.Handle(Menu(Commands), 0);
        Assert.Equal([new Bell()], still.Handle(new SequenceEffect.Unbound(["Commands"]), 10));
    }

    [Fact]
    public void IfTheGuideWasStillWaitingForItsDelayItAppearsAtOnce()
    {
        var s = Schedule(delay: 500, timeout: 2000);
        s.Handle(Menu(Commands), 0);
        var commands = s.Handle(new SequenceEffect.Unbound(["Commands"]), 100);
        Assert.Equal(3, commands.Count);
        Assert.IsType<Bell>(commands[0]);
        Assert.Equal(Commands, Assert.IsType<Show>(commands[1]).Menu);
        Assert.IsType<Flash>(commands[2]);
        Assert.True(s.IsVisible);
        Assert.Equal(2100, s.NextDeadline);
    }

    [Fact]
    public void WithShowHudOffNothingShows()
    {
        var s = Schedule(showHud: false);
        Assert.Empty(s.Handle(Menu(Commands), 0));
        Assert.False(s.IsVisible);
    }
}
