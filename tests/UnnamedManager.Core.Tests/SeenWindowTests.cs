using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// Focus and swap only the windows you can see, in reading order (spec v12). Ported from
/// the Mac's SeenWindowTests.
public class SeenWindowTests
{
    private static readonly Screen[] Screens = [new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1080))];

    /// Windows front to back, each with what the ones before it leave showing.
    private static SeenWindow[] Stacked(params PixelRect[] frames)
    {
        var parts = WindowGeometry.VisibleParts(frames);
        return [.. frames.Select((f, i) => new SeenWindow(f, parts[i]))];
    }

    private static SeenWindow[] Seen(params PixelRect[] frames) => [.. frames.Select(f => new SeenWindow(f))];

    private static IReadOnlyList<int> Order(SeenWindow[] windows, int rowTolerance = 20) =>
        WindowGeometry.CycleOrder(windows, Screens, SeenDistances.Default with { RowTolerance = rowTolerance });

    private static int? Neighbor(PixelRect current, string direction, SeenWindow[] windows, SeenDistances? distances = null) =>
        WindowGeometry.Neighbor(current, direction, windows, distances ?? SeenDistances.Default);

    [Fact]
    public void AWindowsVisiblePartIsItsFrameMinusTheWindowsInFront()
    {
        PixelRect front = new(0, 0, 960, 1080);
        var parts = WindowGeometry.VisibleParts([front, front, new PixelRect(480, 0, 960, 1080)]);
        Assert.Equal([front], parts[0]);
        Assert.Empty(parts[1]);
        Assert.Equal([new PixelRect(960, 0, 480, 1080)], parts[2]);
    }

    [Fact]
    public void AWindowCountsWhenItsPartsHoldTheSquareTogether()
    {
        // A hole in the right of the window leaves three thin parts that hold a 50 pixel square together.
        var parts = WindowGeometry.VisibleParts([new PixelRect(50, 20, 100, 20), new PixelRect(0, 0, 100, 60)])[1];
        Assert.Equal(3, parts.Count);
        Assert.True(WindowGeometry.Shows(parts, 40));
        Assert.True(WindowGeometry.Shows(parts, 50));
        Assert.False(WindowGeometry.Shows(parts, 55));
    }

    [Fact]
    public void AThinStripDoesntCountUnlessMinVisibleIs0()
    {
        var parts = WindowGeometry.VisibleParts([new PixelRect(0, 0, 490, 500), new PixelRect(0, 0, 500, 500)])[1];
        Assert.False(WindowGeometry.Shows(parts, 20));
        Assert.True(WindowGeometry.Shows(parts, 10));
        Assert.True(WindowGeometry.Shows(parts, 0));
        Assert.False(WindowGeometry.Shows([], 0));
    }

    [Fact]
    public void DownFromTheTopHalfFindsAQuarterWidthWindowBelow()
    {
        PixelRect current = new(0, 0, 1920, 540);
        Assert.Equal(0, Neighbor(current, "down", Seen(new PixelRect(0, 540, 480, 540), new PixelRect(1440, 540, 480, 540))));
        Assert.Equal(0, Neighbor(current, "down", Seen(new PixelRect(1440, 540, 480, 540))));
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("up")]
    [InlineData("down")]
    public void FromAWindowInsideALargerOneEveryDirectionGoesToIt(string direction)
    {
        PixelRect small = new(500, 300, 400, 300);
        Assert.Equal(0, Neighbor(small, direction, [Stacked(small, new PixelRect(0, 0, 1920, 1080))[1]]));
    }

    [Fact]
    public void OnlyThePartPastTheCenterCounts()
    {
        PixelRect current = new(500, 0, 500, 500);
        var behind = Stacked(current, new PixelRect(300, 0, 500, 500))[1];
        Assert.Null(Neighbor(current, "right", [behind]));
        Assert.Equal(0, Neighbor(current, "left", [behind]));
    }

    [Fact]
    public void AWindowTouchingTheSideBeatsOneFurtherAway()
    {
        PixelRect current = new(0, 0, 500, 500);
        var windows = Seen(new PixelRect(600, 0, 500, 500), new PixelRect(500, 450, 300, 300));
        Assert.Equal(1, Neighbor(current, "right", windows));
        Assert.Equal(0, Neighbor(current, "right", windows, SeenDistances.Default with { MinVisible = 60 }));
    }

    [Fact]
    public void GapsWithinTieToleranceTieAndTheOneSharingMoreWins()
    {
        PixelRect current = new(0, 0, 500, 500);
        var windows = Seen(new PixelRect(500, 0, 300, 100), new PixelRect(502, 100, 300, 400));
        Assert.Equal(1, Neighbor(current, "right", windows));
        Assert.Equal(0, Neighbor(current, "right", windows, SeenDistances.Default with { TieTolerance = 0 }));
    }

    [Fact]
    public void AThreeByTwoGridReadsAcrossTheTopFirst()
    {
        var cells = Enumerable.Range(0, 6).Select(i => new PixelRect(i % 3 * 640, i / 3 * 540, 640, 540)).ToArray();
        int[] shuffled = [4, 0, 5, 2, 1, 3];
        Assert.Equal([0, 1, 2, 3, 4, 5], Order(Seen([.. shuffled.Select(i => cells[i])])).Select(i => shuffled[i]));
    }

    [Fact]
    public void AFullHeightWindowOnTheLeftComesBeforeTwoStackedOnTheRight() =>
        Assert.Equal([2, 1, 0], Order(Seen(new PixelRect(960, 540, 960, 540), new PixelRect(960, 0, 960, 540), new PixelRect(0, 0, 960, 1080))));

    [Fact]
    public void TopsWithinRowToleranceShareARow()
    {
        var windows = Seen(new PixelRect(0, 10, 500, 500), new PixelRect(600, 0, 500, 500));
        Assert.Equal([0, 1], Order(windows));
        Assert.Equal([1, 0], Order(windows, rowTolerance: 5));
    }

    [Fact]
    public void AWindowsPlaceInTheOrderIsTheTopLeftOfWhatShows() =>
        Assert.Equal([0, 1, 2], Order(Stacked(new PixelRect(0, 0, 500, 200), new PixelRect(600, 100, 500, 500), new PixelRect(0, 0, 500, 500))));

    [Fact]
    public void TheDistancesScaleWithTheMonitor() =>
        Assert.Equal(new SeenDistances(30, 30, 3), SeenDistances.From(new WindowSettings(), new Screen(default, default, 1.5)));

    [Fact]
    public void TheSettingsDefaultTo20And20And2AndCantBeNegative()
    {
        const string start = """{ "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "noop" } ]""";
        var plain = ConfigLoader.Load(start + " }").Loaded!.Config.Windows;
        Assert.Equal((20.0, 20.0, 2.0), (plain.MinVisible, plain.RowTolerance, plain.TieTolerance));
        var changed = ConfigLoader.Load(start + """, "windows": { "minVisible": 40, "rowTolerance": 12.5, "tieTolerance": 0 } }""").Loaded!.Config;
        Assert.Contains("""  "windows": { "minVisible": 40, "rowTolerance": 12.5, "tieTolerance": 0 }""", ConfigWriter.Write(changed));
        foreach (var name in new[] { "minVisible", "rowTolerance", "tieTolerance" })
            Assert.Contains(ConfigLoader.Load(start + $$""", "windows": { "{{name}}": -1 } }""").Errors, e => e.Rule == Rule.NegativeWindowDistance);
    }
}
