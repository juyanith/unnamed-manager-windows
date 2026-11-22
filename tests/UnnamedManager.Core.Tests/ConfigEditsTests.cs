using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// The Bindings window's edits (S-039 to S-044) and its draft (S-048).
public class ConfigEditsTests
{
    private static readonly Config.Config Apps = new ConfigReader(ActionRegistry.Standard).Read("""
        { "version": 1, "entries": [
            { "keys": "Hyper+Space", "title": "Commands" },
            { "keys": "Hyper+Space A", "title": "Applications" },
            { "keys": "Hyper+Space A T", "title": "Terminal", "action": "x" },
            { "keys": "Hyper+Space A E", "title": "Editor", "action": "open-10" },
            { "keys": "Hyper+Space B", "title": "Browser", "action": "open-2" },
            { "keys": "Ctrl+Alt+T", "title": "Direct", "action": "x" }
          ], "actions": { "x": { "type": "noop" }, "open-10": { "type": "noop" }, "open-2": { "type": "noop" }, "Alpha": { "type": "noop" } } }
        """);

    private static KeyPath P(string text) => KeyPath.Parse(text)!;

    private static string Keys(Config.Config c) => string.Join(" | ", c.Entries.Select(e => e.Keys));

    // S-039

    [Fact]
    public void ANewEntryGoesAtTheEndOfItsMenuWithTheFirstFreeKey()
    {
        var c = Apps.AddEntry(P("Hyper+Space A"), "New binding", null, out var added);
        Assert.Equal("Hyper+Space A A", added.ToString());
        Assert.Equal("Hyper+Space | Hyper+Space A | Hyper+Space A T | Hyper+Space A E | Hyper+Space A A | Hyper+Space B | Ctrl+Alt+T", Keys(c));
    }

    [Fact]
    public void WithNothingSelectedItIsANewLeaderOnHyperAndALetterOfficeDoesntKeep()
    {
        Apps.AddEntry(null, "New binding", null, out var added);
        Assert.Equal("Hyper+A", added.ToString());
        var withA = Apps.AddEntry(null, "a", "x", out _).AddEntry(null, "b", "x", out _).AddEntry(null, "c", "x", out _);
        withA.AddEntry(null, "d", null, out var next);
        Assert.Equal("Hyper+E", next.ToString()); // Hyper+D belongs to Office
    }

    [Fact]
    public void ANewStepNeverTakesTheCancelOrBackKey()
    {
        var c = Apps with { Runtime = new RuntimeSettings { CancelKey = "A" } };
        c.AddEntry(P("Hyper+Space"), "n", null, out var added);
        Assert.Equal("Hyper+Space C", added.ToString());
    }

    [Theory]
    [InlineData("Hyper+Space A T", "E", KeyProblem.Taken)]
    [InlineData("Hyper+Space A T", "Escape", KeyProblem.CancelOrBack)]
    [InlineData("Hyper+Space A T", "Ctrl+X", KeyProblem.ModifierInStep)]
    [InlineData("Hyper+Space", "Cmd+L", KeyProblem.ReservedByWindows)]
    [InlineData("Hyper+Space A T", "T", KeyProblem.None)]
    [InlineData("Hyper+Space A T", "X", KeyProblem.None)]
    public void AKeyThatClashesIsReported(string path, string key, KeyProblem problem) =>
        Assert.Equal(problem, Apps.CheckKey(P(path), Chord.Parse(key)!.Value));

    // S-040

    [Fact]
    public void ChangingAMenusKeyMovesEverythingUnderIt()
    {
        var c = Apps.ChangeKey(P("Hyper+Space A"), Chord.Parse("P")!.Value, out var renamed);
        Assert.Equal("Hyper+Space P", renamed.ToString());
        Assert.Equal("Hyper+Space | Hyper+Space P | Hyper+Space P T | Hyper+Space P E | Hyper+Space B | Ctrl+Alt+T", Keys(c));
        Assert.Same(Apps, Apps.ChangeKey(P("Hyper+Space A"), Chord.Parse("B")!.Value, out _));
    }

    // S-041

    [Fact]
    public void ActionsSortAlphabeticallyWithNumbersByValueThenNewAction() =>
        Assert.Equal(["Alpha", "open-2", "open-10", "x", "New action…"], Apps.ActionChoices());

    [Fact]
    public void ChangingATitleOrActionRewritesOnlyThatEntry()
    {
        var c = Apps.SetTitle(P("Hyper+Space B"), "Edge").SetAction(P("Hyper+Space B"), "x");
        Assert.Equal(Apps.Entries.Where((_, i) => i != 4), c.Entries.Where((_, i) => i != 4));
        Assert.Equal(new Entry("Hyper+Space B", "Edge", "x"), c.Entries[4]);
    }

    // S-042

    [Fact]
    public void DeletingAMenuTakesEverythingUnderIt()
    {
        var c = Apps.RemoveEntry(P("Hyper+Space A"), out var removed);
        Assert.Equal(3, removed);
        Assert.Equal("Hyper+Space | Hyper+Space B | Ctrl+Alt+T", Keys(c));
    }

    // S-043

    [Fact]
    public void MovingAnItemKeepsItInItsMenuWithItsChildren()
    {
        var down = Apps.Move(P("Hyper+Space A"), 1);
        Assert.Equal("Hyper+Space | Hyper+Space B | Hyper+Space A | Hyper+Space A T | Hyper+Space A E | Ctrl+Alt+T", Keys(down));
        Assert.Same(Apps, Apps.Move(P("Hyper+Space B"), 1));
        Assert.Equal("Hyper+Space | Hyper+Space A | Hyper+Space A E | Hyper+Space A T | Hyper+Space B | Ctrl+Alt+T", Keys(Apps.Move(P("Hyper+Space A E"), -1)));
        Assert.Equal("Ctrl+Alt+T | Hyper+Space | Hyper+Space A | Hyper+Space A T | Hyper+Space A E | Hyper+Space B", Keys(Apps.Move(P("Ctrl+Alt+T"), -1)));
    }

    // S-044

    [Fact]
    public void RenamingAnActionUpdatesItsBindingsAndAnActionInUseIsntDeleted()
    {
        var c = Apps.RenameAction("x", "nothing");
        Assert.Equal(2, c.Users("nothing").Count);
        Assert.Null(c.Action("x"));
        Assert.Equal(["x", "open-10", "open-2", "Alpha"], Apps.Actions.Select(a => a.Key));
        Assert.Equal(["nothing", "open-10", "open-2", "Alpha"], c.Actions.Select(a => a.Key));
        Assert.Same(Apps, Apps.RenameAction("x", "open-2"));
        Assert.Same(Apps, Apps.RemoveAction("x"));
        Assert.Null(Apps.RemoveAction("Alpha").Action("Alpha"));
        Assert.Equal("new-action", Apps.FreeActionId());
    }

    // The draft

    [Fact]
    public void AValidChangeIsSavedAndUndoRestoresIt()
    {
        var saves = new List<Config.Config>();
        var editor = new ConfigEditor(Apps, c => { saves.Add(c); return []; });
        editor.Change("Rename", c => c.SetTitle(P("Hyper+Space B"), "Edge"));
        Assert.Single(saves);
        Assert.Equal("Rename", editor.UndoName);
        Assert.True(editor.Undo());
        Assert.Equal("Browser", editor.Saved.Entry(P("Hyper+Space B"))!.Title);
        Assert.Equal(2, saves.Count);
        Assert.True(editor.Redo());
        Assert.Equal("Edge", editor.Saved.Entry(P("Hyper+Space B"))!.Title);
    }

    [Fact]
    public void ANewBindingWithNoActionWaitsWithItsReasonAndIsSavedOnceItHasOne()
    {
        var saves = 0;
        var editor = new ConfigEditor(Apps, _ => { saves++; return []; });
        KeyPath added = null!;
        editor.Change("New binding", c => c.AddEntry(P("Hyper+Space A"), "New binding", null, out added));
        editor.NeedAction(added);
        Assert.Equal(0, saves);
        Assert.Equal("Choose an action for “Hyper+Space A A”.", Assert.Single(editor.DraftErrors).Message);
        editor.Change("Change action", c => c.SetAction(added, "x"));
        Assert.Empty(editor.DraftErrors);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void AChangeOnDiskReplacesTheDraft()
    {
        var editor = new ConfigEditor(Apps, _ => []);
        var outside = Apps.SetTitle(P("Ctrl+Alt+T"), "Terminal");
        editor.Reload(outside);
        Assert.Same(outside, editor.Draft);
    }

    [Fact]
    public void TheTreeOfADraftFollowsTheFile()
    {
        var tree = Apps.AddEntry(null, "Menu", null, out _).Tree();
        Assert.Equal(["Commands", "Direct", "Menu"], tree.Select(n => n.Title));
        Assert.Equal(["Applications", "Browser"], tree[0].Children.Select(n => n.Title));
    }

    // S-046

    private static readonly Config.Config Browsers = new ConfigReader(ActionRegistry.Standard).Read("""
        { "version": 1, "entries": [ { "keys": "Ctrl+Alt+L", "title": "Address bar", "action": "bar" } ],
          "actions": { "bar": { "type": "contextual", "variants": { "browser": { "type": "send", "keys": "Ctrl+L" } } } },
          "contexts": [ { "id": "browser", "apps": ["msedge.exe"] }, { "id": "editors", "apps": ["Code.exe"] } ] }
        """);

    [Fact]
    public void ContextsAreAddedRenamedWithTheirVariantsAndReordered()
    {
        var added = Browsers.AddContext(Browsers.FreeContextId()).SetContextApps("new-context", ["firefox.exe"]);
        Assert.Equal(["browser", "editors", "new-context"], added.Contexts.Select(c => c.Id));
        Assert.Equal(["firefox.exe"], added.Contexts[2].Apps);
        var renamed = Browsers.RenameContext("browser", "browsers");
        Assert.Equal("browsers", Assert.IsType<ContextualAction>(renamed.Action("bar")).Variants[0].Key);
        Assert.True(ConfigLoader.Load(ConfigWriter.Write(renamed)).IsValid);
        Assert.Equal(["editors", "browser"], Browsers.MoveContext("browser", 1).Contexts.Select(c => c.Id));
        Assert.Same(Browsers, Browsers.MoveContext("browser", -1));
    }

    [Fact]
    public void AContextAVariantUsesIsntDeleted()
    {
        Assert.Same(Browsers, Browsers.RemoveContext("browser"));
        Assert.Equal(["bar"], Browsers.ContextUsers("browser"));
        Assert.Equal(["browser"], Browsers.RemoveContext("editors").Contexts.Select(c => c.Id));
    }

    // S-096, S-097
    [Fact]
    public void EditingOneVariantLeavesTheOthersAndTheFallbackExactly()
    {
        var config = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Hyper+A", "title": "A", "action": "a" } ],
              "actions": { "a": { "type": "contextual",
                "variants": { "one": { "type": "send", "keys": "Ctrl+L" }, "two": { "type": "window.move", "to": { "x": 0.333333333, "y": 0.5 } } },
                "fallback": { "type": "send", "keys": "Ctrl+F" } } },
              "contexts": [ { "id": "one", "apps": ["a.exe"] }, { "id": "two", "apps": ["b.exe"] } ] }
            """).Loaded!.Config;
        var before = (ContextualAction)config.Action("a")!;
        var changed = config.SetVariant("a", "one", new BasicAction("noop"));
        var after = (ContextualAction)changed.Action("a")!;
        Assert.Same(before.Variants[1].Value, after.Variants[1].Value);
        Assert.Same(before.Fallback, after.Fallback);
        Assert.Equal("noop", after.Variants[0].Value.Type);

        var without = config.SetVariant("a", "one", null).SetVariant("a", "two", null).SetFallback("a", null);
        Assert.Empty(((ContextualAction)without.Action("a")!).Variants);
        // With nothing in it, it's still a file that loads, as a new one is.
        Assert.True(ConfigLoader.Load(ConfigWriter.Write(without)).IsValid);
    }

    // S-088
    [Fact]
    public void SortPutsNamedKeysThenDigitsThenLettersAndTakesWhatsUnderEach()
    {
        var config = ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Main" },
              { "keys": "Hyper+Space B", "title": "B", "action": "x" },
              { "keys": "Hyper+Space A", "title": "A" },
              { "keys": "Hyper+Space A Z", "title": "Under Z", "action": "x" },
              { "keys": "Hyper+Space A C", "title": "Under C", "action": "x" },
              { "keys": "Hyper+Space 9", "title": "9", "action": "x" },
              { "keys": "Hyper+Space F10", "title": "F10", "action": "x" },
              { "keys": "Hyper+Space Space", "title": "Space", "action": "x" },
              { "keys": "Hyper+Space 0", "title": "0", "action": "x" },
              { "keys": "Hyper+Space F2", "title": "F2", "action": "x" },
              { "keys": "Ctrl+Alt+K", "title": "Other", "action": "x" } ],
              "actions": { "x": { "type": "noop" } } }
            """).Loaded!.Config;
        var sorted = config.SortChildren(KeyPath.Parse("Hyper+Space")!);
        Assert.Equal(["Main", "F2", "F10", "Space", "0", "9", "A", "Under Z", "Under C", "B", "Other"], sorted.Entries.Select(e => e.Title));
        Assert.Same(sorted, sorted.SortChildren(KeyPath.Parse("Hyper+Space")!));
    }

    // S-087
    [Fact]
    public void ACopyGoesRightAfterTheOriginalWithEverythingUnderIt()
    {
        var config = ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Main" },
              { "keys": "Hyper+Space A", "title": "Apps", "icon": "U+E71D" },
              { "keys": "Hyper+Space A T", "title": "Terminal", "action": "x", "icon": "U+E756" },
              { "keys": "Hyper+Space A R", "title": "Recent", "list": { "type": "windows.recent", "count": 4 } },
              { "keys": "Hyper+Space Z", "title": "Last", "action": "x" },
              { "keys": "Alt+Tab", "title": "Held", "hold": true },
              { "keys": "Alt+Tab X", "title": "One", "action": "x" } ],
              "actions": { "x": { "type": "noop" } } }
            """).Loaded!.Config;
        var copied = config.Duplicate(KeyPath.Parse("Hyper+Space A")!, out var added);
        Assert.Equal("Hyper+Space B", added.ToString());
        Assert.Equal(["Hyper+Space B", "Hyper+Space B T", "Hyper+Space B R"], copied.Entries.Skip(4).Take(3).Select(e => e.Keys));
        Assert.Equal(config.Entries[1] with { Keys = "Hyper+Space B", Title = "Apps copy" }, copied.Entries[4]);
        Assert.Equal(config.Entries[2] with { Keys = "Hyper+Space B T" }, copied.Entries[5]);
        Assert.Equal(config.Entries[3] with { Keys = "Hyper+Space B R" }, copied.Entries[6]);
        Assert.Equal("Hyper+Space Z", copied.Entries[7].Keys);
        Assert.Empty(ConfigValidator.Validate(copied).Errors);

        var held = config.Duplicate(KeyPath.Parse("Alt+Tab")!, out var leader);
        Assert.True(held.Entry(leader)!.Hold);
        Assert.Empty(ConfigValidator.Validate(held).Errors);
    }

    [Fact]
    public void ACopyInAHeldMenuTakesAKeyThatCantClash()
    {
        // A is taken, B is the repeat key, C is the copy's child's key.
        var config = ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "Alt+B", "title": "Held", "hold": true },
              { "keys": "Alt+B A", "title": "More" },
              { "keys": "Alt+B A C", "title": "One", "action": "x" } ],
              "actions": { "x": { "type": "noop" } } }
            """).Loaded!.Config;
        var copied = config.Duplicate(KeyPath.Parse("Alt+B A")!, out var added);
        Assert.Equal("Alt+B D", added.ToString());
        Assert.Empty(ConfigValidator.Validate(copied).Errors);

        var entries = new List<Entry> { new("Alt+Tab", "Held", null) { Hold = true } };
        entries.AddRange(Enumerable.Range(0, 25).Select(i => new Entry($"Alt+Tab {(char)('A' + i)}", $"E{i}", "x")));
        entries.Add(new Entry("Alt+Tab Z", "Recent", null) { List = new ListSpec() });
        var full = (config with { Entries = entries }).Duplicate(KeyPath.Parse("Alt+Tab Z")!, out var list);
        Assert.Equal("Alt+Tab F13", list.ToString());
        Assert.Empty(ConfigValidator.Validate(full).Errors);
    }

    // 0.5.1

    [Theory]
    [InlineData("CAG+A", true, "CAG+A B")]
    [InlineData("CAG+A R", true, "CAG+A R A")]
    [InlineData("CAG+A", false, "CAG+A A")]
    public void ANewEntryInAHeldMenuSkipsItsRepeatKey(string parent, bool hold, string expected)
    {
        var config = new ConfigReader(ActionRegistry.Standard).Read($$"""
            { "version": 1, "entries": [ { "keys": "CAG+A", "title": "Held", "hold": {{(hold ? "true" : "false")}} }, { "keys": "CAG+A R", "title": "Menu" } ] }
            """);
        config.AddEntry(P(parent), "New binding", null, out var added);
        Assert.Equal(expected, added.ToString());
    }

    [Fact]
    public void ANewEntryInAMenuInsideAHeldOneSkipsThatMenusKey()
    {
        var config = new ConfigReader(ActionRegistry.Standard).Read("""
            { "version": 1, "entries": [ { "keys": "CAG+Space", "title": "Held", "hold": true }, { "keys": "CAG+Space A", "title": "Inner" } ] }
            """);
        config.AddEntry(P("CAG+Space A"), "New binding", null, out var added);
        Assert.Equal("CAG+Space A B", added.ToString());
    }
}
