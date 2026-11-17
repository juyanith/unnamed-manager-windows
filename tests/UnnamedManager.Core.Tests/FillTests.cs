using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-022: Maximize the window in front, and restore it.
public class FillTests
{
    private static readonly Screen Left = new(new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1032));
    private static readonly Screen Primary = new(new(0, 0, 2880, 1620), new(0, 0, 2880, 1548), 1.5);

    private static (FakeWindowSystem Fake, WindowControl Control) Setup()
    {
        var fake = new FakeWindowSystem();
        fake.Monitors.Clear();
        fake.Monitors.AddRange([Primary, Left]);
        return (fake, new WindowControl(fake));
    }

    [Theory]
    [InlineData(-1500, 100, -1920)] // all on the left monitor
    [InlineData(-300, 100, 0)]       // mostly on the primary
    [InlineData(-1100, 100, -1920)] // mostly on the left
    [InlineData(5000, 5000, 0)]      // on none: the primary
    public void TheTargetIsTheWorkAreaOfTheMonitorWithMostOfTheWindow(int x, int y, int workX)
    {
        var screen = WindowGeometry.ScreenFor(new PixelRect(x, y, 1000, 600), [Primary, Left])!;
        Assert.Equal(workX, screen.Work.X);
    }

    [Fact]
    public void AWindowThatCanMaximizeUsesItsOwnMaximizeAndRestore()
    {
        var (fake, control) = Setup();
        var w = fake.Add(1, new(100, 100, 800, 600));
        Assert.Null(control.Run(new WindowCommand.Fill(true)));
        Assert.True(w.IsMaximized);
        Assert.Null(control.Run(new WindowCommand.Fill(true)));
        Assert.False(w.IsMaximized);
        Assert.Equal(new PixelRect(100, 100, 800, 600), w.Frame);
    }

    [Fact]
    public void WithoutToggleAMaximizedWindowStaysMaximized()
    {
        var (fake, control) = Setup();
        var w = fake.Add(1, new(100, 100, 800, 600));
        control.Run(new WindowCommand.Fill(false));
        control.Run(new WindowCommand.Fill(false));
        Assert.True(w.IsMaximized);
    }

    [Fact]
    public void AWindowThatCantMaximizeFillsTheWorkAreaAndGoesBack()
    {
        var (fake, control) = Setup();
        var w = fake.Add(1, new(-1500, 100, 800, 600));
        w.CanMaximize = false;
        control.Run(new WindowCommand.Fill(true));
        Assert.Equal(Left.Work, w.Frame);
        Assert.False(w.IsMaximized);
        control.Run(new WindowCommand.Fill(true));
        Assert.Equal(new PixelRect(-1500, 100, 800, 600), w.Frame);
    }

    [Fact]
    public void TheFrameBeforeIsKeptPerWindow()
    {
        var (fake, control) = Setup();
        var a = fake.Add(1, new(100, 100, 800, 600));
        var b = fake.Add(2, new(300, 200, 900, 700));
        a.CanMaximize = b.CanMaximize = false;
        fake.Front = 1; control.Run(new WindowCommand.Fill(true));
        fake.Front = 2; control.Run(new WindowCommand.Fill(true));
        fake.Front = 1; control.Run(new WindowCommand.Fill(true));
        fake.Front = 2; control.Run(new WindowCommand.Fill(true));
        Assert.Equal(new PixelRect(100, 100, 800, 600), a.Frame);
        Assert.Equal(new PixelRect(300, 200, 900, 700), b.Frame);
    }

    [Fact]
    public void AWindowThatMovedSinceForgetsItsFrameAndFillsAgain()
    {
        var (fake, control) = Setup();
        var w = fake.Add(1, new(100, 100, 800, 600));
        w.CanMaximize = false;
        control.Run(new WindowCommand.Fill(true));
        w.Frame = new PixelRect(400, 300, 700, 500);
        control.Run(new WindowCommand.Fill(true));
        Assert.Equal(Primary.Work, w.Frame);
        control.Run(new WindowCommand.Fill(true));
        Assert.Equal(new PixelRect(400, 300, 700, 500), w.Frame);
    }

    [Fact]
    public void AFilledWindowWithinTwoPointsCountsAsFilled()
    {
        var (fake, control) = Setup();
        // 2 points at 150 % is 3 pixels.
        var w = fake.Add(1, new(3, 0, 2877, 1545));
        w.CanMaximize = false;
        control.Run(new WindowCommand.Fill(true));
        Assert.Equal(new PixelRect(3, 0, 2877, 1545), w.Frame);
    }

    [Fact]
    public void AnAppThatSizesInStepsStillRestores()
    {
        var (fake, control) = Setup();
        var w = fake.Add(1, new(100, 100, 800, 600));
        w.CanMaximize = false;
        w.MaxSize = (2800, 1500);
        control.Run(new WindowCommand.Fill(true));
        Assert.Equal(new PixelRect(0, 0, 2800, 1500), w.Frame);
        control.Run(new WindowCommand.Fill(true));
        Assert.Equal(new PixelRect(100, 100, 800, 600), w.Frame);
    }

    [Fact]
    public void NoWindowInFrontFailsWithTheAppsName()
    {
        var (fake, control) = Setup();
        fake.FrontAppName = "The desktop";
        Assert.Equal("The desktop has no window to fill.", control.Run(new WindowCommand.Fill(true))?.Message);
    }

    [Fact]
    public void AFullScreenWindowIsLeftAlone()
    {
        var (fake, control) = Setup();
        var w = fake.Add(1, Primary.Bounds);
        w.IsFullScreen = true;
        w.App = "Edge";
        Assert.Equal("Edge is full screen. Leave full screen first.", control.Run(new WindowCommand.Fill(true))?.Message);
        Assert.False(w.IsMaximized);
    }

    [Theory]
    [InlineData("""{ "type": "window.maximize" }""", true, "")]
    [InlineData("""{ "type": "window.maximize", "toggle": false }""", false, "toggle=false")]
    public void ItPlansMaximizeAndWritesToggleOnlyWhenFalse(string json, bool toggle, string written)
    {
        var action = ConfigLoader.Load($$"""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ], "actions": { "a": {{json}} } }
            """).Loaded!.Config.Action("a")!;
        var family = ActionRegistry.Standard.Family("window.maximize")!;
        Assert.Equal(new ActionPlan.Window(new WindowCommand.Fill(toggle)), family.Plan(action, new PlanContext(@"C:\Users\me")));
        Assert.Equal(written, string.Join(" ", family.Write(action).Select(f => $"{f.Key}={f.Value.ToJsonString()}")));
    }
}
