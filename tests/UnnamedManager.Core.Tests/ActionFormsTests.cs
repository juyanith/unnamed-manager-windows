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
}
