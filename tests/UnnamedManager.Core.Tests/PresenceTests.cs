using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

// S-078: cloaked by the shell doesn't mean on another desktop.
public class PresenceTests
{
    [Theory]
    [InlineData(0, true, WindowPresence.Here)]
    [InlineData(0, null, WindowPresence.Here)]
    [InlineData(0, false, WindowPresence.Elsewhere)]
    [InlineData(2, false, WindowPresence.Elsewhere)]
    [InlineData(2, true, WindowPresence.Nowhere)]
    [InlineData(2, null, WindowPresence.Nowhere)]
    [InlineData(1, false, WindowPresence.Nowhere)]
    [InlineData(4, true, WindowPresence.Nowhere)]
    public void ShellCloakedCountsOnlyWhenWindowsSaysAnotherDesktop(int cloaked, bool? onCurrent, WindowPresence presence) =>
        Assert.Equal(presence, Presence.Of(cloaked, onCurrent));

    private static (FakeWindowSystem, WindowControl) WithThree()
    {
        var fake = new FakeWindowSystem { DesktopCount = 2 };
        var control = new WindowControl(fake);
        for (var h = 1; h <= 3; h++) fake.Add(h, new PixelRect(100 * h, 100, 400, 300)).Title = $"W{h}";
        fake.Click(1);
        fake.Click(2);
        fake.Click(3);
        return (fake, control);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASuspendedWindowIsNoItemAndNeverTheWindowUsedBefore(bool desktopUnknown)
    {
        var (fake, control) = WithThree();
        fake.Windows[2].Cloaked = 2;
        fake.Windows[2].DesktopUnknown = desktopUnknown;
        Assert.Equal(["W1"], control.RecentWindows(new ListSpec()).Items.Select(i => i.Title));
        Assert.Null(control.Run(new WindowCommand.Focus("recent")));
        Assert.Equal((nint)1, fake.Front);
    }

    [Fact]
    public void AWindowOnAnotherDesktopIsListedMarkedAndGoneTo()
    {
        var (fake, control) = WithThree();
        fake.Windows[2].Cloaked = 2;
        fake.Windows[2].Desktop = 1;
        var first = control.RecentWindows(new ListSpec()).Items[0];
        Assert.Equal("W2", first.Title);
        Assert.True(first.Marks.HasFlag(ItemMarks.OtherDesktop));
        Assert.Null(control.Run(new WindowCommand.Focus("recent")));
        Assert.Equal(((nint)2, 1), (fake.Front!.Value, fake.CurrentDesktop));
    }
}
