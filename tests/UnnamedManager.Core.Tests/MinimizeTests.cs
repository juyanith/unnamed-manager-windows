using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-023: Minimize the window in front.
public class MinimizeTests
{
    [Fact]
    public void ItMinimizesTheWindowInFront()
    {
        var fake = new FakeWindowSystem();
        var w = fake.Add(1, new(100, 100, 800, 600));
        Assert.Null(new WindowControl(fake).Run(new WindowCommand.Minimize()));
        Assert.True(w.IsMinimized);
    }

    [Fact]
    public void NoWindowInFrontFails()
    {
        var fake = new FakeWindowSystem { FrontAppName = "The taskbar" };
        Assert.Equal("The taskbar has no window to minimize.", new WindowControl(fake).Run(new WindowCommand.Minimize()));
    }

    [Fact]
    public void ItPlansMinimize()
    {
        var action = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ], "actions": { "a": { "type": "window.minimize" } } }
            """).Loaded!.Config.Action("a")!;
        Assert.Equal(new ActionPlan.Window(new WindowCommand.Minimize()), ActionRegistry.Standard.Family("window.minimize")!.Plan(action, new PlanContext(@"C:\Users\me")));
    }
}
