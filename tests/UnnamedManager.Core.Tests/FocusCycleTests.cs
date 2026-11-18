using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-029: Focus the next or previous window.
public class FocusCycleTests
{
    private static readonly Screen Left = new(new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1032));
    private static readonly Screen Primary = new(new(0, 0, 2560, 1440), new(0, 0, 2560, 1392), 1.25);

    private static FakeWindowSystem Grid()
    {
        var fake = new FakeWindowSystem();
        fake.Monitors.Clear();
        fake.Monitors.AddRange([Primary, Left]);
        fake.Add(10, new(1300, 700, 800, 400));   // primary, bottom right
        fake.Add(11, new(100, 100, 800, 400));    // primary, top left
        fake.Add(12, new(-1800, 600, 800, 400));  // left monitor, bottom
        fake.Add(13, new(1300, 100, 800, 400));   // primary, top right
        fake.Add(14, new(-1800, 100, 800, 400));  // left monitor, top
        fake.Add(15, new(100, 700, 800, 400));    // primary, bottom left
        return fake;
    }

    private static List<nint> Order(FakeWindowSystem fake)
    {
        var all = fake.Focusable();
        return [.. WindowGeometry.CycleOrder([.. all.Select(w => new SeenWindow(w.Frame, w.Owner))], fake.Monitors, SeenDistances.Default).Select(i => all[i].Window.Handle)];
    }

    [Fact]
    public void TheOrderIsByMonitorLeftToRightThenInRows() =>
        Assert.Equal([14, 12, 11, 13, 15, 10], Order(Grid()));

    [Fact]
    public void TheSameWindowsAlwaysGiveTheSameOrder()
    {
        var fake = Grid();
        var reversed = new FakeWindowSystem();
        reversed.Monitors.Clear();
        reversed.Monitors.AddRange([Left, Primary]);
        foreach (var (h, w) in fake.Windows.Reverse()) reversed.Add(h, w.Frame);
        Assert.Equal(Order(fake), Order(reversed));
    }

    [Fact]
    public void WindowsInTheSamePlaceAreOrderedByOwnerThenSize()
    {
        var fake = new FakeWindowSystem();
        fake.Add(2, new(100, 100, 800, 600)).Owner = 9;
        fake.Add(1, new(150, 150, 700, 500)).Owner = 9;
        fake.Add(3, new(100, 100, 800, 600)).Owner = 4;
        Assert.Equal([3, 2, 1], Order(fake));
    }

    [Fact]
    public void NextAndPreviousWrapAtTheEnds()
    {
        var fake = Grid();
        var control = new WindowControl(fake);
        fake.Front = 10;
        control.Run(new WindowCommand.Focus("next"));
        Assert.Equal(14, fake.Front);
        control.Run(new WindowCommand.Focus("previous"));
        Assert.Equal(10, fake.Front);
        control.Run(new WindowCommand.Focus("previous"));
        Assert.Equal(15, fake.Front);
    }

    [Fact]
    public void ItsOrderDoesntDependOnWhichWindowWasFocusedLast()
    {
        var fake = Grid();
        var control = new WindowControl(fake);
        fake.Front = 11;
        control.Run(new WindowCommand.Focus("next"));
        Assert.Equal(13, fake.Front);
        fake.Front = 15;
        control.Run(new WindowCommand.Focus("previous"));
        Assert.Equal(13, fake.Front);
    }

    [Fact]
    public void WithNoWindowInFrontNextAndPreviousGoToTheWindowInFront()
    {
        var fake = Grid();
        fake.Front = null;
        new WindowControl(fake).Run(new WindowCommand.Focus("next"));
        Assert.Equal(15, fake.Front);
        fake.Front = null;
        new WindowControl(fake).Run(new WindowCommand.Focus("previous"));
        Assert.Equal(15, fake.Front);
    }

    [Fact]
    public void WithNoOtherWindowItFails()
    {
        var fake = new FakeWindowSystem();
        fake.Add(1, new(0, 0, 800, 600));
        Assert.Equal("No other window to focus.", new WindowControl(fake).Run(new WindowCommand.Focus("next"))?.Message);
    }
}
