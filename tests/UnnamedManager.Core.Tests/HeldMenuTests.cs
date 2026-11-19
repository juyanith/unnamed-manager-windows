using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using Xunit;
using static UnnamedManager.Core.Keyboard.SequenceEffect;

namespace UnnamedManager.Core.Tests;

/// Held menus (S-073, spec v18), ported from the Mac's HeldMenuTests so both apps
/// move the highlight and choose the same way.
public class HeldMenuTests
{
    private const Modifiers Cag = Modifiers.Cag;

    private const string Entries = """
        { "keys": "CAG+A", "title": "Apps", "hold": true },
        { "keys": "CAG+A T", "title": "Terminal", "action": "edit" },
        { "keys": "CAG+A B", "title": "Browser", "action": "edit" },
        { "keys": "CAG+A W", "title": "Windows" },
        { "keys": "CAG+A W F", "title": "Fill", "action": "window-fill" },
        { "keys": "CAG+A W M", "title": "Minimize", "action": "window-minimize" },
        { "keys": "CAG+A E", "title": "Search", "action": "search" },
        { "keys": "Hyper+U", "title": "Up Menu", "hold": true },
        { "keys": "Hyper+U Up", "title": "On Up", "action": "edit" },
        { "keys": "Hyper+U X", "title": "Other", "action": "edit" },
        { "keys": "CAG+D", "title": "Window" },
        { "keys": "CAG+D D", "title": "Center", "action": "window-place-center" },
        { "keys": "CAG+D S", "title": "Left Half", "action": "window-place-left-half" }
        """;

    /// Search runs only in a browser, so Notepad in front makes it unavailable.
    private static LoadedConfig Config(RuntimeSettings? runtime = null) => ConfigLoader.Load($$"""
        { "version": 1, "entries": [ {{Entries}} ],
          "actions": { "edit": { "type": "application", "exe": "Code.exe" },
            "search": { "type": "contextual", "variants": { "browser": { "type": "send", "keys": "Ctrl+L" } } } },
          "contexts": [ { "id": "browser", "apps": ["chrome.exe"] } ] }
        """).Loaded!;

    internal static SequenceEngine Engine()
    {
        var engine = new SequenceEngine(Config());
        engine.SetFrontApp(new FrontApp(@"C:\Windows\notepad.exe", null, "Notepad"));
        return engine;
    }

    private static KeyEvent Down(KeyName key, Modifiers modifiers = Cag, bool repeat = false) => KeyEvent.Down(key, modifiers, repeat);

    /// The menu's title and the highlight a held effect shows.
    private static (string, int)? Held(SequenceEffect? effect) => effect is ShowHeld h ? (h.Menu.Title, h.Highlight) : null;

    private static string? Ran(SequenceEffect? effect) => (effect as Run)?.Binding.Title;

    [Fact]
    public void OpeningAHeldMenuHighlightsItsFirstItemAfterCancel()
    {
        var e = Engine();
        var output = e.Handle(Down(KeyName.A));
        Assert.True(output.Consume);
        Assert.Equal(("Apps", 1), Held(output.Effect));
        Assert.True(e.IsHeld);
    }

    [Fact]
    public void AQuickPressAndReleaseRunsTheFirstItem()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        Assert.Equal("Terminal", Ran(e.ModifiersChanged(Modifiers.None)));
        Assert.False(e.IsActive || e.IsHeld);
    }

    [Fact]
    public void TheRepeatKeyMovesOnAndWrapsThroughCancel()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        foreach (var expected in new[] { 2, 3, 4, 0, 1 }) Assert.Equal(("Apps", expected), Held(e.Handle(Down(KeyName.A)).Effect));
        e.Handle(Down(KeyName.A));
        Assert.Equal("Browser", Ran(e.ModifiersChanged(Modifiers.None)));
    }

    [Fact]
    public void ShiftWithTheRepeatKeyMovesBack()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        Assert.Equal(("Apps", 0), Held(e.Handle(Down(KeyName.A, Cag | Modifiers.Shift)).Effect));
        Assert.Equal(("Apps", 4), Held(e.Handle(Down(KeyName.A, Cag | Modifiers.Shift)).Effect));
    }

    [Fact]
    public void WithShiftInTheLeaderTheRepeatKeyStillMovesOn()
    {
        var e = Engine();
        e.Handle(Down(KeyName.U, Modifiers.Hyper));
        Assert.Equal(("Up Menu", 2), Held(e.Handle(Down(KeyName.U, Modifiers.Hyper)).Effect));
    }

    [Fact]
    public void DownAndUpMoveTheHighlightUnlessAnItemHasThatKey()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        Assert.Equal(("Apps", 2), Held(e.Handle(Down(KeyName.Down)).Effect));
        Assert.Equal(("Apps", 1), Held(e.Handle(Down(KeyName.Up)).Effect));
        Assert.Equal(("Apps", 0), Held(e.Handle(Down(KeyName.Up)).Effect));

        var u = Engine();
        u.Handle(Down(KeyName.U, Modifiers.Hyper));
        Assert.Equal(("Up Menu", 1), Held(u.Handle(Down(KeyName.Up, Modifiers.Hyper)).Effect));
        Assert.Equal(("Up Menu", 2), Held(u.Handle(Down(KeyName.Down, Modifiers.Hyper)).Effect));
    }

    [Fact]
    public void AKeyRepeatMovesTheHighlightOnce()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        e.Handle(Down(KeyName.A));
        Assert.Null(e.Handle(Down(KeyName.A, repeat: true)).Effect);
        Assert.Null(e.Handle(Down(KeyName.A, repeat: true)).Effect);
        Assert.Equal(2, e.Highlight);
    }

    [Fact]
    public void ABindingsKeyMovesTheHighlightAndRunsOnlyOnLettingGo()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        Assert.Equal(("Apps", 2), Held(e.Handle(Down(KeyName.B)).Effect));
        Assert.True(e.IsActive);
        Assert.Equal("Browser", Ran(e.ModifiersChanged(Modifiers.None)));
    }

    [Fact]
    public void AMenusKeyOpensItHeldWithItsKeyAsTheRepeatKey()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        Assert.Equal(("Windows", 1), Held(e.Handle(Down(KeyName.W)).Effect));
        Assert.Equal(("Windows", 2), Held(e.Handle(Down(KeyName.W)).Effect));
        Assert.Equal("Minimize", Ran(e.ModifiersChanged(Modifiers.None)));
    }

    [Theory]
    [InlineData(Modifiers.Ctrl, Modifiers.Alt, Modifiers.Win)]
    [InlineData(Modifiers.Ctrl, Modifiers.Win, Modifiers.Alt)]
    [InlineData(Modifiers.Alt, Modifiers.Ctrl, Modifiers.Win)]
    [InlineData(Modifiers.Alt, Modifiers.Win, Modifiers.Ctrl)]
    [InlineData(Modifiers.Win, Modifiers.Ctrl, Modifiers.Alt)]
    [InlineData(Modifiers.Win, Modifiers.Alt, Modifiers.Ctrl)]
    public void LettingGoChoosesOnceForEveryOrder(Modifiers first, Modifiers second, Modifiers third)
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        e.Handle(Down(KeyName.B));
        var held = Cag;
        var chosen = new List<string>();
        foreach (var modifier in new[] { first, second, third })
        {
            held &= ~modifier;
            if (Ran(e.ModifiersChanged(held)) is { } title) chosen.Add(title);
        }
        Assert.Equal(["Browser"], chosen);
    }

    [Fact]
    public void PressingAnotherModifierDoesntChoose()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        Assert.Null(e.ModifiersChanged(Cag | Modifiers.Shift));
        Assert.True(e.IsHeld);
    }

    [Fact]
    public void LettingGoOnAMenuGoesOnAsAnOrdinaryMenu()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        e.Handle(Down(KeyName.A));
        e.Handle(Down(KeyName.A));
        var menu = Assert.IsType<ShowMenu>(e.ModifiersChanged(Modifiers.None));
        Assert.Equal("Windows", menu.Menu.Title);
        Assert.Equal(["Apps", "Windows"], menu.Path);
        Assert.True(e.IsActive && !e.IsHeld);
        Assert.Equal("Fill", Ran(e.Handle(Down(KeyName.F, Modifiers.None)).Effect));
    }

    [Fact]
    public void LettingGoOnCancelEndsTheSequence()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        e.Handle(Down(KeyName.Up));
        Assert.IsType<Cancelled>(e.ModifiersChanged(Modifiers.None));
        Assert.False(e.IsActive);
    }

    [Fact]
    public void LettingGoOnAnUnavailableBindingStaysInItsMenuNowOrdinary()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        e.Handle(Down(KeyName.E));
        Assert.IsType<Unavailable>(e.ModifiersChanged(Modifiers.None));
        Assert.True(e.IsActive && !e.IsHeld);
        Assert.Equal("Terminal", Ran(e.Handle(Down(KeyName.T, Modifiers.None)).Effect));
    }

    [Fact]
    public void CancelEndsAtOnceAndBackGoesUpWithTheHighlightOnTheMenuYouCameFrom()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        e.Handle(Down(KeyName.W));
        Assert.Equal(("Apps", 3), Held(e.Handle(Down(KeyName.Backspace)).Effect));
        Assert.IsType<Cancelled>(e.Handle(Down(KeyName.Escape)).Effect);
        Assert.False(e.IsActive);
    }

    [Fact]
    public void BackFromAHeldLeadersMenuOpensHomeWhichIsntHeld()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        Assert.IsType<ShowHome>(e.Handle(Down(KeyName.Backspace)).Effect);
        Assert.False(e.IsHeld);
        Assert.Null(e.ModifiersChanged(Modifiers.None));
        Assert.True(e.IsActive);
    }

    [Fact]
    public void AnUnboundKeyKeepsTheHighlight()
    {
        var e = Engine();
        e.Handle(Down(KeyName.A));
        e.Handle(Down(KeyName.B));
        Assert.IsType<Unbound>(e.Handle(Down(KeyName.Z)).Effect);
        Assert.Equal(2, e.Highlight);
    }

    [Fact]
    public void ALeaderWithoutHoldWorksAsBefore()
    {
        var e = Engine();
        Assert.IsType<ShowMenu>(e.Handle(Down(KeyName.D)).Effect);
        Assert.Null(e.ModifiersChanged(Modifiers.None));
        Assert.Equal("Left Half", Ran(e.Handle(Down(KeyName.S, Modifiers.None)).Effect));
    }

    [Fact]
    public void NoTimeoutWhileHeldAndItStartsOnceAChosenMenuGoesOn()
    {
        var e = Engine();
        var schedule = new GuideSchedule(new RuntimeSettings { TimeoutMs = 1000 }, new UISettings());
        schedule.Handle(e.Handle(Down(KeyName.A)).Effect!, 0);
        Assert.True(schedule.IsVisible);
        Assert.Null(schedule.NextDeadline);
        e.Handle(Down(KeyName.A));
        schedule.Handle(e.Handle(Down(KeyName.A)).Effect!, 10);
        Assert.Null(schedule.NextDeadline);
        schedule.Handle(e.ModifiersChanged(Modifiers.None)!, 20);
        Assert.Equal(1020, schedule.NextDeadline);
    }

    [Fact]
    public void LettingGoBeforeTheGuidesDelayChoosesWithNoGuide()
    {
        var e = Engine();
        var schedule = new GuideSchedule(new RuntimeSettings { HudDelayMs = 400 }, new UISettings());
        Assert.Empty(schedule.Handle(e.Handle(Down(KeyName.A)).Effect!, 0));
        Assert.Empty(schedule.Handle(e.Handle(Down(KeyName.B)).Effect!, 50));
        Assert.Empty(schedule.Handle(e.ModifiersChanged(Modifiers.None)!, 80));
        Assert.False(schedule.IsVisible);
        Assert.Null(schedule.NextDeadline);
    }

    [Fact]
    public void AltTabCanBeAHeldLeaderAndTheTrackerChoosesWhenAltComesUp()
    {
        var config = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Alt+Tab", "title": "Switch", "hold": true, "list": { "type": "windows.recent" } } ] }
            """);
        Assert.True(config.IsValid, string.Join(" | ", config.Errors));
        var tracker = new KeyboardTracker(config.Loaded!);
        const int LAlt = 0xA4, Tab = 0x09;
        tracker.Process(new RawKey(LAlt, 0x38, false, false));
        var open = tracker.Process(new RawKey(Tab, 0x0F, false, false));
        Assert.True(open.Consume);
        Assert.True(open.SendMask);
        Assert.IsType<FillList>(open.Effect);
        Assert.Equal(Modifiers.Alt, tracker.HoldModifiers);
        Assert.IsType<ShowHeld>(Assert.Single(tracker.Fill([new ListItem(7, "Notes")], null)));
        Assert.Equal(7, Assert.IsType<Run>(tracker.Process(new RawKey(LAlt, 0x38, false, true)).Effect).Binding.Item!.Id);
    }

    [Fact]
    public void AMissedKeyUpIsNoticedAndChooses()
    {
        var config = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Alt+Tab", "title": "Switch", "hold": true, "list": { "type": "windows.recent" } } ] }
            """);
        var tracker = new KeyboardTracker(config.Loaded!);
        tracker.Process(new RawKey(0xA4, 0x38, false, false));
        tracker.Process(new RawKey(0x09, 0x0F, false, false));
        tracker.Fill([new ListItem(7, "Notes")], null);
        Assert.Null(tracker.Resync(_ => true));
        Assert.IsType<Run>(tracker.Resync(_ => false));
        Assert.Equal(Modifiers.None, tracker.HoldModifiers);
    }
}

/// The rules for `hold` (spec v18).
public class HeldMenuRuleTests
{
    private static IReadOnlyList<string> Rules(string entries)
    {
        var result = ConfigLoader.Load($$"""
            { "version": 1, "entries": [ {{entries}} ], "actions": { "edit": { "type": "noop" } } }
            """);
        return [.. result.Errors.Select(e => $"{e.Rule} {e.Subject}")];
    }

    [Fact]
    public void HoldReadsAndIsWrittenOnlyWhenOn()
    {
        var loaded = ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "CAG+A", "title": "Apps", "hold": true },
              { "keys": "CAG+A T", "title": "Terminal", "action": "edit" },
              { "keys": "CAG+D", "title": "Window", "hold": false },
              { "keys": "CAG+D T", "title": "Terminal", "action": "edit" } ],
              "actions": { "edit": { "type": "noop" } } }
            """).Loaded!;
        Assert.Equal([true, false], loaded.Leaders.Select(l => l.Hold));
        var written = ConfigWriter.Write(loaded.Config);
        Assert.Contains("""{ "keys": "CAG+A", "title": "Apps", "hold": true }""", written);
        Assert.Contains("""{ "keys": "CAG+D", "title": "Window" }""", written);
    }

    [Fact]
    public void AKeyUnderAHeldLeaderCantBeItsMenusRepeatKey()
    {
        Assert.Equal(["step-is-repeat-key CAG+D D"], Rules("""
            { "keys": "CAG+D", "title": "Window", "hold": true },
            { "keys": "CAG+D D", "title": "Center", "action": "edit" }
            """));
        Assert.Equal(["step-is-repeat-key CAG+A B B"], Rules("""
            { "keys": "CAG+A", "title": "Apps", "hold": true },
            { "keys": "CAG+A B", "title": "More" },
            { "keys": "CAG+A B A", "title": "Fine", "action": "edit" },
            { "keys": "CAG+A B B", "title": "Not", "action": "edit" }
            """));
        Assert.Empty(Rules("""
            { "keys": "CAG+D", "title": "Window" },
            { "keys": "CAG+D D", "title": "Center", "action": "edit" }
            """));
    }

    [Fact]
    public void HoldOnlyOnALeaderWithModifiers()
    {
        Assert.Equal(["hold-not-leader CAG+A W"], Rules("""
            { "keys": "CAG+A", "title": "Apps" },
            { "keys": "CAG+A W", "title": "Windows", "hold": true },
            { "keys": "CAG+A W T", "title": "Terminal", "action": "edit" }
            """));
        Assert.Equal(["hold-not-leader CAG+T"], Rules("""{ "keys": "CAG+T", "title": "Terminal", "action": "edit", "hold": true }"""));
        Assert.Equal(["hold-no-modifiers F13"], Rules("""
            { "keys": "F13", "title": "Apps", "hold": true },
            { "keys": "F13 T", "title": "Terminal", "action": "edit" }
            """));
    }
}
