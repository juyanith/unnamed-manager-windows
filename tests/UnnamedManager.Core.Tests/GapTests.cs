using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-030: Leave a gap around placed windows.
public class GapTests
{
    private static readonly Screen Screen = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1032));

    private static PixelRect Placed(string preset, double gap, Screen? screen = null)
    {
        var s = screen ?? Screen;
        return WindowGeometry.WithGap(WindowGeometry.Rect(PlaceAction.PresetFrame(preset), s.Work), s, gap);
    }

    [Fact]
    public void TheGapDefaultsTo8AndANegativeOneIsAnError()
    {
        Assert.Equal(8, new WindowSettings().Gap);
        var negative = ConfigLoader.Load("""{ "version": 1, "windows": { "gap": -1 }, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ], "actions": { "a": { "type": "noop" } } }""");
        Assert.Contains(negative.Errors, i => i.Rule == Rule.NegativeGap);
    }

    [Fact]
    public void WithAGapOf8TheHalvesStand8FromTheEdgesAndFromEachOther()
    {
        var left = Placed("leftHalf", 8);
        var right = Placed("rightHalf", 8);
        Assert.Equal(new PixelRect(8, 8, 948, 1016), left);
        Assert.Equal(new PixelRect(964, 8, 948, 1016), right);
        Assert.Equal(8, right.X - left.Right);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(7)]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(6.6)]
    public void NeighboursEndExactlyOneGapApartForEvenAndOddGaps(double gap)
    {
        var g = (int)Math.Round(gap, MidpointRounding.AwayFromZero);
        Assert.Equal(g, Placed("rightHalf", gap).X - Placed("leftHalf", gap).Right);
        Assert.Equal(g, Placed("centerThird", gap).X - Placed("leftThird", gap).Right);
        Assert.Equal(g, Placed("rightThird", gap).X - Placed("centerThird", gap).Right);
        Assert.Equal(g, Placed("bottomHalf", gap).Y - Placed("topHalf", gap).Bottom);
        Assert.Equal(g, Placed("bottomLeft", gap).Y - Placed("topLeft", gap).Bottom);
        Assert.Equal(g, Placed("leftThird", gap).X);
        Assert.Equal(g, Screen.Work.Right - Placed("rightThird", gap).Right);
    }

    [Fact]
    public void TheGapIsInPointsAtTheMonitorsScale()
    {
        var scaled = new Screen(new(0, 0, 2880, 1620), new(0, 0, 2880, 1548), 1.5);
        Assert.Equal(12, Placed("leftHalf", 8, scaled).X);
        Assert.Equal(12, Placed("rightHalf", 8, scaled).X - Placed("leftHalf", 8, scaled).Right);
    }

    [Fact]
    public void AWindowKeepsAtLeastOnePixel() =>
        Assert.Equal((1, 1), (WindowGeometry.WithGap(new(0, 0, 10, 10), Screen, 50).Width, WindowGeometry.WithGap(new(0, 0, 10, 10), Screen, 50).Height));

    [Fact]
    public void AWindowThatWontTakeItsPlacementStaysOneGapInFromTheEdge()
    {
        var fake = new FakeWindowSystem();
        var w = fake.Add(1, new(100, 100, 800, 600));
        w.MinSize = (1200, 400);
        new WindowControl(fake, () => new WindowSettings { Gap = 8 }).Run(new WindowCommand.Place(PlaceAction.PresetFrame("rightHalf")));
        Assert.Equal(new PixelRect(1920 - 8 - 1200, 8, 1200, 1016), w.Frame);
    }

    [Fact]
    public void FillLeavesTheGapToo()
    {
        var fake = new FakeWindowSystem();
        var w = fake.Add(1, new(100, 100, 800, 600));
        w.CanMaximize = false;
        new WindowControl(fake, () => new WindowSettings { Gap = 8 }).Run(new WindowCommand.Fill(true));
        Assert.Equal(new PixelRect(8, 8, 1904, 1016), w.Frame);
    }
}
