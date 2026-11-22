using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

// S-085: "scope": "display" with nothing focused.
public class DisplayScopeTests
{
    private static (FakeWindowSystem Fake, WindowControl Control) TwoDisplays()
    {
        var fake = new FakeWindowSystem();
        fake.Monitors.Add(new Screen(new(1920, 0, 1920, 1080), new(1920, 0, 1920, 1032)));
        var control = new WindowControl(fake);
        fake.Add(1, new PixelRect(100, 100, 600, 400)).Title = "Main A";
        fake.Add(2, new PixelRect(2100, 100, 600, 400)).Title = "Side A";
        fake.Add(3, new PixelRect(2800, 100, 600, 400)).Title = "Side B";
        fake.Front = null;
        return (fake, control);
    }

    private static string Items(WindowControl control) =>
        string.Join(",", control.RecentWindows(new ListSpec(Scope: "display")).Items.Select(i => i.Title).Order());

    [Fact]
    public void ItUsesTheDisplayOfTheCandidateInFront()
    {
        var (fake, control) = TwoDisplays();
        Assert.Equal("Side A,Side B", Items(control));
        fake.Click(1);
        fake.Front = null;
        Assert.Equal("Main A", Items(control));
    }

    [Fact]
    public void AWindowThatCantTakeFocusDoesntDecideTheDisplay()
    {
        var (fake, control) = TwoDisplays();
        fake.Click(1);
        fake.Front = null;
        fake.Windows[1].Blocked = true;
        Assert.Equal("Side A,Side B", Items(control));
    }

    [Fact]
    public void WithNoCandidateTheListIsEmptyAndNothingFails()
    {
        var (fake, control) = TwoDisplays();
        foreach (var window in fake.Windows.Values) window.IsMinimized = true;
        Assert.Equal("", Items(control));
    }
}
