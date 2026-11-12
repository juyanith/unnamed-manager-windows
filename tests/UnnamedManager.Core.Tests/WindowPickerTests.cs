using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

public class WindowPickerTests
{
    private static WindowFacts W(nint handle, bool app = true, bool minimized = false, bool tool = false, bool cloaked = false,
        bool owned = false, bool ours = false, bool hung = false, bool visible = true) =>
        new(handle, app, visible, minimized, tool, cloaked, owned, ours, hung);

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
