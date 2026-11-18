using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// Arrange Windows (spec v8, v9): the grid, the order, the focused window's cell. Ported
/// from the Mac's ArrangeTests, so both apps lay out the same windows the same way.
public class ArrangeTests
{
    private static readonly PixelRect Wide = new(0, 25, 1440, 875), Tall = new(0, 0, 1080, 1920), Small = new(100, 100, 800, 600);
    private static readonly PixelRect Left = new(0, 25, 720, 875), Right = new(720, 25, 720, 875);
    private static readonly PixelRect TopLeft = new(0, 25, 720, 438), BottomLeft = new(0, 463, 720, 437);
    private static readonly PixelRect TopRight = new(720, 25, 720, 438), BottomRight = new(720, 463, 720, 437);

    /// The frame each window gets, without the gap, by its index; null for a window left alone.
    private static PixelRect?[] Arranged(ArrangeAction action, PixelRect[] windows, int maxColumns = 2, int maxRows = 2, PixelRect? usable = null)
    {
        var area = usable ?? Wide;
        var plan = ArrangeGrid.Plan(action, maxColumns, maxRows, area, windows, 2);
        return [.. Enumerable.Range(0, windows.Length).Select(i => plan.Where(p => p.Window == i).Select(p => (PixelRect?)p.Cell.Rect(area)).FirstOrDefault())];
    }

    private static PixelRect?[] Arranged(params PixelRect[] windows) => Arranged(new ArrangeAction(), windows);

    [Fact]
    public void TwoWindowsKeepTheirOrderWhicheverHasFocus()
    {
        PixelRect a = new(50, 100, 600, 500), b = new(500, 200, 600, 500);
        Assert.Equal([Left, Right], Arranged(a, b));
        Assert.Equal([Right, Left], Arranged(b, a));
    }

    [Fact]
    public void WindowsInTheSameSpotGoByUseTheFocusedOneOnTheRightOrAtTheTop()
    {
        Assert.Equal([Right, Left], Arranged(Small, Small));
        Assert.Equal([new PixelRect(0, 25, 1440, 438), new PixelRect(0, 463, 1440, 437)], Arranged(new ArrangeAction(Layout: "rows"), [Small, Small]));
    }

    [Fact]
    public void ArrangedWindowsStayWhenItRunsAgain()
    {
        Assert.Equal([Right, Left], Arranged(Right, Left));
        Assert.Equal([Left, Right], Arranged(Left, Right));
        Assert.Equal([Right, BottomLeft, TopLeft], Arranged(new ArrangeAction(3), [Right, BottomLeft, TopLeft]));
    }

    [Fact]
    public void ThreeWindowsInTwoColumnsTheFocusedOneTakesTheColumnOnItsSide()
    {
        PixelRect focused = new(0, 30, 400, 300), upper = new(600, 40, 400, 300), lower = new(300, 500, 400, 300);
        Assert.Equal([Left, BottomRight, TopRight], Arranged(new ArrangeAction(3), [focused, lower, upper]));
        Assert.Equal([Right, TopLeft, BottomLeft], Arranged(new ArrangeAction(3), [TopRight, Left, BottomRight]));
    }

    private static PixelRect?[] Five(int place)
    {
        var windows = Enumerable.Range(0, 5).Select(i => new PixelRect(100 + 200 * i, 100 + 50 * i, 400, 300)).ToList();
        var focused = windows[place];
        windows.RemoveAt(place);
        windows.Insert(0, focused);
        var frames = Arranged(new ArrangeAction(5), [.. windows], maxColumns: 3).ToList();
        var first = frames[0];
        frames.RemoveAt(0);
        frames.Insert(place, first);
        return [.. frames];
    }

    private static PixelRect Cell(int column, int? row) => row switch
    {
        0 => new(480 * column, 25, 480, 438),
        1 => new(480 * column, 463, 480, 437),
        _ => new(480 * column, 25, 480, 875),
    };

    [Fact]
    public void FiveWindowsInThreeColumnsTheFocusedOneFillsItsColumn()
    {
        Assert.Equal([Cell(0, 0), Cell(0, 1), Cell(1, 0), Cell(1, 1), Cell(2, null)], Five(4));
        Assert.Equal([Cell(0, 0), Cell(0, 1), Cell(1, null), Cell(2, 0), Cell(2, 1)], Five(2));
        Assert.Equal([Cell(0, null), Cell(1, 0), Cell(1, 1), Cell(2, 0), Cell(2, 1)], Five(0));
    }

    [Fact]
    public void EightWindowsInThreeColumnsTheFocusedOneTakesTheSpareCell()
    {
        PixelRect[] windows = [new(900, 500, 400, 300), new(900, 40, 400, 300), .. Enumerable.Range(0, 6).Select(i => new PixelRect(50 * i, 100, 400, 300))];
        var frames = Arranged(new ArrangeAction(8, 3), windows);
        Assert.Equal(new PixelRect(960, 25, 480, 292), frames[1]);
        Assert.Equal(new PixelRect(960, 317, 480, 583), frames[0]);
        Assert.All(frames.Skip(2), f => Assert.Contains(f!.Value.Height, new[] { 291, 292 }));
    }

    [Fact]
    public void FourWindowsInThreeColumnsTheLastColumnHasOneToo()
    {
        PixelRect[] windows = [.. new[] { 1, 0, 2, 3 }.Select(i => new PixelRect(100 + 200 * i, 100, 400, 300))];
        Assert.Equal([new PixelRect(480, 25, 480, 875), new PixelRect(0, 25, 480, 438), new PixelRect(0, 463, 480, 437), new PixelRect(960, 25, 480, 875)],
            Arranged(new ArrangeAction(4, 3), windows));
    }

    [Fact]
    public void FewerWindowsThanCellsLeaveTheLeftAndBottomEmpty()
    {
        Assert.Equal([Right], Arranged(Small));
        Assert.Equal([new PixelRect(960, 25, 480, 875), new PixelRect(480, 25, 480, 875)], Arranged(new ArrangeAction(3, 3), [Small, Small]));
        Assert.Equal([Left, TopRight], Arranged(new ArrangeAction(3), [Small, Small]));
    }

    [Fact]
    public void WindowsPastTheCountAreLeftAlone() => Assert.Null(Arranged(Small, Small, Small)[2]);

    [Fact]
    public void AllArrangesUpToTheLimit()
    {
        var six = Enumerable.Repeat(Small, 6).ToArray();
        var four = Arranged(new ArrangeAction(null), six);
        Assert.Equal(4, four.Count(f => f is not null));
        Assert.Equal(TopRight, four[0]);
        Assert.Equal([Wide], Arranged(new ArrangeAction(null), [Small]));
        Assert.Equal([Left, TopRight, BottomRight], Arranged(new ArrangeAction(null), six[..3]));
        Assert.Equal([480, 480, 480], Arranged(new ArrangeAction(null), six[..3], maxColumns: 4).Select(f => f!.Value.Width));
        Assert.Equal(8, Arranged(new ArrangeAction(null), Enumerable.Repeat(Small, 10).ToArray(), 8, 8).Count(f => f is not null));
    }

    [Fact]
    public void AnAutomaticLayoutIsRowsOnATallDisplay()
    {
        Assert.Equal([new PixelRect(0, 0, 1080, 960), new PixelRect(540, 960, 540, 960), new PixelRect(0, 960, 540, 960)],
            Arranged(new ArrangeAction(3), [Small, Small, Small], usable: Tall));
        Assert.Equal(new PixelRect(540, 0, 540, 1920), Arranged(new ArrangeAction(Layout: "columns"), [Small, Small], usable: Tall)[0]);
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(1727)]
    [InlineData(3440)]
    [InlineData(1001)]
    public void AnyGridFillsTheAreaAndStaysWhenRunAgain(int width)
    {
        var usable = new PixelRect(0, 0, width, 901);
        for (var count = 2; count <= 8; count++)
            for (var groups = 1; groups <= count; groups++)
                foreach (var layout in new[] { "columns", "rows" })
                {
                    var action = new ArrangeAction(count, groups, layout);
                    var start = Arranged(action, Enumerable.Repeat(Small, count).ToArray(), usable: usable).OfType<PixelRect>().ToList();
                    for (var focused = 0; focused < count; focused++)
                    {
                        var windows = start.ToList();
                        var f = windows[focused];
                        windows.RemoveAt(focused);
                        windows.Insert(0, f);
                        var cells = Arranged(action, [.. windows], usable: usable).OfType<PixelRect>().ToArray();
                        var areas = cells.Select(c => (long)c.Width * c.Height).ToList();
                        Assert.Equal(count, cells.Length);
                        Assert.Equal((long)width * 901, areas.Sum());
                        Assert.True(areas[0] >= areas.Max() - Math.Max(width, 901), $"{count} in {groups}, {layout}");
                        Assert.Equal(cells.Cast<PixelRect?>(), Arranged(action, cells, usable: usable));
                    }
                }
    }

    [Fact]
    public void ItReadsWritesAndPlans()
    {
        var family = ActionRegistry.Standard.Family("window.arrange")!;
        var all = new ConfigReader(ActionRegistry.Standard).ReadAction("""{ "type": "window.arrange", "count": "all", "groups": 3, "layout": "rows" }""", "a");
        Assert.Equal(new ArrangeAction(null, 3, "rows"), all);
        Assert.Equal("""{ "type": "window.arrange", "count": "all", "groups": 3, "layout": "rows" }""", ConfigWriter.Action(all, []));
        Assert.Equal("""{ "type": "window.arrange" }""", ConfigWriter.Action(new ArrangeAction(), []));
        Assert.Equal(new ActionPlan.Window(new WindowCommand.Arrange(new ArrangeAction(3))), family.Plan(new ArrangeAction(3), PlanContext.Current));
    }

    [Fact]
    public void TheLimitsAreCheckedAndWrittenOnlyWhenChanged()
    {
        const string start = """{ "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "window-arrange" } ]""";
        var plain = ConfigLoader.Load(start + " }").Loaded!.Config;
        Assert.DoesNotContain("windows", ConfigWriter.Write(plain));
        var three = ConfigLoader.Load(start + """, "windows": { "maxColumns": 3 } }""").Loaded!.Config;
        Assert.Equal(3, three.Windows.MaxColumns);
        Assert.Contains("""  "windows": { "maxColumns": 3 }""", ConfigWriter.Write(three));
        Assert.Contains(ConfigLoader.Load(start + """, "windows": { "maxRows": 9 } }""").Errors, e => e.Rule == Rule.BadWindowLimit);
        Assert.Contains(ConfigLoader.Load(start + """, "windows": { "maxColumns": 0 } }""").Errors, e => e.Rule == Rule.BadWindowLimit);
    }
}
