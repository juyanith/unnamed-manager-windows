using System.Text.Json.Nodes;
using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// What every built family gives (M2): its schema part is the spec's, and its plan.
public class ActionFamilyTests
{
    private static readonly PlanContext Me = new(@"C:\Users\me");

    /// The types whose families are built: writer, schema and plan. Each story grows this list.
    public static TheoryData<string> BuiltTypes() => new("application", "open", "exec", "send", "noop", "help", "window.maximize", "window.minimize", "window.place", "window.resize", "window.move", "window.focus");

    private static ConfigAction Read(string json) =>
        ConfigLoader.Load($$"""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ],
              "actions": { "a": {{json}} } }
            """).Loaded?.Config.Action("a") ?? throw new InvalidOperationException("didn't load");

    private static ActionPlan? Plan(string json)
    {
        var action = Read(json);
        return ActionRegistry.Standard.Family(action.Type)!.Plan(action, Me);
    }

    [Theory]
    [MemberData(nameof(BuiltTypes))]
    public void ItsSchemaPartIsTheSpecs(string type)
    {
        var spec = JsonNode.Parse(File.ReadAllText(Path.Combine(SpecFixtureTests.SpecFolder, "config.schema.json")))!;
        var ours = ActionRegistry.Standard.Family(type)!.Schema(type);
        Assert.NotEmpty(ours);
        foreach (var list in type == "contextual" ? new[] { "action" } : ["action", "plainAction"])
        {
            var specVariants = spec["$defs"]![list]!["oneOf"]!.AsArray()
                .Where(v => v!["properties"]!["type"]!["enum"]![0]!.GetValue<string>() == type && v["properties"]!["bundleId"] is null)
                .ToList();
            Assert.Equal(specVariants.Count, ours.Count);
            for (var i = 0; i < ours.Count; i++)
                Assert.True(JsonNode.DeepEquals(specVariants[i], ours[i]), $"{type} in {list}: {ours[i].ToJsonString()}\nspec: {specVariants[i]!.ToJsonString()}");
        }
    }

    [Theory]
    [InlineData("~", @"C:\Users\me")]
    [InlineData(@"~\notes", @"C:\Users\me\notes")]
    [InlineData("~/notes/today.md", @"C:\Users\me/notes/today.md")]
    [InlineData("~notes", "~notes")]
    [InlineData(@"C:\work\~\x", @"C:\work\~\x")]
    [InlineData("%TEMP%\\x", "%TEMP%\\x")]
    [InlineData("https://example.com/~me", "https://example.com/~me")]
    public void OnlyALeadingTildeIsExpanded(string target, string opened) =>
        Assert.Equal(new ActionPlan.Open(opened), Plan($$"""{ "type": "open", "target": {{System.Text.Json.JsonSerializer.Serialize(target)}} }"""));

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("mailto:me@example.com", true)]
    [InlineData(@"C:\Users\me\notes", false)]
    [InlineData(@"\\server\share", false)]
    [InlineData("notes.txt", false)]
    public void UrlsAreToldFromPaths(string target, bool url) => Assert.Equal(url, new ActionPlan.Open(target).IsUrl);

    [Fact]
    public void OpenWritesItsTarget() =>
        Assert.Equal("target=~/notes", string.Join(" ", ActionRegistry.Standard.Family("open")!.Write(Read("""{ "type": "open", "target": "~/notes" }"""))
            .Select(f => $"{f.Key}={f.Value.GetValue<string>()}")));

    [Fact]
    public void ADoNothingBindingEndsTheSequenceAndDoesNothingElse()
    {
        var config = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Hyper+Space", "title": "Commands" },
              { "keys": "Hyper+Space R", "title": "Reserved for later", "action": "none" } ],
              "actions": { "none": { "type": "noop" } } }
            """).Loaded!;
        var engine = new Keyboard.SequenceEngine(config);
        engine.Handle(Keyboard.KeyEvent.Down(KeyName.Space, Modifiers.Hyper));
        engine.ModifiersChanged(Modifiers.None);
        var run = Assert.IsType<Keyboard.SequenceEffect.Run>(engine.Handle(Keyboard.KeyEvent.Down(KeyName.R)).Effect);
        Assert.False(engine.IsActive);
        Assert.IsType<ActionPlan.Nothing>(ActionRegistry.Standard.Family("noop")!.Plan(config.Config.Action(run.Binding.Action!)!, Me));
        Assert.Empty(ActionRegistry.Standard.Family("noop")!.Write(new BasicAction("noop")));
    }
}
