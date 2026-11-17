using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-028: Focus the nearest window left, right, up or down.
public class FocusDirectionTests
{
    private static readonly PixelRect Current = new(800, 400, 400, 300); // center 1000, 550

    private static SeenWindow[] Seen(PixelRect[] frames) => [.. frames.Select(f => new SeenWindow(f))];

    [Fact]
    public void ItPicksTheNearestWindowThatWay()
    {
        PixelRect[] windows = [new(1300, 400, 400, 300), new(1700, 400, 400, 300), new(200, 400, 400, 300)];
        Assert.Equal(0, WindowGeometry.Neighbor(Current, "right", Seen(windows), SeenDistances.Default));
        Assert.Equal(2, WindowGeometry.Neighbor(Current, "left", Seen(windows), SeenDistances.Default));
        Assert.Null(WindowGeometry.Neighbor(Current, "up", Seen(windows), SeenDistances.Default));
    }

    [Fact]
    public void OnlyWindowsWithin45DegreesCount()
    {
        // 300 right and 350 down: more than 45° from right, within 45° of down.
        PixelRect[] windows = [new(1100, 750, 400, 300)];
        Assert.Null(WindowGeometry.Neighbor(Current, "right", Seen(windows), SeenDistances.Default));
        Assert.Equal(0, WindowGeometry.Neighbor(Current, "down", Seen(windows), SeenDistances.Default));
    }

    [Fact]
    public void TheNearestEdgeInLineWins()
    {
        // Both are in line; the first starts at the current window's right edge, the second 100 past it.
        PixelRect[] windows = [new(1200, 500, 400, 100), new(1300, 400, 200, 300)];
        Assert.Equal(0, WindowGeometry.Neighbor(Current, "right", Seen(windows), SeenDistances.Default));
        Assert.Equal(1, WindowGeometry.Neighbor(Current, "right", Seen([windows[1], windows[0]]), SeenDistances.Default));
    }

    [Theory]
    [InlineData("left", "No window to the left.")]
    [InlineData("right", "No window to the right.")]
    [InlineData("up", "No window above.")]
    [InlineData("down", "No window below.")]
    public void WithNoWindowThatWayItFails(string direction, string failure)
    {
        var fake = new FakeWindowSystem();
        fake.Add(1, Current);
        Assert.Equal(failure, new WindowControl(fake).Run(new WindowCommand.Focus(direction))?.Message);
    }

    [Fact]
    public void AMinimizedWindowIsNoCandidate()
    {
        var fake = new FakeWindowSystem();
        fake.Add(2, new(1300, 400, 400, 300)).IsMinimized = true;
        fake.Add(1, Current);
        Assert.Equal("No window to the right.", new WindowControl(fake).Run(new WindowCommand.Focus("right"))?.Message);
    }

    [Fact]
    public void ItFocusesTheWindowItPicksAcrossMonitors()
    {
        var fake = new FakeWindowSystem();
        fake.Monitors.Add(new Screen(new(1920, 0, 2560, 1440), new(1920, 0, 2560, 1392), 1.25));
        fake.Add(2, new(2200, 300, 900, 700));
        fake.Add(1, new(900, 300, 900, 700));
        Assert.Null(new WindowControl(fake).Run(new WindowCommand.Focus("right")));
        Assert.Equal(2, fake.Front);
        Assert.Null(new WindowControl(fake).Run(new WindowCommand.Focus("left")));
        Assert.Equal(1, fake.Front);
    }

    [Fact]
    public void AFullScreenWindowCanBeFocusedAwayFrom()
    {
        var fake = new FakeWindowSystem();
        fake.Add(2, new(-1000, 0, 800, 600));
        var game = fake.Add(1, new(0, 0, 1920, 1080));
        game.IsFullScreen = true;
        Assert.Null(new WindowControl(fake).Run(new WindowCommand.Focus("left")));
    }

    [Theory]
    [InlineData("left")]
    [InlineData("down")]
    public void ItPlansAndWritesItsTarget(string direction)
    {
        var action = new FocusAction(direction);
        var family = ActionRegistry.Standard.Family("window.focus")!;
        Assert.Equal(new ActionPlan.Window(new WindowCommand.Focus(direction)), family.Plan(action, new PlanContext(@"C:\Users\me")));
        Assert.Equal($"target=\"{direction}\"", string.Join(" ", family.Write(action).Select(f => $"{f.Key}={f.Value.ToJsonString()}")));
    }
}
