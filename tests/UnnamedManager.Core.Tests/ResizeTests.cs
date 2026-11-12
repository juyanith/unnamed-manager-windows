using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-027: Resize the window by a step or to an exact size.
public class ResizeTests
{
    private static readonly Screen Screen = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1032));
    private static readonly PixelRect Window = new(400, 200, 800, 600);

    private static ResizeAction To(double w, double h, string anchor = "topLeft") => new("to", w, h, null, null, anchor);
    private static ResizeAction By(double dw, double dh, string anchor = "topLeft") => new("by", dw, dh, null, null, anchor);

    private static PixelRect Run(ResizeAction resize, PixelRect? frame = null, Screen? screen = null)
    {
        var s = screen ?? Screen;
        var f = frame ?? Window;
        return WindowGeometry.Resized(f, WindowGeometry.Size(f, s, resize), resize.Anchor, s.Work);
    }

    [Theory]
    [InlineData("topLeft", 400, 200)]
    [InlineData("top", 300, 200)]
    [InlineData("topRight", 200, 200)]
    [InlineData("left", 400, 100)]
    [InlineData("center", 300, 100)]
    [InlineData("right", 200, 100)]
    [InlineData("bottomLeft", 400, 0)]
    [InlineData("bottom", 300, 0)]
    [InlineData("bottomRight", 200, 0)]
    public void ByChangesTheSizeAndTheAnchorStaysPut(string anchor, int x, int y) =>
        Assert.Equal(new PixelRect(x, y, 1000, 800), Run(By(200, 200, anchor)));

    [Fact]
    public void ToSetsFractionsOfTheWorkArea() =>
        Assert.Equal(new PixelRect(400, 200, 960, 516), Run(To(0.5, 0.5)));

    [Fact]
    public void ByIsInPointsAtTheMonitorsScale()
    {
        var scaled = new Screen(new(0, 0, 2880, 1620), new(0, 0, 2880, 1548), 1.5);
        Assert.Equal(new PixelRect(400, 200, 860, 570), Run(By(40, -20), screen: scaled));
    }

    [Fact]
    public void TheResultIsClampedToTheWorkArea()
    {
        Assert.Equal(new PixelRect(0, 0, 1920, 1032), Run(By(5000, 5000)));
        Assert.Equal(new PixelRect(400, 200, 1, 1), Run(By(-5000, -5000)));
        // Growing from the top left past the right edge moves it back inside.
        Assert.Equal(new PixelRect(920, 200, 1000, 600), Run(By(200, 0), new(1100, 200, 800, 600)));
    }

    [Fact]
    public void AFixedSizeWindowStaysAsItWasWithNoFailure()
    {
        var fake = new FakeWindowSystem();
        var w = fake.Add(1, Window);
        w.IsFixedSize = true;
        Assert.Null(new WindowControl(fake).Run(new WindowCommand.Resize(By(200, 200, "center"))));
        Assert.Equal(Window, w.Frame);
    }

    [Fact]
    public void AWindowWithAMinimumKeepsItsAnchorAtTheSizeItTook()
    {
        var fake = new FakeWindowSystem();
        var w = fake.Add(1, Window);
        w.MinSize = (700, 500);
        new WindowControl(fake).Run(new WindowCommand.Resize(By(-400, -400, "bottomRight")));
        Assert.Equal(new PixelRect(500, 300, 700, 500), w.Frame);
    }
}
