using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-034: Choose where the Guide appears.
public class GuidePlacementTests
{
    private static readonly Screen Primary = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1032));
    private static readonly Screen Left = new(new(-2880, -200, 2880, 1620), new(-2880, -200, 2880, 1548), 1.5);

    [Fact]
    public void TheGuideGoesOnTheMonitorOfTheWindowInFrontNotThePrimaryOne()
    {
        Assert.Equal(Left, GuidePlacement.ScreenFor(new PixelRect(-2000, 100, 800, 600), [Primary, Left]));
        Assert.Equal(Primary, GuidePlacement.ScreenFor(new PixelRect(100, 100, 800, 600), [Primary, Left]));
    }

    [Fact]
    public void WithNoWindowInFrontItGoesOnThePrimaryMonitor() =>
        Assert.Equal(Primary, GuidePlacement.ScreenFor(null, [Primary, Left]));

    [Theory]
    [InlineData("center", 760, 416)]
    [InlineData("Center", 760, 416)]
    [InlineData("top", 760, 48)]
    [InlineData("BOTTOM", 760, 1032 - 200 - 48)]
    public void CenterTopAndBottomOfTheWorkArea(string position, int x, int y) =>
        Assert.Equal((x, y), GuidePlacement.Origin(400, 200, Primary, position, 48));

    [Fact]
    public void OnAScaledMonitorTheEdgeIsInPointsAndTheOriginIsOnThatMonitor()
    {
        Assert.Equal((-2880 + (2880 - 600) / 2, -200 + 72), GuidePlacement.Origin(600, 300, Left, "top", 48));
        Assert.Equal(-200 + 1548 - 300 - 72, GuidePlacement.Origin(600, 300, Left, "bottom", 48).Y);
    }

    [Fact]
    public void TheGuideIsAtMost80PercentOfTheWorkAreaWideInPoints()
    {
        Assert.Equal(1536, GuidePlacement.MaxWidthPoints(Primary));
        Assert.Equal(1536, GuidePlacement.MaxWidthPoints(Left));
    }
}
