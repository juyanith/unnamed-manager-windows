using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

// S-060: app, file and URL icons in the Guide.
public class GuideIconsTests
{
    private const string Home = @"C:\Users\me";

    [Fact]
    public void UseApplicationShowsItsExeOrItsStartEntry()
    {
        Assert.Equal(new IconSource.Exe("Code.exe"), GuideIcons.For(new UseApplicationAction("Code.exe", null), Home));
        Assert.Equal(new IconSource.AppId("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"),
            GuideIcons.For(new UseApplicationAction(null, "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"), Home));
    }

    [Fact]
    public void OpenShowsTheFileOrTheAppForTheUrl()
    {
        Assert.Equal(new IconSource.File(@"C:\Users\me\Documents"), GuideIcons.For(new OpenAction(@"~\Documents"), Home));
        Assert.Equal(new IconSource.File(@"D:\notes.txt"), GuideIcons.For(new OpenAction(@"D:\notes.txt"), Home));
        Assert.Equal(new IconSource.Url("https"), GuideIcons.For(new OpenAction("https://example.com/x"), Home));
        Assert.Equal(new IconSource.Url("mailto"), GuideIcons.For(new OpenAction("mailto:me@example.com"), Home));
    }

    // S-093
    [Theory]
    [InlineData("U+E756", 0xE756)]
    [InlineData("u+e756", 0xE756)]
    [InlineData("U+01F600", 0x1F600)]
    public void ACodePointIsAGlyph(string icon, int code) =>
        Assert.Equal(new IconSource.Glyph(char.ConvertFromUtf32(code)), GuideIcons.Own(icon, Home));

    [Fact]
    public void AnythingElseIsAFileWithHomeExpanded()
    {
        Assert.Equal(new IconSource.File(@"C:\Users\me\app.ico"), GuideIcons.Own(@"~\app.ico", Home));
        Assert.Equal(new IconSource.File(@"C:\Tools\app.exe"), GuideIcons.Own(@"C:\Tools\app.exe", Home));
        Assert.Null(GuideIcons.Own("U+D800", Home));
        Assert.Null(GuideIcons.Own("", Home));
    }

    [Fact]
    public void AnEntrysOwnIconWinsOverItsActions()
    {
        var loaded = ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "Hyper+A", "title": "Own", "icon": "U+E756", "action": "app" },
              { "keys": "Hyper+B", "title": "App", "action": "app" },
              { "keys": "Hyper+C", "title": "Menu", "icon": "U+E80F" },
              { "keys": "Hyper+C A", "title": "Under", "action": "noop" } ],
              "actions": { "app": { "type": "application", "exe": "Code.exe" } } }
            """).Loaded!;
        var shown = loaded.Leaders.Select(n => GuideIcons.For(n, loaded.Config, Home)!);
        Assert.Equal([new IconSource.Glyph("\uE756"), new IconSource.Exe("Code.exe"), new IconSource.Glyph("\uE80F")], shown);
        Assert.Contains(new IconSource.Glyph("\uE80F"), GuideIcons.All(loaded, Home));
    }

    [Fact]
    public void OtherActionsHaveNoIcon()
    {
        Assert.Null(GuideIcons.For(new FillAction(), Home));
        Assert.Null(GuideIcons.For(null, Home));
    }

    [Fact]
    public void EachSourceIsLoadedOncePerConfig()
    {
        var loaded = ConfigLoader.Load("""
            { "version": 1, "entries": [
              { "keys": "Hyper+Space", "title": "Commands" },
              { "keys": "Hyper+Space A", "title": "A", "action": "code" },
              { "keys": "Hyper+Space B", "title": "B", "action": "code-again" },
              { "keys": "Hyper+Space W", "title": "W", "action": "web" },
              { "keys": "Hyper+Space M", "title": "M", "action": "max" } ],
              "actions": { "code": { "type": "application", "exe": "Code.exe" }, "code-again": { "type": "application", "exe": "code.exe" },
                "web": { "type": "open", "target": "https://example.com" }, "max": { "type": "window.maximize" } } }
            """).Loaded!;
        Assert.Equal(["exe|code.exe", "url|https"], GuideIcons.All(loaded, Home).Select(s => s.Key));
    }
}
