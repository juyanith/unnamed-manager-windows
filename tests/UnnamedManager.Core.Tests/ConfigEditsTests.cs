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
}
