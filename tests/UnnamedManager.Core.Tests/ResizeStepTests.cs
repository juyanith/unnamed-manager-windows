using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-025: Grow or shrink the window through preset widths.
public class ResizeStepTests
{
    private static readonly Screen Screen = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1032));
    private static readonly double[] Defaults = ResizeAction.DefaultWidths;

    [Theory]
    [InlineData(800, true, 960)]    // between 1/3 (640) and 1/2: grow to 1/2
    [InlineData(800, false, 640)]   // shrink to 1/3
    [InlineData(960, true, 1280)]   // exactly 1/2 grows to 2/3
    [InlineData(961, true, 1280)]   // within the tolerance of 1/2 counts as it
    [InlineData(960, false, 640)]
    [InlineData(300, true, 480)]    // narrower than all: the first
    [InlineData(1800, false, 1440)] // wider than all: the last
    public void TheNextWidthIsTheNearestPresetThatWay(int width, bool grow, int next) =>
        Assert.Equal(next, WindowGeometry.Step(new(100, 50, width, 700), Screen, Defaults, grow)!.Value.Width);

    [Theory]
    [InlineData(1440, true)]
    [InlineData(480, false)]
    public void AtTheEndOfTheListItStaysPut(int width, bool grow) =>
        Assert.Null(WindowGeometry.Step(new(100, 50, width, 700), Screen, Defaults, grow));

    [Fact]
    public void ItKeepsItsLeftEdgeAndHeight()
    {
        var next = WindowGeometry.Step(new(100, 50, 800, 700), Screen, Defaults, true)!.Value;
        Assert.Equal(new PixelRect(100, 50, 960, 700), next);
    }

    [Fact]
    public void ItKeepsItsRightEdgeWhenTheLeftWouldPushItOffScreen()
    {
        var next = WindowGeometry.Step(new(1000, 50, 800, 700), Screen, Defaults, true)!.Value;
        Assert.Equal(new PixelRect(840, 50, 960, 700), next);
        var wide = WindowGeometry.Step(new(1300, 50, 600, 700), Screen, [1.0], true)!.Value;
        Assert.Equal(new PixelRect(0, 50, 1920, 700), wide);
    }

    [Fact]
    public void WithNoWidthsTheDefaultIsAQuarterToThreeQuarters()
    {
        Assert.Equal([0.25, 1.0 / 3, 0.5, 2.0 / 3, 0.75], ResizeAction.DefaultWidths);
        var fake = new FakeWindowSystem();
        var w = fake.Add(1, new(100, 50, 800, 700));
        var action = Read("""{ "type": "window.resize", "step": "grow" }""");
        Assert.Null(new WindowControl(fake).Run(Assert.IsType<WindowCommand.Resize>(Assert.IsType<ActionPlan.Window>(Plan(action)).Command)));
        Assert.Equal(960, w.Frame.Width);
    }

    [Fact]
    public void ItsOwnWidthsAreUsedInAnyOrder()
    {
        var fake = new FakeWindowSystem();
        var w = fake.Add(1, new(0, 0, 500, 700));
        var action = Read("""{ "type": "window.resize", "step": "grow", "widths": [0.9, 0.4] }""");
        var control = new WindowControl(fake);
        control.Run(((ActionPlan.Window)Plan(action)!).Command);
        Assert.Equal(768, w.Frame.Width);
        control.Run(((ActionPlan.Window)Plan(action)!).Command);
        Assert.Equal(1728, w.Frame.Width);
    }

    [Theory]
    [InlineData("""{ "type": "window.resize", "step": "shrink" }""", "step=\"shrink\"")]
    [InlineData("""{ "widths": [0.5, 1], "step": "grow", "type": "window.resize" }""", "step=\"grow\" widths=[0.5,1]")]
    [InlineData("""{ "type": "window.resize", "to": { "height": 1, "width": 0.5 }, "anchor": "center" }""", "to={\"width\":0.5,\"height\":1} anchor=\"center\"")]
    [InlineData("""{ "type": "window.resize", "by": { "dw": 0, "dh": -40 } }""", "by={\"dh\":-40}")]
    public void ItWritesItsFieldsWithDefaultsLeftOut(string json, string written) =>
        Assert.Equal(written, string.Join(" ", ActionRegistry.Standard.Family("window.resize")!.Write(Read(json)).Select(f => $"{f.Key}={f.Value.ToJsonString()}")));

    private static ConfigAction Read(string json) => ConfigLoader.Load($$"""
        { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ], "actions": { "a": {{json}} } }
        """).Loaded!.Config.Action("a")!;

    private static ActionPlan? Plan(ConfigAction action) => ActionRegistry.Standard.Family(action.Type)!.Plan(action, new PlanContext(@"C:\Users\me"));
}
