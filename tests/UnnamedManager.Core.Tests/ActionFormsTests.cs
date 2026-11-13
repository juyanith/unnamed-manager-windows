using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-044: the Actions page's forms.
public class ActionFormsTests
{
    private static ConfigAction Read(string json) => new ConfigReader(ActionRegistry.Standard).ReadAction(json, "a");

    [Fact]
    public void TheTypeListIsAlphabetical()
    {
        var names = ActionForms.Types().Select(k => k.DisplayName).ToList();
        Assert.Equal(names.Order(StringComparer.CurrentCultureIgnoreCase), names);
        Assert.Contains("Use Application", names);
    }

    [Theory]
    [InlineData("""{"type":"application","exe":"Code.exe","mode":"start"}""")]
    [InlineData("""{"type":"application","appId":"Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"}""")]
    [InlineData("""{"type":"open","target":"~/notes"}""")]
    [InlineData("""{"type":"exec","command":["git","pull"],"cwd":"~/src","env":{"A":"1","B":"x=y"}}""")]
    [InlineData("""{"type":"send","keys":"Ctrl+Shift+T"}""")]
    [InlineData("""{"type":"noop"}""")]
    [InlineData("""{"type":"window.maximize","toggle":false}""")]
    [InlineData("""{"type":"window.focus","direction":"next"}""")]
    public void AFormShowsAnActionsFieldsAndBuildsItBackUnchanged(string json)
    {
        var action = Read(json);
        var rebuilt = ActionForms.Build(action.Type, ActionForms.Values(action), "a");
        Assert.Equal(ConfigWriter.Action(action, []), ConfigWriter.Action(rebuilt, []));
    }

    [Fact]
    public void AFormThatIsntAValidActionSaysWhyInTheReadersWords()
    {
        var values = new Dictionary<string, string> { ["exe"] = "Code.exe", ["appId"] = "x!App" };
        var problem = Assert.Throws<ConfigProblem>(() => ActionForms.Build("application", values, "open-code"));
        Assert.Equal("Use only one of “exe” and “appId” in action “open-code”.", problem.Message);
    }

    [Fact]
    public void DefaultsAreLeftOutOfTheFile() =>
        Assert.Equal("""{ "type": "application", "exe": "Code.exe" }""",
            ConfigWriter.Action(ActionForms.Build("application", new Dictionary<string, string> { ["exe"] = " Code.exe ", ["mode"] = "focusOrStart" }, "a"), []));

    // S-045: the window forms.

    [Theory]
    [InlineData("""{"type":"window.place","preset":"rightThird"}""")]
    [InlineData("""{"type":"window.place","frame":{"x":0.1,"y":0,"width":0.5,"height":1}}""")]
    [InlineData("""{"type":"window.move","to":{"x":0.5,"y":0}}""")]
    [InlineData("""{"type":"window.move","by":{"dx":-40,"dy":20}}""")]
    [InlineData("""{"type":"window.resize","step":"shrink"}""")]
    [InlineData("""{"type":"window.resize","step":"grow","widths":[0.5,0.75]}""")]
    [InlineData("""{"type":"window.resize","to":{"width":0.6,"height":0.8},"anchor":"center"}""")]
    [InlineData("""{"type":"window.resize","by":{"dw":100}}""")]
    public void AWindowFormBuildsItsActionBackUnchanged(string json) =>
        AFormShowsAnActionsFieldsAndBuildsItBackUnchanged(json);

    [Fact]
    public void APresetShowsItsNumbersAndEditingANumberMakesACustomFrame()
    {
        var values = ActionForms.Values(Read("""{"type":"window.place","preset":"leftHalf"}"""));
        Assert.Equal(("leftHalf", "0", "50"), (values["preset"], values["frame.x"], values["frame.width"]));
        Assert.Equal("preset", ActionForms.Fields("window.place")!.Single(f => f.Name == "frame.width").Customizes);
        values["preset"] = "custom";
        values["frame.width"] = "40";
        Assert.Equal("""{ "type": "window.place", "frame": { "x": 0, "y": 0, "width": 0.4, "height": 1 } }""", ConfigWriter.Action(ActionForms.Build("window.place", values, "a"), []));
    }

    [Fact]
    public void PercentFieldsRunFrom0To100AndShowOnlyForTheirWay()
    {
        var fields = ActionForms.Fields("window.resize")!;
        Assert.All(fields.Where(f => f.Name.StartsWith("to.")), f => Assert.Equal((0, 100), (f.Min, f.Max)));
        var widths = fields.Single(f => f.Name == "widths");
        Assert.True(widths.IsShown(new Dictionary<string, string> { ["_how"] = "grow" }));
        Assert.False(widths.IsShown(new Dictionary<string, string> { ["_how"] = "to" }));
    }

    [Fact]
    public void EveryWindowTypeHasAFormAndAGlyph()
    {
        foreach (var kind in ActionForms.Types().Where(k => k.Type.StartsWith("window.")))
        {
            Assert.NotNull(ActionForms.Fields(kind.Type));
            Assert.NotNull(ActionGlyphs.For(ActionForms.Build(kind.Type, ActionForms.Defaults(kind.Type), "a")));
        }
    }
}
