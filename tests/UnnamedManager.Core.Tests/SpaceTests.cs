using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// Switch Desktop, Send to Desktop and Follow to Desktop (S-072, S-077, S-112, S-113, spec v17, v20, v22, v23).
public class SpaceTests
{
    private static readonly ConfigReader Reader = new(ActionRegistry.Standard);

    [Fact]
    public void TheyReadWriteAndHaveBuiltInNames()
    {
        Assert.Equal(new SpaceSwitchAction(SpaceTarget.Of("left")), Reader.ReadAction("""{ "type": "space.switch", "target": "left" }""", "a"));
        var one = Reader.ReadAction("""{ "type": "space.switch", "target": 1 }""", "a");
        Assert.Equal(new SpaceSwitchAction(SpaceTarget.Of(1)), one);
        Assert.Equal("""{ "type": "space.switch", "target": 1 }""", ConfigWriter.Action(one, []));
        var follow = Reader.ReadAction("""{ "type": "space.follow", "target": 2 }""", "a");
        Assert.Equal(new SpaceSendAction(SpaceTarget.Of(2), Follow: true), follow);
        Assert.Equal("""{ "type": "space.follow", "target": 2 }""", ConfigWriter.Action(follow, []));
        Assert.Equal(new SpaceSendAction(SpaceTarget.Of(2)), Reader.ReadAction("""{ "type": "space.send", "target": 2 }""", "a"));
        Assert.Equal("""{ "type": "space.send", "target": "right" }""", ConfigWriter.Action(new SpaceSendAction(SpaceTarget.Of("right")), []));
        Assert.Equal(new SpaceSwitchAction(SpaceTarget.Of("right")), BuiltInActions.Action("space-switch-right"));
        Assert.Equal(new SpaceSwitchAction(SpaceTarget.Of(2)), BuiltInActions.Action("space-switch-2"));
        Assert.Equal(new SpaceSendAction(SpaceTarget.Of(3)), BuiltInActions.Action("space-send-3"));
        Assert.Null(BuiltInActions.Action("space-switch-0"));
        Assert.Null(BuiltInActions.Action("space-switch-02"));
        Assert.Null(BuiltInActions.Action("space-send-0"));
        Assert.Equal(new SpaceSendAction(SpaceTarget.Of("left"), Follow: true), BuiltInActions.Action("space-follow-left"));
        Assert.Equal(new SpaceSendAction(SpaceTarget.Of(2), Follow: true), BuiltInActions.Action("space-follow-2"));
        Assert.Null(BuiltInActions.Action("space-follow-0"));
        Assert.Equal("Follow to Desktop: Desktop 2", BuiltInActions.Label(BuiltInActions.Action("space-follow-2")!));
        Assert.Equal("Send to Desktop: Desktop 3", BuiltInActions.Label(BuiltInActions.Action("space-send-3")!));
        Assert.Equal("Switch Desktop: Left", BuiltInActions.Label(BuiltInActions.Action("space-switch-left")!));
        Assert.Equal("Switch Desktop: Desktop 1", BuiltInActions.Label(BuiltInActions.Action("space-switch-1")!));
    }

    [Theory]
    [InlineData("""{ "type": "space.switch" }""", Rule.MissingField)]
    [InlineData("""{ "type": "space.switch", "target": "up" }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.switch", "target": 0 }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.switch", "target": "2" }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.switch", "target": 1.5 }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.send", "target": 0 }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.send", "target": "2" }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.send", "target": 2, "follow": true }""", Rule.UnknownField)]
    [InlineData("""{ "type": "space.send", "target": 2, "follow": false }""", Rule.UnknownField)]
    [InlineData("""{ "type": "space.follow" }""", Rule.MissingField)]
    [InlineData("""{ "type": "space.follow", "target": 0 }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.follow", "target": "2" }""", Rule.BadWindowValue)]
    [InlineData("""{ "type": "space.follow", "target": 2, "follow": true }""", Rule.UnknownField)]
    public void BadValuesNameTheirRule(string json, string rule) =>
        Assert.Equal(rule, Assert.Throws<ConfigProblem>(() => Reader.ReadAction(json, "a")).Rule);

    [Fact]
    public void SendWithFollowSaysToUseFollowToDesktop() =>
        Assert.Contains("use Follow to Desktop (“space.follow”)",
            Assert.Throws<ConfigProblem>(() => Reader.ReadAction("""{ "type": "space.send", "target": 2, "follow": true }""", "a")).Message);

    [Fact]
    public void TheirFormsBuildWhatTheFileHas()
    {
        var send = new SpaceSendAction(SpaceTarget.Of(4));
        Assert.Equal(send, ActionForms.Build("space.send", ActionForms.Values(send), "a"));
        var follow = new SpaceSendAction(SpaceTarget.Of(4), Follow: true);
        Assert.Equal(follow, ActionForms.Build("space.follow", ActionForms.Values(follow), "a"));
        Assert.Equal(new SpaceSendAction(SpaceTarget.Of("right"), Follow: true), ActionForms.Build("space.follow", ActionForms.Defaults("space.follow"), "a"));
        Assert.DoesNotContain(ActionForms.Fields("space.send")!, f => f.Name == "follow");
        Assert.Equal(new SpaceSendAction(SpaceTarget.Of("right")), ActionForms.Build("space.send", ActionForms.Defaults("space.send"), "a"));
        Assert.Equal(new SpaceSwitchAction(SpaceTarget.Of("left")), ActionForms.Build("space.switch", ActionForms.Values(new SpaceSwitchAction(SpaceTarget.Of("left"))), "a"));
        Assert.Equal(new SpaceSwitchAction(SpaceTarget.Of(3)), ActionForms.Build("space.switch", ActionForms.Values(new SpaceSwitchAction(SpaceTarget.Of(3))), "a"));
        Assert.Equal(new SpaceSwitchAction(SpaceTarget.Of("right")), ActionForms.Build("space.switch", ActionForms.Defaults("space.switch"), "a"));
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
        Assert.Null(control.Run(new WindowCommand.SwitchDesktop(SpaceTarget.Of("right"))));
        Assert.Equal(2, fake.CurrentDesktop);
        Assert.Null(control.Run(new WindowCommand.SwitchDesktop(SpaceTarget.Of("right"))));
        Assert.Equal(1, fake.Switches);
    }

    [Fact]
    public void SwitchGoesToADesktopByNumberEvenWithNoFocusedWindow()
    {
        var (fake, control) = Desktops(4, current: 3);
        Assert.Null(control.Run(new WindowCommand.SwitchDesktop(SpaceTarget.Of(1))));
        Assert.Equal(0, fake.CurrentDesktop);
        Assert.Null(control.Run(new WindowCommand.SwitchDesktop(SpaceTarget.Of(1))));
        Assert.Equal(1, fake.Switches);
        var past = control.Run(new WindowCommand.SwitchDesktop(SpaceTarget.Of(5)));
        Assert.Equal("No desktop 5. There are 4.", past?.Message);
        Assert.True(past?.Alerts);
        Assert.Equal(1, fake.Switches);
    }

    [Fact]
    public void SwitchWithNoFocusedWindowDoesNothing()
    {
        var (fake, control) = Desktops(3);
        Assert.Null(control.Run(new WindowCommand.SwitchDesktop(SpaceTarget.Of("right"))));
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
    public void FollowGoesThereWithTheWindow()
    {
        var (fake, control) = Desktops(3);
        fake.Add(1, new PixelRect(0, 0, 800, 600));
        Assert.Null(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(3), true)));
        Assert.Equal(2, fake.Windows[1].Desktop);
        Assert.Equal(2, fake.CurrentDesktop);
        Assert.Equal(1, fake.Front);
        // Already there, or at the last desktop: nothing moves, you stay, and no sound.
        Assert.Null(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(3), true)));
        Assert.Null(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of("right"), true)));
        Assert.Equal(1, fake.Switches);
        Assert.True(control.Run(new WindowCommand.SendToDesktop(SpaceTarget.Of(4), true))!.Alerts);
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
