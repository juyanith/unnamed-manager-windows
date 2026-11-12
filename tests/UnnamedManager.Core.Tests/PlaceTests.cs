using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-024: Place the window on a half, third, quarter or corner.
public class PlaceTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1032);

    [Theory]
    [InlineData("leftHalf", 0, 0, 960, 1032)]
    [InlineData("rightHalf", 960, 0, 960, 1032)]
    [InlineData("topHalf", 0, 0, 1920, 516)]
    [InlineData("bottomHalf", 0, 516, 1920, 516)]
    [InlineData("leftThird", 0, 0, 640, 1032)]
    [InlineData("centerThird", 640, 0, 640, 1032)]
    [InlineData("rightThird", 1280, 0, 640, 1032)]
    [InlineData("leftTwoThirds", 0, 0, 1280, 1032)]
    [InlineData("rightTwoThirds", 640, 0, 1280, 1032)]
    [InlineData("topLeft", 0, 0, 960, 516)]
    [InlineData("topRight", 960, 0, 960, 516)]
    [InlineData("bottomLeft", 0, 516, 960, 516)]
    [InlineData("bottomRight", 960, 516, 960, 516)]
    [InlineData("center", 320, 172, 1280, 688)]
    public void EachPresetMapsToItsFrame(string preset, int x, int y, int w, int h) =>
        Assert.Equal(new PixelRect(x, y, w, h), WindowGeometry.Rect(PlaceAction.PresetFrame(preset), Work));

    public static TheoryData<PixelRect> WorkAreas() => new(
        new PixelRect(0, 0, 1920, 1032),
        new PixelRect(0, 0, 2879, 1547),          // odd sizes, 150 %
        new PixelRect(-1366, 0, 1366, 728),       // left of the primary
        new PixelRect(0, -1393, 2559, 1393),      // above it
        new PixelRect(3840, 200, 1001, 777),      // right of it, lower down
        new PixelRect(-500, 1080, 1279, 1001));   // below, partly left

    [Theory]
    [MemberData(nameof(WorkAreas))]
    public void NeighboursShareAnEdgeWithNoGapOrOverlap(PixelRect work)
    {
        PixelRect R(string p) => WindowGeometry.Rect(PlaceAction.PresetFrame(p), work);
        Assert.Equal(R("leftHalf").Right, R("rightHalf").X);
        Assert.Equal(R("topHalf").Bottom, R("bottomHalf").Y);
        Assert.Equal(R("leftThird").Right, R("centerThird").X);
        Assert.Equal(R("centerThird").Right, R("rightThird").X);
        Assert.Equal(R("leftTwoThirds").Right, R("rightThird").X);
        Assert.Equal(R("leftThird").Right, R("rightTwoThirds").X);
        Assert.Equal(R("topLeft").Right, R("topRight").X);
        Assert.Equal(R("topLeft").Bottom, R("bottomLeft").Y);
        Assert.Equal(work.X, R("leftHalf").X);
        Assert.Equal(work.Right, R("rightThird").Right);
        Assert.Equal(work.Bottom, R("bottomRight").Bottom);
        Assert.Equal(work.Y, R("topRight").Y);
    }

    private static (FakeWindowSystem, WindowControl, FakeWindowSystem.Window) Setup(params Screen[] screens)
    {
        var fake = new FakeWindowSystem();
        if (screens.Length > 0) { fake.Monitors.Clear(); fake.Monitors.AddRange(screens); }
        var w = fake.Add(1, new(100, 100, 800, 600));
        return (fake, new WindowControl(fake), w);
    }

    [Fact]
    public void ItPlacesOnTheMonitorWithMostOfTheWindow()
    {
        var primary = new Screen(new(0, 0, 1920, 1080), new(0, 0, 1920, 1032));
        var left = new Screen(new(-2880, -200, 2880, 1620), new(-2880, -200, 2880, 1572), 1.5);
        var (_, control, w) = Setup(primary, left);
        w.Frame = new(-1500, 100, 800, 600);
        control.Run(new WindowCommand.Place(PlaceAction.PresetFrame("rightHalf")));
        Assert.Equal(new PixelRect(-1440, -200, 1440, 1572), w.Frame);
    }

    [Fact]
    public void AWindowWithALargerMinimumKeepsTheEdgeOfThePlacement()
    {
        var (_, control, w) = Setup();
        w.MinSize = (1200, 400);
        control.Run(new WindowCommand.Place(PlaceAction.PresetFrame("rightHalf")));
        Assert.Equal(new PixelRect(720, 0, 1200, 1032), w.Frame);
        control.Run(new WindowCommand.Place(PlaceAction.PresetFrame("leftHalf")));
        Assert.Equal(new PixelRect(0, 0, 1200, 1032), w.Frame);
    }

    [Fact]
    public void AWindowWithASmallerMaximumIsCenteredInACenteredPlacement()
    {
        var (_, control, w) = Setup();
        w.MaxSize = (1000, 600);
        control.Run(new WindowCommand.Place(PlaceAction.PresetFrame("center")));
        Assert.Equal(new PixelRect(460, 216, 1000, 600), w.Frame);
    }

    [Fact]
    public void AMaximizedWindowIsPlacedToo()
    {
        var (fake, control, w) = Setup();
        fake.Maximize(new WindowRef(1));
        control.Run(new WindowCommand.Place(PlaceAction.PresetFrame("leftHalf")));
        Assert.False(w.IsMaximized);
        Assert.Equal(new PixelRect(0, 0, 960, 1032), w.Frame);
    }

    [Theory]
    [InlineData("""{ "type": "window.place", "preset": "leftHalf" }""", "preset=\"leftHalf\"")]
    [InlineData("""{ "type": "window.place", "frame": { "y": 0, "x": 0.25, "height": 1, "width": 0.5 } }""", "frame={\"x\":0.25,\"y\":0,\"width\":0.5,\"height\":1}")]
    public void ItWritesAPresetOrAFrame(string json, string written)
    {
        var action = ConfigLoader.Load($$"""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ], "actions": { "a": {{json}} } }
            """).Loaded!.Config.Action("a")!;
        var family = ActionRegistry.Standard.Family("window.place")!;
        Assert.Equal(written, string.Join(" ", family.Write(action).Select(f => $"{f.Key}={f.Value.ToJsonString()}")));
        Assert.IsType<WindowCommand.Place>(Assert.IsType<ActionPlan.Window>(family.Plan(action, new PlanContext(@"C:\Users\me"))).Command);
    }
}
