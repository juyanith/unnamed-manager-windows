using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-056: an app running as administrator is in front.
public class ElevationTests
{
    private static (FakeWindowSystem Fake, WindowControl Control, FakeWindowSystem.Window Window) Setup()
    {
        var fake = new FakeWindowSystem();
        var window = fake.Add(7, new PixelRect(100, 100, 800, 600));
        window.App = "Task Manager";
        window.Elevated = true;
        fake.Add(8, new PixelRect(300, 300, 800, 600));
        fake.Front = 7;
        return (fake, new WindowControl(fake), window);
    }

    private const string Message = "Task Manager runs as administrator, so Unnamed Manager can't change it.";

    [Fact]
    public void WindowActionsOnItFailAndSayWhy()
    {
        var (_, control, window) = Setup();
        Assert.Equal(Message, control.Run(new WindowCommand.Maximize(false)));
        Assert.Equal(Message, control.Run(new WindowCommand.Minimize()));
        Assert.Equal(Message, control.Run(new WindowCommand.Place(new FractionFrame(0, 0, 0.5, 1))));
        Assert.Equal(new PixelRect(100, 100, 800, 600), window.Frame);
        Assert.False(window.IsMaximized || window.IsMinimized);
    }

    [Fact]
    public void SendShortcutOnItFailsAndSaysWhy()
    {
        var (fake, _, window) = Setup();
        Assert.Equal(Message, Elevation.BlocksSend(fake.Foreground()));
        window.Elevated = false;
        Assert.Null(Elevation.BlocksSend(fake.Foreground()));
        Assert.Null(Elevation.BlocksSend(null));
    }

    [Fact]
    public void TheTrayNamesTheApp() =>
        Assert.Equal("Windows keeps Unnamed Manager from moving Task Manager or typing into it.", Elevation.TrayWhy("Task Manager"));
}
