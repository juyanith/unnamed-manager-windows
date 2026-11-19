using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// Switch Desktop and Send to Desktop (S-072, S-077, spec v17, v20).
public class SpaceTests
{
    private static readonly ConfigReader Reader = new(ActionRegistry.Standard);

    [Fact]
    public void TheyReadWriteAndHaveBuiltInNames()
    {
        Assert.Equal(new SpaceSwitchAction("left"), Reader.ReadAction("""{ "type": "space.switch", "target": "left" }""", "a"));
        var send = Reader.ReadAction("""{ "type": "space.send", "target": 2, "follow": true }""", "a");
        Assert.Equal(new SpaceSendAction(SpaceTarget.Of(2), true), send);
        Assert.Equal("""{ "type": "space.send", "target": 2, "follow": true }""", ConfigWriter.Action(send, []));
        Assert.Equal("""{ "type": "space.send", "target": "right" }""", ConfigWriter.Action(new SpaceSendAction(SpaceTarget.Of("right")), []));
        Assert.Equal(new SpaceSwitchAction("right"), BuiltInActions.Action("space-switch-right"));
        Assert.Equal(new SpaceSendAction(SpaceTarget.Of(3)), BuiltInActions.Action("space-send-3"));
        Assert.Null(BuiltInActions.Action("space-switch-2"));
        Assert.Null(BuiltInActions.Action("space-send-0"));
        Assert.Equal("Send to Desktop: Desktop 3", BuiltInActions.Label(BuiltInActions.Action("space-send-3")!));
        Assert.Equal("Switch Desktop: Left", BuiltInActions.Label(BuiltInActions.Action("space-switch-left")!));
    }

    [Theory]
    [InlineData("""{ "type": "space.switch" }""", Rule.MissingField)]
    [InlineData("""{ "type": "space.switch", "target": "up" }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.switch", "target": 2 }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.send", "target": 0 }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.send", "target": "2" }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.send", "target": 2, "follow": "yes" }""", Rule.WrongType)]
    public void BadValuesNameTheirRule(string json, string rule) =>
        Assert.Equal(rule, Assert.Throws<ConfigProblem>(() => Reader.ReadAction(json, "a")).Rule);

    [Fact]
    public void TheirFormsBuildWhatTheFileHas()
    {
        var send = new SpaceSendAction(SpaceTarget.Of(4), true);
        Assert.Equal(send, ActionForms.Build("space.send", ActionForms.Values(send), "a"));
        Assert.Equal(new SpaceSendAction(SpaceTarget.Of("right")), ActionForms.Build("space.send", ActionForms.Defaults("space.send"), "a"));
        Assert.Equal(new SpaceSwitchAction("left"), ActionForms.Build("space.switch", ActionForms.Values(new SpaceSwitchAction("left")), "a"));
        Assert.Equal(new SpaceSwitchAction("right"), ActionForms.Build("space.switch", ActionForms.Defaults("space.switch"), "a"));
    }

    private static (FakeWindowSystem, WindowControl) Desktops(int count, int current = 0)
    {
        var fake = new FakeWindowSystem { DesktopCount = count, CurrentDesktop = current };
        return (fake, new WindowControl(fake));
    }

    [Fact]
    public void SwitchGoesLeftOrRightAndStopsAtTheEndsWithNoFailure()
    {
        var (fake, control) = Desktops(3, current: 1);
        fake.Add(1, new PixelRect(0, 0, 800, 600)).Desktop = 1;
        Assert.Null(control.Run(new WindowCommand.SwitchDesktop("right")));
        Assert.Equal(2, fake.CurrentDesktop);
        Assert.Null(control.Run(new WindowCommand.SwitchDesktop("right")));
        Assert.Equal(1, fake.Switches);
    }

    [Fact]
    public void SwitchWithNoFocusedWindowDoesNothing()
    {
        var (fake, control) = Desktops(3);
        Assert.Null(control.Run(new WindowCommand.SwitchDesktop("right")));
        Assert.Equal(0, fake.Switches);
    }

    [Fact]
    public void SendPutsTheWindowThereAndFocusGoesToTheWindowUsedBeforeHere()
    {
        var (fake, control) = Desktops(3);
        fake.Add(1, new PixelRect(0, 0, 800, 600));
        fake.Add(2, new PixelRect(100, 0, 800, 600));
        fake.Add(3, new PixelRect(200, 0, 800, 600));
        fake.Click(1);
        fake.Click(3);
        Assert.Null(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of("right"), false)));
        Assert.Equal(1, fake.Windows[3].Desktop);
        Assert.Equal(0, fake.CurrentDesktop);
        Assert.Equal(1, fake.Front);
        // The history keeps the sent window, so Focus Window's recent can go back to it.
        Assert.Contains(new WindowRef(3), control.History);
    }

    [Fact]
    public void SendWithFollowGoesThereWithTheWindow()
    {
        var (fake, control) = Desktops(3);
        fake.Add(1, new PixelRect(0, 0, 800, 600));
        Assert.Null(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(3), true)));
        Assert.Equal(2, fake.Windows[1].Desktop);
        Assert.Equal(2, fake.CurrentDesktop);
        Assert.Equal(1, fake.Front);
    }

    [Fact]
    public void SendWithNoOtherWindowLeavesNoneFocused()
    {
        var (fake, control) = Desktops(2);
        fake.Add(1, new PixelRect(0, 0, 800, 600));
        Assert.Null(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(2), false)));
        Assert.Null(fake.Front);
    }

    [Fact]
    public void SendFailsWithTheAlertSoundWhereItCantAndDoesNothingWhereItsAlreadyThere()
    {
        var (fake, control) = Desktops(2);
        Assert.True(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(2), false))!.Alerts);
        var w = fake.Add(1, new PixelRect(0, 0, 800, 600));
        var past = control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(3), false))!;
        Assert.Equal("No desktop 3. There are 2.", past.Message);
        Assert.True(past.Alerts);
        Assert.Null(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of("left"), false)));
        Assert.Null(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(1), false)));
        Assert.Equal(0, w.Desktop);
        w.OnEveryDesktop = true;
        Assert.Equal("Notepad's window is on every desktop.", control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(2), false))!.Message);
        w.OnEveryDesktop = false;
        w.IsFullScreen = true;
        Assert.True(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(2), false))!.Alerts);
        w.IsFullScreen = false;
        fake.DesktopsUnavailable = true;
        Assert.True(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(2), false))!.Alerts);
    }
}
