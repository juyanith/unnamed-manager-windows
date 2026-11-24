using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-038: what each row of the Bindings page says.
public class BindingLabelsTests
{
    private static readonly LoadedConfig Config = ConfigLoader.Load("""
        { "version": 1, "entries": [
            { "keys": "Hyper+Space", "title": "Commands" },
            { "keys": "Hyper+Space PageUp", "title": "Up", "action": "x" },
            { "keys": "Ctrl+Alt+T", "title": "Terminal", "action": "x" }
          ], "actions": { "x": { "type": "noop" } } }
        """).Loaded!;

    // S-086
    [Fact]
    public void TheLineUnderAKeySaysWhatThatKeyIs()
    {
        var rows = BindingLabels.All(Config.Leaders).ToList();
        Assert.Equal("A leader is a chord that opens its menu from anywhere.", BindingLabels.KeyHelp(rows[0]));
        Assert.Equal("A single key, pressed after the keys above it.", BindingLabels.KeyHelp(rows[1]));
        Assert.Equal("A direct binding is a chord that runs its action from anywhere, with no menu.", BindingLabels.KeyHelp(rows[2]));
        Assert.StartsWith("A direct binding", BindingLabels.KeyHelp(rows[0], needsAction: true));
    }

    [Fact]
    public void ARowShowsItsOwnKeyTitleAndActionInFileOrder()
    {
        var rows = BindingLabels.All(Config.Leaders).Select(n => $"{BindingLabels.Key(n)} {n.Title} {BindingLabels.Action(n)}");
        Assert.Equal(["Hyper+Space Commands Menu", "PageUp Up x", "Ctrl+Alt+T Terminal x"], rows);
    }

    [Fact]
    public void NarratorReadsKeyTitleActionWithKeysInWords() =>
        Assert.Equal("Page Up, Up, x", BindingLabels.Spoken(Config.Leaders[0].Children[0]));
}
