using System.Text.Json.Nodes;
using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-006: Use Application is the first family on the registry with everything
/// a family gives: reading, writing, its schema, its UI name and its plan.
public class UseApplicationTests
{
    private static readonly IActionFamily Family = ActionRegistry.Standard.Family("application")!;

    private static UseApplicationAction Read(string json) =>
        (UseApplicationAction)ConfigLoader.Load($$"""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ],
              "actions": { "a": {{json}} } }
            """).Loaded!.Config.Action("a")!;

    [Fact]
    public void ItsUINameIsUseApplication()
    {
        Assert.Equal("Use Application", Assert.Single(Family.Kinds).DisplayName);
        Assert.Equal("Use Application", new UseApplicationAction("Code.exe", null).DisplayName);
    }

    [Theory]
    [InlineData("""{ "type": "application", "exe": "Code.exe" }""", "exe=Code.exe")]
    [InlineData("""{ "mode": "start", "exe": "C:\\Tools\\x.EXE", "type": "application" }""", "exe=C:\\Tools\\x.EXE mode=start")]
    [InlineData("""{ "type": "application", "mode": "focusOrStart", "appId": "A!B" }""", "appId=A!B")]
    [InlineData("""{ "type": "application", "appId": "A!B", "mode": "focus" }""", "appId=A!B mode=focus")]
    public void ItWritesItsFieldsInAStableOrderWithDefaultsLeftOut(string json, string written)
    {
        var fields = Family.Write(Read(json));
        Assert.Equal(written, string.Join(" ", fields.Select(f => $"{f.Key}={f.Value.GetValue<string>()}")));
    }

    [Fact]
    public void ItsSchemaPartIsTheSpecs()
    {
        var spec = JsonNode.Parse(File.ReadAllText(Path.Combine(SpecFixtureTests.SpecFolder, "config.schema.json")))!;
        foreach (var list in new[] { "action", "plainAction" })
        {
            var specVariants = spec["$defs"]![list]!["oneOf"]!.AsArray()
                .Where(v => v!["properties"]!["type"]!["enum"]![0]!.GetValue<string>() == "application"
                            && v["properties"]!["bundleId"] is null)
                .ToList();
            var ours = Family.Schema("application");
            Assert.Equal(specVariants.Count, ours.Count);
            for (var i = 0; i < ours.Count; i++)
                Assert.True(JsonNode.DeepEquals(specVariants[i], ours[i]), $"{list} variant {i}: {ours[i].ToJsonString()} vs {specVariants[i]!.ToJsonString()}");
        }
    }

    [Theory]
    [InlineData("""{ "type": "application", "exe": "Code.exe" }""", AppMode.FocusOrStart)]
    [InlineData("""{ "type": "application", "exe": "Code.exe", "mode": "focus" }""", AppMode.Focus)]
    [InlineData("""{ "type": "application", "appId": "A!B", "mode": "start" }""", AppMode.Start)]
    public void ItsPlanUsesTheAppWithItsMode(string json, AppMode mode)
    {
        var action = Read(json);
        var plan = Assert.IsType<ActionPlan.UseApp>(Family.Plan(action, new PlanContext(@"C:\Users\me")));
        Assert.Equal(mode, plan.App.Mode);
        Assert.Equal(action, plan.App);
    }

    [Theory]
    [InlineData("Code.exe", "C:\\Program Files\\Microsoft VS Code\\Code.exe", true)]
    [InlineData("code.EXE", "C:\\Program Files\\Microsoft VS Code\\Code.exe", true)]
    [InlineData("Code.exe", "C:\\Other\\Code.exe", true)]
    [InlineData("C:\\Program Files\\Microsoft VS Code\\Code.exe", "c:\\program files\\microsoft vs code\\CODE.exe", true)]
    [InlineData("C:\\Program Files\\Microsoft VS Code\\Code.exe", "C:\\Other\\Code.exe", false)]
    [InlineData("Code.exe", "C:\\Program Files\\Code Helper.exe", false)]
    public void AFileNameMatchesInAnyFolderAndAPathOnlyThatFile(string exe, string running, bool matches) =>
        Assert.Equal(matches, new UseApplicationAction(exe, null).MatchesExe(running));
}
