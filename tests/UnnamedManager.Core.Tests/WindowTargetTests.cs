using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// What Focus Window, Swap Window and Arrange Windows do on fake windows (spec v5 to
/// v15): targets, scope, the window used before, each app's rules, and the alert sound.
public class WindowTargetTests
{
    private static readonly Screen Second = new(new(1920, 0, 1920, 1080), new(1920, 0, 1920, 1032));

    /// Three windows side by side, left (1), middle (2), right (3); the middle one has focus.
    private static FakeWindowSystem Row()
    {
        var fake = new FakeWindowSystem();
        fake.Add(1, new(0, 0, 600, 1000));
        fake.Add(3, new(1300, 0, 600, 1000));
        fake.Add(2, new(650, 0, 600, 1000));
        return fake;
    }

    private static WindowControl Control(FakeWindowSystem fake, WindowSettings? settings = null) =>
        new(fake, () => settings ?? new WindowSettings { Gap = 0 });

    private static WindowFailure? Focus(WindowControl control, string word, string scope = WindowScope.All) =>
        control.Run(new WindowCommand.Focus(word, scope));

    private static WindowFailure? Swap(WindowControl control, string word, string scope = WindowScope.All) =>
        control.Run(new WindowCommand.Swap(word, scope));

    [Fact]
    public void FirstAndLastGoToTheEndsAndStayPut()
    {
        var fake = Row();
        var control = Control(fake);
        Assert.Null(Focus(control, "first"));
        Assert.Equal(1, fake.Front);
        Assert.Null(Focus(control, "first"));
        Assert.Equal(1, fake.Front);
        Assert.Null(Focus(control, "last"));
        Assert.Equal(3, fake.Front);
    }

    [Fact]
    public void FirstLastAndNumbersWorkWithNoFocusedWindow()
    {
        var fake = Row();
        fake.Front = null;
        Assert.Null(Control(fake).Run(new WindowCommand.Focus(WindowTarget.Of(2))));
        Assert.Equal(2, fake.Front);
        fake.Front = null;
        Assert.Null(Focus(Control(fake), "last"));
        Assert.Equal(3, fake.Front);
    }

    [Fact]
    public void ANumberPastTheLastWindowFailsWithTheSound()
    {
        var failure = Control(Row()).Run(new WindowCommand.Focus(WindowTarget.Of(4)));
        Assert.Equal(new WindowFailure("No window 4. There are 3.", Alerts: true), failure);
    }

    [Fact]
    public void WithNoWindowAtAllFocusFailsWithTheSound()
    {
        var fake = new FakeWindowSystem { FrontAppName = "The desktop" };
        Assert.Equal(new WindowFailure("No window to focus.", true), Focus(Control(fake), "left"));
        Assert.Equal(new WindowFailure("No window to focus.", true), Focus(Control(fake), "first"));
    }

    [Fact]
    public void WithNoFocusedWindowADirectionGoesToTheWindowInFront()
    {
        var fake = Row();
        fake.Front = null;
        Assert.Null(Focus(Control(fake), "left"));
        Assert.Equal(2, fake.Front);
    }

    [Fact]
    public void AWindowHiddenBehindAnotherIsSkipped()
    {
        var fake = Row();
        fake.Add(4, new(1350, 100, 400, 400));
        fake.Click(3);
        fake.Click(2);
        var control = Control(fake);
        Assert.Null(Focus(control, "next"));
        Assert.Equal(3, fake.Front);
        Assert.Null(Focus(control, "next"));
        Assert.Equal(1, fake.Front);
    }

    [Fact]
    public void OnTheSameDisplayFocusStopsAtItsEdge()
    {
        var fake = Row();
        fake.Monitors.Add(Second);
        fake.Add(5, new(2000, 0, 600, 1000));
        fake.Click(3);
        var control = Control(fake);
        Assert.Equal(new WindowFailure("No window to the right.", true), Focus(control, "right", WindowScope.Display));
        Assert.Null(Focus(control, "last", WindowScope.Display));
        Assert.Equal(3, fake.Front);
        Assert.Null(Focus(control, "right"));
        Assert.Equal(5, fake.Front);
    }

    [Fact]
    public void SwapTradesFramesAndKeepsFocus()
    {
        var fake = Row();
        var control = Control(fake);
        Assert.Null(Swap(control, "left"));
        Assert.Equal(2, fake.Front);
        Assert.Equal(new PixelRect(0, 0, 600, 1000), fake.Windows[2].Frame);
        Assert.Equal(new PixelRect(650, 0, 600, 1000), fake.Windows[1].Frame);
        Assert.Null(Swap(control, "right"));
        Assert.Equal(new PixelRect(650, 0, 600, 1000), fake.Windows[2].Frame);
        Assert.Equal(new PixelRect(0, 0, 600, 1000), fake.Windows[1].Frame);
    }

    [Fact]
    public void SwapByNumberCountsTheFocusedWindowAndStaysOnItself()
    {
        var fake = Row();
        var control = Control(fake);
        Assert.Null(control.Run(new WindowCommand.Swap(WindowTarget.Of(2))));
        Assert.Equal(new PixelRect(650, 0, 600, 1000), fake.Windows[2].Frame);
        Assert.Null(control.Run(new WindowCommand.Swap(WindowTarget.Of(3))));
        Assert.Equal(new PixelRect(1300, 0, 600, 1000), fake.Windows[2].Frame);
        Assert.Equal(new WindowFailure("No window 5. There are 3.", true), control.Run(new WindowCommand.Swap(WindowTarget.Of(5))));
    }

    [Fact]
    public void SwapWithNoWindowThereFailsWithTheSoundButNotWithNoFocusedWindow()
    {
        var fake = new FakeWindowSystem();
        fake.Add(1, new(0, 0, 600, 1000));
        Assert.Equal(new WindowFailure("No window to the left.", true), Swap(Control(fake), "left"));
        Assert.Equal(new WindowFailure("No other window to swap with.", true), Swap(Control(fake), "next"));
        fake.Front = null;
        Assert.False(Swap(Control(fake), "left")!.Alerts);
    }

    [Fact]
    public void AWindowWithAMinimumSizeKeepsTheEdgeOnTheScreensEdge()
    {
        var fake = new FakeWindowSystem();
        fake.Add(1, new(0, 0, 600, 1000)).MinSize = (800, 1000);
        fake.Add(2, new(1320, 0, 600, 1000));
        Assert.Null(Swap(Control(fake), "left"));
        Assert.Equal(new PixelRect(1120, 0, 800, 1000), fake.Windows[1].Frame);
        Assert.Equal(new PixelRect(0, 0, 600, 1000), fake.Windows[2].Frame);
    }

    [Fact]
    public void TheHistoryPutsAWindowFirstOnceAndKeepsThirty()
    {
        var history = new FocusHistory();
        for (var i = 1; i <= 32; i++) history.Record(new WindowRef(i));
        history.Record(new WindowRef(5));
        Assert.Equal(30, history.Windows.Count);
        Assert.Equal([5, 32, 31, 30], history.Windows.Take(4).Select(w => (int)w.Handle));
        Assert.Equal([4, 3], history.Windows.TakeLast(2).Select(w => (int)w.Handle));
        Assert.Equal(new WindowRef(32), history.Recent(new WindowRef(5), _ => true));
    }

    [Fact]
    public void RecentGoesBackAndForthEvenToACoveredWindow()
    {
        var fake = new FakeWindowSystem();
        var control = Control(fake);
        fake.Add(1, new(0, 0, 400, 400));
        fake.Click(1);
        fake.Add(2, new(0, 0, 1000, 1000));
        fake.Click(2);
        Assert.Null(Focus(control, "recent"));
        Assert.Equal(1, fake.Front);
        Assert.Null(Focus(control, "recent"));
        Assert.Equal(2, fake.Front);
    }

    [Fact]
    public void RecentSkipsAClosedWindowAndCrossesDesktops()
    {
        var fake = new FakeWindowSystem();
        var control = Control(fake);
        fake.Add(1, new(0, 0, 400, 400));
        fake.Click(1);
        fake.CurrentDesktop = 1;
        fake.Add(2, new(500, 0, 400, 400));
        fake.Click(2);
        fake.Add(3, new(1000, 0, 400, 400));
        fake.Click(3);
        fake.Remove(2);
        fake.Click(3);
        Assert.Null(Focus(control, "recent"));
        Assert.Equal(1, fake.Front);
        Assert.Equal(0, fake.CurrentDesktop);
    }

    [Fact]
    public void SwapRecentStaysOnThisDesktopAndSwapsBack()
    {
        var fake = new FakeWindowSystem();
        var control = Control(fake);
        fake.Add(1, new(0, 0, 400, 400));
        fake.Click(1);
        fake.CurrentDesktop = 1;
        fake.Add(2, new(500, 0, 400, 400));
        fake.Click(2);
        fake.Add(3, new(1000, 0, 600, 600));
        fake.Click(3);
        Assert.Null(Swap(control, "recent"));
        Assert.Equal(new PixelRect(500, 0, 400, 400), fake.Windows[3].Frame);
        Assert.Equal(3, fake.Front);
        Assert.Null(Swap(control, "recent"));
        Assert.Equal(new PixelRect(1000, 0, 600, 600), fake.Windows[3].Frame);
        fake.Remove(2);
        Assert.Equal(new WindowFailure("No window used before.", true), Swap(control, "recent"));
    }

    [Fact]
    public void WithNothingInTheHistoryRecentIsTheWindowInFrontAfterTheFocusedOne()
    {
        var fake = Row();
        fake.Front = 2;
        Assert.Null(Focus(Control(fake), "recent"));
        Assert.Equal(3, fake.Front);
    }

    private static WindowSettings Rules(string app, AppRules rules) => new() { Gap = 0, Apps = [new(app, rules)] };

    [Fact]
    public void FocusNeverPicksAnAppLeftOutOfIt()
    {
        var fake = Row();
        fake.Windows[3].App = "Calculator";
        var control = Control(fake, Rules("Calculator.exe", new(Focus: false)));
        Assert.Equal(new WindowFailure("No window to the right.", true), Focus(control, "right"));
        Assert.Null(Focus(control, "last"));
        Assert.Equal(2, fake.Front);
        // Swap Window reads its own rule.
        Assert.Null(Swap(control, "right"));
        Assert.Equal(new PixelRect(1300, 0, 600, 1000), fake.Windows[2].Frame);
    }

    [Fact]
    public void SwapNeverMovesAnAppLeftOutOfItEvenTheFocusedOne()
    {
        var fake = Row();
        fake.Windows[2].App = "Calculator";
        var control = Control(fake, Rules("calculator.exe", new(Swap: false)));
        Assert.Equal(new WindowFailure("Calculator is left out of Swap Window.", true), Swap(control, "left"));
        // From another window, it swaps past the left-out one.
        fake.Click(1);
        Assert.Null(Swap(control, "right"));
        Assert.Equal(new PixelRect(1300, 0, 600, 1000), fake.Windows[1].Frame);
        Assert.Equal(new PixelRect(650, 0, 600, 1000), fake.Windows[2].Frame);
    }

    [Fact]
    public void ArrangeLeavesOutAnAppButStillArrangesItsDisplay()
    {
        var fake = new FakeWindowSystem();
        fake.Add(1, new(100, 100, 400, 400));
        fake.Add(2, new(300, 300, 400, 400));
        fake.Add(3, new(500, 100, 400, 400)).App = "Calculator";
        var control = Control(fake, Rules("Calculator.exe", new(Arrange: false)) with { Gap = 8 });
        Assert.Null(control.Run(new WindowCommand.Arrange(new ArrangeAction())));
        Assert.Equal(new PixelRect(500, 100, 400, 400), fake.Windows[3].Frame);
        Assert.Equal(new PixelRect(8, 8, 948, 1016), fake.Windows[1].Frame);
        Assert.Equal(new PixelRect(964, 8, 948, 1016), fake.Windows[2].Frame);
        // Run again from the same window, nothing moves.
        Assert.Null(control.Run(new WindowCommand.Arrange(new ArrangeAction())));
        Assert.Equal(new PixelRect(964, 8, 948, 1016), fake.Windows[2].Frame);
    }

    [Fact]
    public void ArrangeWithNoWindowFails()
    {
        var fake = new FakeWindowSystem { FrontAppName = "The desktop" };
        Assert.Equal("No window to arrange.", Control(fake).Run(new WindowCommand.Arrange(new ArrangeAction()))?.Message);
    }

    [Fact]
    public void ArrangeLeavesOtherDisplaysAlone()
    {
        var fake = new FakeWindowSystem();
        fake.Monitors.Add(Second);
        fake.Add(9, new(2000, 100, 400, 400));
        fake.Add(1, new(100, 100, 400, 400));
        fake.Add(2, new(600, 100, 400, 400));
        Assert.Null(Control(fake).Run(new WindowCommand.Arrange(new ArrangeAction())));
        Assert.Equal(new PixelRect(2000, 100, 400, 400), fake.Windows[9].Frame);
        Assert.Equal(new PixelRect(960, 0, 960, 1032), fake.Windows[2].Frame);
    }

    [Fact]
    public void AppRulesAreReadAndWrittenWithAllThree()
    {
        const string start = """{ "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "window-arrange" } ]""";
        var config = ConfigLoader.Load(start + """, "windows": { "apps": { "CalculatorApp.exe": { "arrange": false } } } }""").Loaded!.Config;
        Assert.Equal([new("CalculatorApp.exe", new AppRules(Arrange: false))], config.Windows.Apps);
        Assert.Contains("""  "windows": { "apps": { "CalculatorApp.exe": { "focus": true, "swap": true, "arrange": false } } }""", ConfigWriter.Write(config));
        Assert.Equal(["CalculatorApp.exe"], config.Windows.LeftOut(r => r.Arrange));
        Assert.Empty(config.Windows.LeftOut(r => r.Focus));
    }
}
