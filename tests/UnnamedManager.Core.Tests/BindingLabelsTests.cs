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
