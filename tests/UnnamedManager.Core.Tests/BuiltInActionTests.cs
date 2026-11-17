using UnnamedManager.Core.Config;
using UnnamedManager.Core.Windows;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// Built-in actions (spec v11): names that need no definition, and overrides.
public class BuiltInActionTests
{
    [Theory]
    [InlineData("window-fill")]
    [InlineData("window-minimize")]
    [InlineData("window-place-left-two-thirds")]
    [InlineData("window-resize-shrink")]
    [InlineData("window-arrange")]
    [InlineData("window-arrange-8")]
    [InlineData("window-arrange-all")]
    [InlineData("window-focus-recent")]
    [InlineData("window-focus-12")]
    [InlineData("window-swap-first")]
    [InlineData("help")]
    [InlineData("noop")]
    public void TheseAreBuiltIn(string name) => Assert.NotNull(BuiltInActions.Action(name));

    [Theory]
    [InlineData("window-place-leftTwoThirds")]
    [InlineData("Window-fill")]
    [InlineData("window-focus-0")]
    [InlineData("window-focus-02")]
    [InlineData("window-arrange-9")]
    [InlineData("window-arrange-1")]
    [InlineData("window-maximize")]
    [InlineData("window-fill-")]
    [InlineData("window-move")]
    [InlineData("application")]
    public void TheseArent(string name) => Assert.Null(BuiltInActions.Action(name));

    [Fact]
    public void ANameStandsForItsActionAtTheDefaults()
    {
        Assert.Equal(new PlaceAction("leftTwoThirds", null), BuiltInActions.Action("window-place-left-two-thirds"));
        Assert.Equal(new FocusAction(WindowTarget.Of(2)), BuiltInActions.Action("window-focus-2"));
        Assert.Equal(new ArrangeAction(3), BuiltInActions.Action("window-arrange-3"));
        Assert.Equal(new FillAction(), BuiltInActions.Action("window-fill"));
    }

    [Fact]
    public void TheListedOnesEachStandForTheirOwnActionNoneTwice()
    {
        var entries = BuiltInActions.Entries();
        Assert.Equal(entries.Count, entries.Select(e => e.Name).Distinct().Count());
        Assert.Equal(entries.Count, entries.Select(e => ConfigWriter.Action(e.Action, [])).Distinct().Count());
        Assert.All(entries, e => Assert.Equal(ConfigWriter.Action(e.Action, []), ConfigWriter.Action(BuiltInActions.Action(e.Name)!, [])));
        Assert.DoesNotContain(entries, e => e.Name == "window-arrange-2");
        Assert.Contains(entries, e => e.Name == "window-place-bottom-right");
    }

    [Fact]
    public void AnActionWithABuiltInNameOverridesItWithNoWarning()
    {
        var loaded = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "window-focus-left" } ],
              "actions": { "window-focus-left": { "type": "noop" } } }
            """).Loaded!;
        Assert.Empty(loaded.Warnings);
        Assert.Equal(new BasicAction("noop"), loaded.Config.Resolve("window-focus-left"));
        Assert.True(BuiltInActions.IsBuiltIn("window-focus-left"));
    }

    [Fact]
    public void ABuiltInActionHasALabel()
    {
        Assert.Equal("Place Window: Left half", BuiltInActions.Label(BuiltInActions.Action("window-place-left-half")!));
        Assert.Equal("Swap Window: Window 3", BuiltInActions.Label(BuiltInActions.Action("window-swap-3")!));
        Assert.Equal("Arrange Windows: All on the display", BuiltInActions.Label(BuiltInActions.Action("window-arrange-all")!));
        Assert.Equal("Fill", BuiltInActions.Label(BuiltInActions.Action("window-fill")!));
    }

    [Fact]
    public void FocusWindowsOldFieldStillLoadsAndIsWrittenBack()
    {
        var reader = new ConfigReader(ActionRegistry.Standard);
        var old = reader.ReadAction("""{ "type": "window.focus", "direction": "next" }""", "a");
        Assert.Equal("""{ "type": "window.focus", "direction": "next" }""", ConfigWriter.Action(old, []));
        Assert.Equal("""{ "type": "window.focus", "target": 3, "scope": "display" }""",
            ConfigWriter.Action(reader.ReadAction("""{ "type": "window.focus", "target": 3, "scope": "display" }""", "a"), []));
        Assert.Equal(Rule.ConflictingFields, Assert.Throws<ConfigProblem>(() => reader.ReadAction("""{ "type": "window.focus", "direction": "next", "target": "next" }""", "a")).Rule);
    }

    [Fact]
    public void MaximizeStillLoadsAsFillAndIsWrittenBackUnderItsName()
    {
        var old = new ConfigReader(ActionRegistry.Standard).ReadAction("""{ "type": "window.maximize", "toggle": false }""", "a");
        Assert.Equal(new FillAction(false, FillAction.OldType), old);
        Assert.Equal("""{ "type": "window.maximize", "toggle": false }""", ConfigWriter.Action(old, []));
        Assert.Equal("window.fill", ActionForms.FormType(old));
        Assert.DoesNotContain(ActionForms.Types(), k => k.Type == FillAction.OldType);
        Assert.Contains(ActionForms.Types(), k => k.Type == FillAction.CurrentType);
    }
}
