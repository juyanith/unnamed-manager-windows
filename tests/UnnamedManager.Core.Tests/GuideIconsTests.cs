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
