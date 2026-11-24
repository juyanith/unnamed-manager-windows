using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

// S-082, S-083: dialogs (spec v21).
public class DialogTests
{
    [Theory]
    [InlineData(true, false, false, true, true, WindowKind.Standard)]
    [InlineData(true, false, false, false, true, WindowKind.Standard)]
    [InlineData(true, false, false, true, false, WindowKind.Standard)]
    [InlineData(true, true, false, true, true, WindowKind.Dialog)]
    [InlineData(true, true, false, true, false, WindowKind.None)]
    [InlineData(true, true, false, false, true, WindowKind.None)]
    [InlineData(true, true, true, true, true, WindowKind.None)]
    [InlineData(true, false, true, true, true, WindowKind.None)]
    [InlineData(false, true, false, true, true, WindowKind.None)]
    public void ADialogIsOwnedEnabledAndCaptioned(bool visible, bool owned, bool tool, bool enabled, bool caption, WindowKind kind) =>
        Assert.Equal(kind, WindowKinds.Of(visible, owned, tool, enabled, caption));

    private const nint Left = 1, Right = 2, Dialog = 3;

    /// Two windows side by side and a dialog above both, which is focused.
    private static (FakeWindowSystem Fake, WindowControl Control) WithDialog()
    {
        var fake = new FakeWindowSystem();
        var control = new WindowControl(fake);
        fake.Add(Left, new PixelRect(0, 0, 900, 1000)).Title = "Left";
        fake.Add(Right, new PixelRect(960, 0, 900, 1000)).Title = "Right";
        var dialog = fake.Add(Dialog, new PixelRect(700, 300, 400, 300));
        dialog.IsDialog = true;
        dialog.Title = "Options";
        fake.Click(Left);
        fake.Click(Right);
        fake.Click(Dialog);
        return (fake, control);
    }

    [Fact]
    public void ADialogIsACandidateForEveryFocusTarget()
    {
        WindowTarget[] targets =
        [
            .. new[] { "left", "next", "previous", "recent" }.Select(WindowTarget.Of),
            // Third in reading order: below the tops of the two windows beside it.
            WindowTarget.Of(3),
        ];
        foreach (var target in targets)
        {
            var (fake, control) = WithDialog();
            fake.Click(Right);
            var reached = false;
            for (var i = 0; i < 3 && !reached; i++)
            {
                control.Run(new WindowCommand.Focus(target));
                reached = fake.Front == Dialog;
            }
            Assert.True(reached, $"{target} never reaches the dialog");
        }
    }

    [Fact]
    public void ADialogIsAnItemAndGoesIntoTheHistory()
    {
        var (fake, control) = WithDialog();
        Assert.Equal(new WindowRef(Dialog), control.History[0]);
        fake.Click(Right);
        Assert.Equal("Options", control.RecentWindows(new ListSpec()).Items[0].Title);
    }

    [Fact]
    public void AWindowItsDialogBlocksStandsAsideForFocus()
    {
        var (fake, control) = WithDialog();
        fake.Windows[Right].Blocked = true;
        Assert.Equal(["Left"], control.RecentWindows(new ListSpec()).Items.Select(i => i.Title));
        Assert.Null(control.Run(new WindowCommand.Focus("recent")));
        Assert.Equal(Left, fake.Front);
    }

    [Fact]
    public void ABlockedWindowIsStillSwappedWith()
    {
        var (fake, control) = WithDialog();
        fake.Windows[Right].Blocked = true;
        fake.Click(Left);
        Assert.Null(control.Run(new WindowCommand.Swap("right")));
        Assert.Equal(960, fake.Windows[Left].Frame.X);
    }

    [Fact]
    public void NoSwapTargetPicksADialog()
    {
        WindowTarget[] targets =
        [
            .. new[] { "left", "right", "up", "down", "next", "previous", "first", "last", "recent" }.Select(WindowTarget.Of),
            WindowTarget.Of(1), WindowTarget.Of(2), WindowTarget.Of(3),
        ];
        foreach (var target in targets)
        {
            var (fake, control) = WithDialog();
            var before = fake.Windows[Dialog].Frame;
            fake.Click(Right);
            control.Run(new WindowCommand.Swap(target));
            Assert.Equal(before, fake.Windows[Dialog].Frame);
        }
    }

    [Fact]
    public void AListThatSwapsHasNoDialog()
    {
        var (fake, control) = WithDialog();
        fake.Click(Right);
        Assert.Equal(["Left"], control.RecentWindows(new ListSpec(Choose: "swap")).Items.Select(i => i.Title));
    }

    [Fact]
    public void WithADialogFocusedSwapMovesNothingAndAlerts()
    {
        var (fake, control) = WithDialog();
        var before = fake.Windows.ToDictionary(p => p.Key, p => p.Value.Frame);
        foreach (WindowCommand command in new WindowCommand[] { new WindowCommand.Swap("recent"), new WindowCommand.Choose(new WindowRef(Left), Swaps: true) })
        {
            Assert.Equal(new WindowFailure("A dialog is left out of Swap Window.", Alerts: true), control.Run(command));
            Assert.Equal(before, fake.Windows.ToDictionary(p => p.Key, p => p.Value.Frame));
        }
    }

    [Fact]
    public void ArrangeNeverCountsOrMovesADialog()
    {
        var (fake, control) = WithDialog();
        var before = fake.Windows[Dialog].Frame;
        Assert.Null(control.Run(new WindowCommand.Arrange(new ArrangeAction())));
        Assert.Equal(before, fake.Windows[Dialog].Frame);
        // Two cells for two windows, each on the side it was on (spec v9): none for the dialog.
        Assert.True(fake.Windows[Left].Frame.Right <= fake.Windows[Right].Frame.X);
        Assert.Equal(1920, fake.Windows[Right].Frame.Width + fake.Windows[Left].Frame.Width);
    }

    [Fact]
    public void PlaceStillWorksOnAFocusedDialog()
    {
        var (fake, control) = WithDialog();
        Assert.Null(control.Run(new WindowCommand.Place(PlaceAction.PresetFrame("leftHalf"))));
        Assert.Equal(new PixelRect(0, 0, 960, 1032), fake.Windows[Dialog].Frame);
    }
}
