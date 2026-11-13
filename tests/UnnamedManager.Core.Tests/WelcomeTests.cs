using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

// S-058: Welcome on first start.
public class WelcomeTests
{
    [Fact]
    public void ItShowsUntilYouRunOneSequence()
    {
        Assert.True(WelcomeStatus.ShowsAtStart(ranSequence: false));
        Assert.False(WelcomeStatus.ShowsAtStart(ranSequence: true));
    }

    [Fact]
    public void EachStepIsDoneOnItsOwn()
    {
        var start = new WelcomeStatus(OpenedConfig: false, OfficeShortcutsOn: true, RanSequence: false);
        Assert.False(start.OfficeDone || start.IsComplete);
        Assert.True((start with { OfficeShortcutsOn = false }).OfficeDone);
        Assert.True((start with { OfficeShortcutsOn = null }).OfficeDone);
        Assert.True(new WelcomeStatus(true, false, true).IsComplete);
        Assert.False(new WelcomeStatus(true, false, false).IsComplete);
    }

    [Fact]
    public void TheThirdStepNamesTheFirstLeaderAndItsMenu()
    {
        var starter = ConfigLoader.Load(ConfigLoader.StarterJson).Loaded;
        Assert.Equal(("Try Hyper+Space", "Then press a key from its Commands menu."), WelcomeStatus.TryStep(starter));
        Assert.Equal("Try your leader", WelcomeStatus.TryStep(null).Title);
    }
}
