using System.Text.RegularExpressions;
using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-020: every modifier name in the spec's table, the groups, and the Mac's symbols.
public class ModifierNameTests
{
    [Theory]
    [InlineData("Ctrl+A", "Ctrl+A")]
    [InlineData("CONTROL+a", "Ctrl+A")]
    [InlineData("alt+A", "Alt+A")]
    [InlineData("Opt+A", "Alt+A")]
    [InlineData("option+A", "Alt+A")]
    [InlineData("shift+A", "Shift+A")]
    [InlineData("cmd+A", "Cmd+A")]
    [InlineData("Command+A", "Cmd+A")]
    [InlineData("PRIMARY+A", "Ctrl+A")]
    [InlineData("hyper+A", "Hyper+A")]
    [InlineData("meh+A", "Meh+A")]
    [InlineData("cag+A", "CAG+A")]
    [InlineData("Ctrl+Alt+Shift+W", "Meh+W")]
    [InlineData("Ctrl+Alt+Cmd+T", "CAG+T")]
    [InlineData("Ctrl+Alt+T", "Ctrl+Alt+T")]
    [InlineData("Meh+Cmd+T", "Hyper+T")]
    [InlineData("CAG+Shift+T", "Hyper+T")]
    [InlineData("Cmd+Shift+L", "Shift+Cmd+L")]
    public void EveryNameParsesInAnyCaseAndPrintsBackByTheSpecsNames(string text, string printed) =>
        Assert.Equal(printed, Chord.Parse(text)!.Value.ToString());

    [Fact]
    public void CmdMeansTheWinKey() => Assert.Equal(Modifiers.Win, Chord.Parse("Cmd+E")!.Value.Modifiers);

    [Theory]
    [InlineData("⌃⌥⇧T", "Meh+T")]
    [InlineData("⌘+F", "Cmd+F")]
    [InlineData("⌘F", "Cmd+F")]
    [InlineData("⌃⌥⇧⌘Space", "Hyper+Space")]
    [InlineData("Opt+⌘K", "Alt+Cmd+K")]
    [InlineData("⌃+⌥+Left", "Ctrl+Alt+Left")]
    public void TheMacsSymbolsReadAndTheAppWritesNames(string text, string written) =>
        Assert.Equal(written, KeyPath.Parse(text)!.ToString());

    [Theory]
    [InlineData("Hyper+Space A T")]
    [InlineData("meh+w")]
    [InlineData("CAG+T")]
    [InlineData("Primary+Shift+S")]
    [InlineData("⌃⌥⇧T")]
    [InlineData("⌘+F")]
    [InlineData("Opt+⌘K")]
    [InlineData("  Hyper+Space   PageUp  ")]
    [InlineData("F24 F23")]
    public void TheSchemaAcceptsEveryFormTheAppReads(string text)
    {
        Assert.NotNull(KeyPath.Parse(text));
        Assert.Matches(new Regex(KeyPattern.Path), text);
    }

    [Theory]
    [InlineData("Super+A")]
    [InlineData("Hyper+")]
    [InlineData("Ctrl+F25")]
    public void TheSchemaRejectsWhatTheAppRejects(string text)
    {
        Assert.Null(KeyPath.Parse(text));
        Assert.DoesNotMatch(new Regex(KeyPattern.Path), text);
    }
}
