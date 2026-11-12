using System.Text.Json.Nodes;

namespace UnnamedManager.Core.Config;

/// Open: a file, folder or URL with its default app (S-011).
public sealed record OpenAction(string Target) : ConfigAction
{
    public override string Type => "open";
    public override string DisplayName => "Open";
}

public sealed class OpenFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("open", "Open")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("target");
        return new OpenAction(f.RequiredString("target"));
    }

    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) =>
        [KeyValuePair.Create("target", (JsonNode)((OpenAction)action).Target)];

    public IReadOnlyList<JsonObject> Schema(string type) =>
        [ActionSchema.Variant("open", new JsonObject { ["target"] = new JsonObject { ["type"] = "string" } }, "target")];

    /// Only a leading `~` is expanded; a URL or any other text goes to the shell as written.
    public ActionPlan Plan(ConfigAction action, PlanContext context) =>
        new ActionPlan.Open(context.Expand(((OpenAction)action).Target));
}
