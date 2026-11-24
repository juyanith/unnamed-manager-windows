using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;
using WindowFacts = UnnamedManager.Core.Config.WindowFacts;

namespace UnnamedManager.Core.Tests;

public class WindowPickerTests
{
    private static WindowFacts W(nint handle, bool app = true, bool minimized = false, bool tool = false, bool cloaked = false,
        bool owned = false, bool ours = false, bool hung = false, bool visible = true, bool elsewhere = false, bool dialog = false, bool blocked = false) =>
        new(handle, app, visible, minimized, tool,
            cloaked ? WindowPresence.Nowhere : elsewhere ? WindowPresence.Elsewhere : WindowPresence.Here,
            dialog ? WindowKind.Dialog : owned ? WindowKind.None : WindowKind.Standard, ours, hung, blocked);

    // S-082
    [Fact]
    public void ADialogWinsOverTheWindowItBlocks()
    {
        Assert.Equal(3, WindowPicker.Pick([W(3, dialog: true), W(2, blocked: true)]));
        Assert.Equal(3, WindowPicker.Pick([W(3, dialog: true), W(2, blocked: true)], [new(2), new(3)]));
    }

    // S-080
    [Fact]
    public void TheWindowUsedLastWinsOnWhateverDesktop()
    {
        Assert.Equal(2, WindowPicker.Pick([W(1), W(2, elsewhere: true)], [new(2), new(1)]));
        Assert.Equal(1, WindowPicker.Pick([W(1), W(2, elsewhere: true)], [new(1), new(2)]));
        Assert.Equal(2, WindowPicker.Pick([W(2, elsewhere: true)]));
        Assert.Equal(1, WindowPicker.Pick([W(1), W(2, elsewhere: true)]));
    }

    [Fact]
    public void UsedLastDoesntBeatOpenOrMakeASuspendedWindowCount()
    {
        Assert.Equal(1, WindowPicker.Pick([W(1, elsewhere: true), W(2, minimized: true)], [new(2), new(1)]));
        Assert.Equal(2, WindowPicker.Pick([W(2, minimized: true), W(3, minimized: true)]));
        Assert.Equal(0, WindowPicker.Pick([W(5, cloaked: true)], [new(5)]));
    }

    [Fact]
    public void TheAppsHighestWindowInZOrderWins() =>
        Assert.Equal(2, WindowPicker.Pick([W(1, app: false), W(2), W(3)]));

    [Fact]
    public void AMinimizedWindowOnlyWinsWhenNoOtherIsOpen()
    {
        Assert.Equal(3, WindowPicker.Pick([W(1, app: false), W(2, minimized: true), W(3)]));
        Assert.Equal(2, WindowPicker.Pick([W(2, minimized: true), W(4, minimized: true)]));
    }

    [Theory]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, false, false, true, false, false)]
    [InlineData(false, false, false, false, true, false)]
    [InlineData(false, false, false, false, false, true)]
    public void ToolCloakedOwnedOursHungAndHiddenWindowsAreSkipped(bool tool, bool cloaked, bool owned, bool ours, bool hung, bool hidden) =>
        Assert.Equal(9, WindowPicker.Pick([W(5, tool: tool, cloaked: cloaked, owned: owned, ours: ours, hung: hung, visible: !hidden), W(9)]));

    [Fact]
    public void NoWindowMeansTheAppIsntRunning() =>
        Assert.Equal(0, WindowPicker.Pick([W(1, app: false), W(2, tool: true)]));
}
