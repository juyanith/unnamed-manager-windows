using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using UnnamedManager.Core.Windows;
using Xunit;
using static UnnamedManager.Core.Keyboard.SequenceEffect;

namespace UnnamedManager.Core.Tests;

/// Lists and Recent Windows (S-074 to S-076, spec v18, v19), ported from the Mac's
/// ListTests.
public class ListTests
{
    private const Modifiers Cag = Modifiers.Cag;

    private static LoadedConfig Load(string entries)
    {
        var result = ConfigLoader.Load($$"""{ "version": 1, "entries": [ {{entries}} ] }""");
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        return result.Loaded!;
    }

    private const string Entries = """
        { "keys": "CAG+H", "title": "Recent Windows", "hold": true, "list": { "type": "windows.recent" } },
        { "keys": "Hyper+W", "title": "Windows" },
        { "keys": "Hyper+W R", "title": "Recent", "list": { "type": "windows.recent", "count": 5, "spaces": "current" } },
        { "keys": "Hyper+W F", "title": "Fill", "action": "window-fill" }
        """;

    private static SequenceEngine Engine() => new(Load(Entries));

    private static KeyEvent Down(KeyName key, Modifiers modifiers = Cag) => KeyEvent.Down(key, modifiers);

    private static readonly ListItem[] Items = [new(10, "Notes"), new(11, "Edge"), new(12, "Terminal")];
    private static readonly ListItem Front = new(1, "Code");

    /// The item titles, joined with commas, and the highlight a held effect shows.
    private static (string Titles, int Highlight)? Held(SequenceEffect? effect) =>
        effect is ShowHeld h ? (string.Join(",", h.Menu.Children.Select(c => c.Title)), h.Highlight) : null;

    private static long? RanItem(SequenceEffect? effect) => (effect as Run)?.Binding.Item?.Id;

    private static IReadOnlyList<ListItem> Many(int count) => [.. Enumerable.Range(0, count).Select(i => new ListItem(i, $"W{i}"))];

    [Fact]
    public void AListReadsAndWritesWithItsDefaultsLeftOut()
    {
        var config = Engine().Config.Config;
        Assert.Equal(new ListSpec(), config.Entries[0].List);
        Assert.Equal(new ListSpec(Count: 5, Spaces: "current"), config.Entries[2].List);
        var text = ConfigWriter.Write(config);
        Assert.Contains(""""list": { "type": "windows.recent" }"""", text);
        Assert.Contains(""""list": { "type": "windows.recent", "count": 5, "spaces": "current" }"""", text);
        Assert.Equal(text, ConfigWriter.Write(ConfigLoader.Load(text).Loaded!.Config));
    }

    [Theory]
    [InlineData("""{ "keys": "CAG+H", "title": "R", "list": { "type": "windows.recent" } }, { "keys": "CAG+H F", "title": "F", "action": "window-fill" }""", "list-has-children CAG+H F")]
    [InlineData("""{ "keys": "CAG+H", "title": "R", "action": "window-fill", "list": { "type": "windows.recent" } }""", "conflicting-fields CAG+H")]
    [InlineData("""{ "keys": "CAG+H", "title": "R", "list": { "type": "apps.running" } }""", "unknown-list-type CAG+H")]
    [InlineData("""{ "keys": "CAG+H", "title": "R", "list": { "type": "windows.recent", "count": 0 } }""", "bad-list-value CAG+H")]
    [InlineData("""{ "keys": "CAG+H", "title": "R", "list": { "type": "windows.recent", "scope": "space" } }""", "bad-list-value CAG+H")]
    [InlineData("""{ "keys": "CAG+1", "title": "R", "hold": true, "list": { "type": "windows.recent" } }""", "step-is-repeat-key CAG+1")]
    [InlineData("""{ "keys": "CAG+Tab", "title": "S", "list": { "type": "windows.recent", "choose": "close" } }""", "bad-list-value CAG+Tab")]
    [InlineData("""{ "keys": "CAG+Tab", "title": "S", "list": { "type": "windows.recent", "choose": "swap", "minimized": true } }""", "conflicting-fields CAG+Tab")]
    [InlineData("""{ "keys": "CAG+Tab", "title": "S", "list": { "type": "windows.recent", "choose": "swap", "spaces": "all" } }""", "conflicting-fields CAG+Tab")]
    [InlineData("""{ "keys": "CAG+Tab", "title": "S", "list": { "type": "windows.recent", "hidden": false } }""", "other-platform-field CAG+Tab")]
    public void AListsRules(string entries, string rule)
    {
        var result = ConfigLoader.Load($$"""{ "version": 1, "entries": [ {{entries}} ] }""");
        Assert.Equal([rule], result.Errors.Select(e => $"{e.Rule} {e.Subject}"));
    }

    [Fact]
    public void AListWithoutATypeIsAMissingFieldAndAListIsNeverAnEmptyMenu()
    {
        var missing = ConfigLoader.Load("""{ "version": 1, "entries": [ { "keys": "CAG+H", "title": "R", "list": { "count": 5 } } ] }""");
        Assert.Equal([Rule.MissingField], missing.Errors.Select(e => e.Rule));
        // A list that isn't held can have a digit for its key.
        Load("""{ "keys": "CAG+1", "title": "R", "list": { "type": "windows.recent" } }""");
    }

    [Fact]
    public void ChooseReadsAndWritesWithFocusLeftOut()
    {
        var config = Load("""
            { "keys": "CAG+Tab", "title": "Swap", "list": { "type": "windows.recent", "choose": "swap", "scope": "display" } },
            { "keys": "CAG+H", "title": "Recent", "list": { "type": "windows.recent", "choose": "focus" } }
            """).Config;
        Assert.True(config.Entries[0].List!.Swaps);
        Assert.Contains(""""list": { "type": "windows.recent", "scope": "display", "choose": "swap" }"""", ConfigWriter.Write(config));
        Assert.Equal(new ListSpec(), config.Entries[1].List);
        var swap = new ListSpec(Choose: "swap");
        Assert.True(!swap.IncludesMinimized && swap.CurrentDesktopOnly);
        Assert.True(new ListSpec().IncludesMinimized && !new ListSpec().CurrentDesktopOnly);
    }

    [Fact]
    public void OpeningAHeldListAsksForItsItemsThenShowsThemWithTheFirstHighlighted()
    {
        var e = Engine();
        Assert.IsType<FillList>(e.Handle(Down(KeyName.H)).Effect);
        Assert.True(e.IsFilling && !e.IsHeld);
        var effects = e.Fill(Items, Front);
        var shown = Assert.IsType<ShowHeld>(Assert.Single(effects));
        Assert.Equal(("Notes,Edge,Terminal", 1), Held(shown)!.Value);
        Assert.Equal([KeyName.D1, KeyName.D2, KeyName.D3], shown.Menu.Children.Select(c => c.Path.LastStep!.Value.Key));
        Assert.Equal(Front, shown.Menu.Front);
    }

    [Fact]
    public void KeysPressedBeforeTheItemsComeAreReplayed()
    {
        var e = Engine();
        e.Handle(Down(KeyName.H));
        Assert.True(e.Handle(Down(KeyName.D2)).Consume);
        Assert.Null(e.ModifiersChanged(Modifiers.None));
        var effects = e.Fill(Items, Front);
        Assert.Equal(3, effects.Count);
        Assert.Equal(2, Held(effects[1])?.Highlight);
        Assert.Equal(11, RanItem(effects[2]));
        Assert.False(e.IsActive);
    }

    [Fact]
    public void TheRepeatKeyAndLettingGoChooseAnItem()
    {
        var e = Engine();
        e.Handle(Down(KeyName.H));
        e.Fill(Items, Front);
        Assert.Equal(2, Held(e.Handle(Down(KeyName.H)).Effect)?.Highlight);
        Assert.Equal(11, RanItem(e.ModifiersChanged(Modifiers.None)));
    }

    [Fact]
    public void AHeldListWithNoItemsHasOnlyCancel()
    {
        var e = Engine();
        e.Handle(Down(KeyName.H));
        var shown = Held(e.Fill([], Front).First())!.Value;
        Assert.Equal("", shown.Titles);
        Assert.Equal(0, shown.Highlight);
        Assert.IsType<Cancelled>(e.ModifiersChanged(Modifiers.None));
    }

    [Fact]
    public void AListInAMenuThatIsntHeldRunsAnItemByItsKey()
    {
        var e = Engine();
        e.Handle(Down(KeyName.W, Modifiers.Hyper));
        Assert.IsType<FillList>(e.Handle(Down(KeyName.R, Modifiers.None)).Effect);
        var menu = Assert.IsType<ShowMenu>(e.Fill(Items, null).First());
        Assert.Equal(3, menu.Menu.Children.Count);
        Assert.Equal(["Windows", "Recent"], menu.Path);
        Assert.Equal(12, RanItem(e.Handle(Down(KeyName.D3, Modifiers.None)).Effect));
    }

    [Fact]
    public void WhenAnItemsWindowClosesTheKeysAfterItMoveUpAndTheHighlightMovesOn()
    {
        var e = Engine();
        e.Handle(Down(KeyName.H));
        e.Fill(Items, Front);
        e.Handle(Down(KeyName.H));
        var next = e.RemoveItem(11);
        Assert.Equal(("Notes,Terminal", 2), Held(next)!.Value);
        Assert.Equal([KeyName.D1, KeyName.D2], ((ShowHeld)next!).Menu.Children.Select(c => c.Path.LastStep!.Value.Key));
        Assert.Equal(("Notes", 1), Held(e.RemoveItem(12))!.Value);
        Assert.Equal(0, Held(e.RemoveItem(10))!.Value.Highlight);
        Assert.Null(e.RemoveItem(99));
    }

    [Fact]
    public void AnItemBeforeTheHighlightMovesTheHighlightUpWithItsItem()
    {
        var e = Engine();
        e.Handle(Down(KeyName.H));
        e.Fill(Items, Front);
        e.Handle(Down(KeyName.D3));
        Assert.Equal(("Edge,Terminal", 2), Held(e.RemoveItem(10))!.Value);
    }

    [Fact]
    public void PagesOfNineWithMoreOnZeroAndThePageInThePath()
    {
        var e = Engine();
        e.Handle(Down(KeyName.H));
        var first = Assert.IsType<ShowHeld>(e.Fill(Many(20), null).First());
        Assert.True(first.Menu.Children.Count == 9 && first.Menu.HasMore && first.Highlight == 1);
        Assert.Equal(["Recent Windows", "Page 1 of 3"], first.Path);
        var second = Assert.IsType<ShowHeld>(e.Handle(Down(KeyName.D0)).Effect);
        Assert.Equal("W9", second.Menu.Children[0].Title);
        Assert.Equal(KeyName.D1, second.Menu.Children[0].Path.LastStep!.Value.Key);
        Assert.Equal(["Recent Windows", "Page 2 of 3"], second.Path);
        var last = Assert.IsType<ShowHeld>(e.Handle(Down(KeyName.D0)).Effect);
        Assert.Equal(["W18", "W19"], last.Menu.Children.Select(c => c.Title));
        Assert.False(last.Menu.HasMore);
        Assert.IsType<Unbound>(e.Handle(Down(KeyName.D0)).Effect);
        var back = Assert.IsType<ShowHeld>(e.Handle(Down(KeyName.Backspace)).Effect);
        Assert.Equal(1, back.Menu.Page);
        Assert.Equal(9, RanItem(e.ModifiersChanged(Modifiers.None)));
    }

    [Fact]
    public void TheRepeatKeyGoesAcrossPagesAndAfterTheLastItemToCancelOnTheFirstPage()
    {
        var e = Engine();
        e.Handle(Down(KeyName.H));
        e.Fill(Many(11), null);
        for (var i = 1; i < 9; i++) e.Handle(Down(KeyName.H));
        Assert.Equal(("W9,W10", 1), Held(e.Handle(Down(KeyName.H)).Effect)!.Value);
        Assert.Equal(("W9,W10", 2), Held(e.Handle(Down(KeyName.Down)).Effect)!.Value);
        var cancel = Held(e.Handle(Down(KeyName.H)).Effect)!.Value;
        Assert.Equal(0, cancel.Highlight);
        Assert.Equal(9, cancel.Titles.Split(",").Length);
        Assert.Equal(("W9,W10", 2), Held(e.Handle(Down(KeyName.H, Cag | Modifiers.Shift)).Effect)!.Value);
    }

    [Fact]
    public void RemovingAnItemMovesTheLaterOnesUpAcrossPages()
    {
        var e = Engine();
        e.Handle(Down(KeyName.H));
        e.Fill(Many(11), null);
        e.Handle(Down(KeyName.D0));
        Assert.Equal(("W1,W2,W3,W4,W5,W6,W7,W8,W9", 9), Held(e.RemoveItem(0))!.Value);
    }

    [Fact]
    public void AListThatIsntHeldPagesWithZeroToo()
    {
        var e = Engine();
        e.Handle(Down(KeyName.W, Modifiers.Hyper));
        e.Handle(Down(KeyName.R, Modifiers.None));
        e.Fill(Many(12), null);
        var menu = Assert.IsType<ShowMenu>(e.Handle(Down(KeyName.D0, Modifiers.None)).Effect);
        Assert.Equal(["W9", "W10", "W11"], menu.Menu.Children.Select(c => c.Title));
        Assert.Equal(10, RanItem(e.Handle(Down(KeyName.D2, Modifiers.None)).Effect));
    }
}

/// Which windows Recent Windows lists, and what choosing one does (S-074, S-076).
public class RecentWindowsTests
{
    private static (FakeWindowSystem Fake, WindowControl Control) Setup(WindowSettings? settings = null)
    {
        var fake = new FakeWindowSystem { DesktopCount = 2 };
        var control = new WindowControl(fake, settings is null ? null : () => settings);
        return (fake, control);
    }

    [Fact]
    public void TheHistoryFirstThenTheOthersFrontFirstWithoutTheFocusedWindow()
    {
        var (fake, control) = Setup();
        foreach (var h in new nint[] { 1, 2, 3, 4, 5 }) fake.Add(h, new PixelRect(100 * (int)h, 100, 400, 300)).Title = $"W{h}";
        // Used: 2, then 4, then 1 now. 5 is in front of 3 from being added later.
        fake.Click(2);
        fake.Click(4);
        fake.Click(1);
        var (items, front) = control.RecentWindows(new ListSpec());
        Assert.Equal("W1", front!.Title);
        Assert.Equal(["W4", "W2", "W5", "W3"], items.Select(i => i.Title));
        Assert.Equal(["W4", "W2"], control.RecentWindows(new ListSpec(Count: 2)).Items.Select(i => i.Title));
    }

    [Fact]
    public void ItsFieldsLeaveOutMinimizedWindowsOtherDesktopsAndOtherDisplaysAndMarkThem()
    {
        var (fake, control) = Setup();
        fake.Monitors.Add(new Screen(new(1920, 0, 1920, 1080), new(1920, 0, 1920, 1032)));
        fake.Add(1, new PixelRect(100, 100, 400, 300)).Title = "Here";
        fake.Add(2, new PixelRect(2000, 100, 400, 300)).Title = "Other display";
        fake.Add(3, new PixelRect(200, 100, 400, 300)).IsMinimized = true;
        fake.Windows[3].Title = "Minimized";
        var away = fake.Add(4, new PixelRect(300, 100, 400, 300));
        away.Title = "Away";
        away.Desktop = 1;
        fake.Add(5, new PixelRect(100, 100, 400, 300)).Title = "Focused";
        var all = control.RecentWindows(new ListSpec()).Items;
        Assert.Equal(["Away", "Minimized", "Other display", "Here"], all.Select(i => i.Title));
        Assert.Equal([ItemMarks.OtherDesktop, ItemMarks.Minimized, ItemMarks.OtherDisplay, ItemMarks.None], all.Select(i => i.Marks));
        Assert.Equal(["Other display", "Here"], control.RecentWindows(new ListSpec(Minimized: false, Spaces: "current")).Items.Select(i => i.Title));
        Assert.Equal(["Here"], control.RecentWindows(new ListSpec(Minimized: false, Spaces: "current", Scope: "display")).Items.Select(i => i.Title));
        Assert.Equal(["Other display", "Here"], control.RecentWindows(new ListSpec(Choose: "swap")).Items.Select(i => i.Title));
    }

    [Fact]
    public void AWindowWithNoTitleShowsItsAppsNameAndLeftOutAppsArentItems()
    {
        var (fake, control) = Setup(new WindowSettings { Apps = [new("Calculator.exe", new AppRules(Focus: false)), new("Paint.exe", new AppRules(Swap: false))] });
        fake.Add(1, new PixelRect(100, 100, 400, 300)).App = "Calculator";
        fake.Add(2, new PixelRect(100, 100, 400, 300)).App = "Paint";
        fake.Add(3, new PixelRect(100, 100, 400, 300)).App = "Code";
        Assert.Equal(["Paint"], control.RecentWindows(new ListSpec()).Items.Select(i => i.Title));
        Assert.Equal(["Calculator"], control.RecentWindows(new ListSpec(Choose: "swap")).Items.Select(i => i.Title));
    }

    [Fact]
    public void ASwapListWithNoFocusedWindowIsEmpty()
    {
        var (fake, control) = Setup();
        fake.Add(1, new PixelRect(100, 100, 400, 300));
        fake.FocusNone();
        Assert.Empty(control.RecentWindows(new ListSpec(Choose: "swap")).Items);
        Assert.Single(control.RecentWindows(new ListSpec()).Items);
    }

    [Fact]
    public void ChoosingFocusesTheWindowFromMinimizedOrAnotherDesktopAndPutsItFirst()
    {
        var (fake, control) = Setup();
        fake.Add(1, new PixelRect(100, 100, 400, 300)).Desktop = 1;
        fake.Windows[1].IsMinimized = true;
        fake.Add(2, new PixelRect(500, 100, 400, 300));
        Assert.Null(control.Run(new WindowCommand.Choose(new WindowRef(1), Swaps: false)));
        Assert.Equal(1, fake.Front);
        Assert.Equal(1, fake.CurrentDesktop);
        Assert.False(fake.Windows[1].IsMinimized);
        Assert.Equal(new WindowRef(1), control.History[0]);
        fake.Remove(2);
        Assert.True(control.Run(new WindowCommand.Choose(new WindowRef(2), Swaps: false))!.Alerts);
    }

    [Fact]
    public void ChoosingToSwapTradesFramesKeepsFocusAndTheHistory()
    {
        var (fake, control) = Setup(new WindowSettings { Gap = 0, Apps = [new("Paint.exe", new AppRules(Swap: false))] });
        PixelRect a = new(0, 0, 960, 1032), b = new(960, 0, 960, 1032);
        fake.Add(2, b);
        fake.Add(1, a);
        Assert.Null(control.Run(new WindowCommand.Choose(new WindowRef(2), Swaps: true)));
        Assert.Equal(b, fake.Windows[1].Frame);
        Assert.Equal(a, fake.Windows[2].Frame);
        Assert.Equal(1, fake.Front);
        Assert.Equal([new WindowRef(1)], control.History);
        fake.Windows[1].App = "Paint";
        Assert.Equal("Paint is left out of Swap Window.", control.Run(new WindowCommand.Choose(new WindowRef(2), Swaps: true))!.Message);
    }
}
