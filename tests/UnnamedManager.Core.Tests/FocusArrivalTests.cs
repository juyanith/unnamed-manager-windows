using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

// S-084: focus that doesn't arrive is a failure; focus that's late isn't.
public class FocusArrivalTests
{
    private static (FakeWindowSystem Fake, WindowControl Control) ThreeWindows()
    {
        var fake = new FakeWindowSystem();
        var control = new WindowControl(fake);
        for (var h = 1; h <= 3; h++)
        {
            fake.Add(h, new PixelRect(600 * (h - 1), 0, 500, 500)).App = $"App{h}";
            fake.Click(h);
        }
        return (fake, control);
    }

    public static TheoryData<string> Ways() => ["recent", "chosen"];

    private static WindowCommand Command(string way) =>
        way == "recent" ? new WindowCommand.Focus("recent") : new WindowCommand.Choose(new WindowRef(2), Swaps: false);

    [Theory]
    [MemberData(nameof(Ways))]
    public async Task WhenItDoesntArriveItSaysSoAndTheHistoryIsAsItWas(string way)
    {
        var (fake, control) = ThreeWindows();
        fake.Windows[2].CantFocus = true;
        var history = control.History.ToList();
        Assert.Equal(new WindowFailure("Couldn't focus App2."), await control.RunAsync(Command(way)));
        Assert.Equal(history, control.History);
    }

    [Theory]
    [MemberData(nameof(Ways))]
    public async Task LateFocusIsNoFailure(string way)
    {
        var (fake, control) = ThreeWindows();
        fake.Windows[2].FocusAfterChecks = 4;
        Assert.Null(await control.RunAsync(Command(way)));
        Assert.Equal(new WindowRef(2), control.History[0]);
    }

    [Fact]
    public async Task AWindowFocusedMeanwhileStaysFirstInTheHistory()
    {
        var (fake, control) = ThreeWindows();
        fake.Windows[2].CantFocus = true;
        var running = control.RunAsync(Command("chosen"));
        fake.Click(1);
        Assert.NotNull(await running);
        Assert.Equal([new WindowRef(1), new WindowRef(3), new WindowRef(2)], control.History);
    }

    [Fact]
    public async Task FocusThatArrivedAndMovedOnIsNoFailure()
    {
        var (fake, control) = ThreeWindows();
        fake.Windows[2].CantFocus = true;
        var running = control.RunAsync(Command("chosen"));
        fake.Windows[2].CantFocus = false;
        fake.Click(2);
        fake.Click(1);
        Assert.Null(await running);
    }
}
