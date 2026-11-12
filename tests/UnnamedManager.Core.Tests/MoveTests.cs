using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-026: Move the window by a step or to a spot.
public class MoveTests
{
    private static readonly Screen Screen = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1032));
    private static readonly Screen Scaled = new(new(-2880, 0, 2880, 1620), new(-2880, 0, 2880, 1548), 1.5);
    private static readonly PixelRect Window = new(100, 100, 800, 600);

    private static MoveAction To(double x, double y) => new(x, y, 0, 0);
    private static MoveAction By(double dx, double dy) => new(null, null, dx, dy);

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0.5, 0.5, 560, 216)]
    [InlineData(1, 1, 1120, 432)]
    [InlineData(1, 0, 1120, 0)]
    public void ToPlacesItAtFractionsOfTheFreeSpace(double x, double y, int left, int top) =>
        Assert.Equal(new PixelRect(left, top, 800, 600), WindowGeometry.Moved(Window, Screen, To(x, y)));

    [Fact]
    public void AWindowWiderThanTheWorkAreaGoesToItsLeftEdge() =>
        Assert.Equal(new PixelRect(0, 0, 2000, 600), WindowGeometry.Moved(new(300, 50, 2000, 600), Screen, To(0.5, 0)));

    [Theory]
    [InlineData(40, 0, 140, 100)]
    [InlineData(0, -40, 100, 60)]
    [InlineData(-20, 25, 80, 125)]
    public void ByMovesByPoints(double dx, double dy, int x, int y) =>
        Assert.Equal(new PixelRect(x, y, 800, 600), WindowGeometry.Moved(Window, Screen, By(dx, dy)));

    [Fact]
    public void PointsAreEffectivePixelsAtTheMonitorsScale() =>
        Assert.Equal(new PixelRect(-1940, 160, 800, 600), WindowGeometry.Moved(new(-2000, 100, 800, 600), Scaled, By(40, 40)));

    [Theory]
    [InlineData(-5000, 0, -760, 100)]   // far left: 40 points of the title bar stay
    [InlineData(5000, 0, 1880, 100)]    // far right
    [InlineData(0, -5000, 100, 0)]      // the top edge stays inside
    [InlineData(0, 5000, 100, 992)]     // 40 points of the title bar above the bottom
    public void AMoveNeverLeavesTheWindowOffScreen(double dx, double dy, int x, int y) =>
        Assert.Equal(new PixelRect(x, y, 800, 600), WindowGeometry.Moved(Window, Screen, By(dx, dy)));

    [Fact]
    public void TheTitleBarKeptIsInPointsOnAScaledMonitor()
    {
        var moved = WindowGeometry.Moved(new(-2000, 100, 800, 600), Scaled, By(-5000, 0));
        Assert.Equal(-2880 - 800 + 60, moved.X);
    }

    [Fact]
    public void ItsSizeStaysTheSame()
    {
        var fake = new FakeWindowSystem();
        var w = fake.Add(1, Window);
        new WindowControl(fake).Run(new WindowCommand.Move(To(0.5, 0.5)));
        Assert.Equal((800, 600), (w.Frame.Width, w.Frame.Height));
    }

    [Theory]
    [InlineData("""{ "type": "window.move", "to": { "y": 0.5, "x": 0.5 } }""", "to={\"x\":0.5,\"y\":0.5}")]
    [InlineData("""{ "type": "window.move", "by": { "dx": 0, "dy": 40 } }""", "by={\"dy\":40}")]
    [InlineData("""{ "type": "window.move", "by": { } }""", "by={}")]
    public void ItWritesToOrBy(string json, string written)
    {
        var action = ConfigLoader.Load($$"""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ], "actions": { "a": {{json}} } }
            """).Loaded!.Config.Action("a")!;
        var family = ActionRegistry.Standard.Family("window.move")!;
        Assert.Equal(written, string.Join(" ", family.Write(action).Select(f => $"{f.Key}={f.Value.ToJsonString()}")));
        Assert.Equal(new ActionPlan.Window(new WindowCommand.Move((MoveAction)action)), family.Plan(action, new PlanContext(@"C:\Users\me")));
    }
}
