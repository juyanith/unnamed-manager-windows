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
        var rebuilt = ActionForms.KeepNames(ActionForms.Build(action.Type, ActionForms.Values(action), "a"), action);
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
    [InlineData("""{"type":"window.fill"}""")]
    [InlineData("""{"type":"window.focus","target":3,"scope":"display"}""")]
    [InlineData("""{"type":"window.focus","target":"recent"}""")]
    [InlineData("""{"type":"window.swap","target":"first"}""")]
    [InlineData("""{"type":"window.swap","target":12,"scope":"display"}""")]
    [InlineData("""{"type":"window.arrange"}""")]
    [InlineData("""{"type":"window.arrange","count":"all","layout":"rows"}""")]
    [InlineData("""{"type":"window.arrange","count":5,"groups":3}""")]
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

    // 0.5.1: what a form doesn't change stays as the file has it.

    private static string Edit(string json, params (string Field, string Text)[] edits)
    {
        var action = Read(json);
        var values = ActionForms.Values(action);
        foreach (var (field, text) in edits) values[field] = text;
        return ConfigWriter.Action(ActionForms.KeepNames(ActionForms.Build(ActionForms.FormType(action), values, "a", action), action), []);
    }

    [Theory]
    [InlineData("""{"type":"window.resize","step":"grow","widths":[0.333333333333,0.5]}""")]
    [InlineData("""{"type":"window.resize","step":"grow","widths":[0.00001,0.5]}""")]
    [InlineData("""{"type":"window.resize","to":{"width":0.333333333333,"height":0.666666666667}}""")]
    [InlineData("""{"type":"window.resize","by":{"dw":10.12345}}""")]
    [InlineData("""{"type":"window.place","frame":{"x":0.333333333333,"y":0,"width":0.333333333333,"height":1}}""")]
    [InlineData("""{"type":"window.move","to":{"x":0.333333333333,"y":0.5}}""")]
    [InlineData("""{"type":"window.move","by":{"dx":10.12345}}""")]
    [InlineData("""{"type":"window.maximize"}""")]
    [InlineData("""{"type":"exec","command":["tool.exe","","last"]}""")]
    [InlineData("""{"type":"exec","command":["pwsh.exe","-Command","one\ntwo"]}""")]
    [InlineData("""{"type":"exec","command":["tool.exe"],"env":{"A":"one\nend","B":"two\nend"}}""")]
    public void AnUneditedFormKeepsTheActionExactly(string json) =>
        Assert.Equal(ConfigWriter.Action(Read(json), []), Edit(json));

    [Fact]
    public void EveryValidSpecActionSurvivesItsForm()
    {
        foreach (var fixture in SpecFixtureTests.WindowsFixtures().Where(f => f.Valid))
        {
            var loaded = ConfigLoader.LoadFile(Path.Combine(SpecFixtureTests.SpecFolder, "fixtures", fixture.File)).Loaded!;
            foreach (var (id, action) in loaded.Config.Actions.Where(a => ActionForms.Fields(a.Value.Type) is not null))
            {
                var built = ActionForms.KeepNames(ActionForms.Build(ActionForms.FormType(action), ActionForms.Values(action), id, action), action);
                Assert.Equal(ConfigWriter.Action(action, []), ConfigWriter.Action(built, []));
            }
        }
    }

    [Fact]
    public void EditingOneNumberKeepsTheOthersExact()
    {
        Assert.Equal("""{ "type": "window.place", "frame": { "x": 0.333333333333, "y": 0, "width": 0.333333333333, "height": 0.5 } }""",
            Edit("""{"type":"window.place","frame":{"x":0.333333333333,"y":0,"width":0.333333333333,"height":1}}""", ("frame.height", "50")));
        Assert.Equal("""{ "type": "window.resize", "to": { "width": 0.333333333333, "height": 0.5 }, "anchor": "center" }""",
            Edit("""{"type":"window.resize","to":{"width":0.333333333333,"height":0.5}}""", ("anchor", "center")));
    }

    [Fact]
    public void APresetMadeCustomKeepsItsExactThirds()
    {
        var json = Edit("""{"type":"window.place","preset":"centerThird"}""", ("preset", "custom"), ("frame.height", "50"));
        var frame = Assert.IsType<PlaceAction>(Read(json)).Frame!.Value;
        Assert.Equal((1.0 / 3, 1.0 / 3, 0.5), (frame.X, frame.Width, frame.Height));
    }

    [Theory]
    [InlineData("33.33\r50\r75", "[0.333333333333, 0.5, 0.75]")]
    [InlineData("25\r33.33\r50", "[0.25, 0.333333333333, 0.5]")]
    [InlineData("50\r33.33", "[0.5, 0.333333333333]")]
    [InlineData("33.33", "[0.333333333333]")]
    [InlineData("40\r50", "[0.4, 0.5]")]
    public void EditingAListOfWidthsKeepsTheWidthsLeftInItExact(string text, string widths) =>
        Assert.Contains($"\"widths\": {widths}", Edit("""{"type":"window.resize","step":"grow","widths":[0.333333333333,0.5]}""", ("widths", text)));

    [Fact]
    public void EditingAnotherFieldKeepsArgumentsTheFormCantShow() =>
        Assert.Equal("""{ "type": "exec", "command": ["tool.exe", "", "last"], "cwd": "~/src" }""",
            Edit("""{"type":"exec","command":["tool.exe","","last"]}""", ("cwd", "~/src")));

    [Fact]
    public void AVariableWrittenTwiceSaysSoInsteadOfCrashing()
    {
        var values = new Dictionary<string, string> { ["command"] = "tool.exe", ["env"] = "A=1\rA=2" };
        Assert.Contains("“A”", Assert.Throws<ConfigProblem>(() => ActionForms.Build("exec", values, "a")).Message);
    }
}
