using System.Text.Json.Nodes;

namespace UnnamedManager.Core.Config;

/// Do Nothing (S-015) and Show Help: no fields.
public sealed record BasicAction(string Kind) : ConfigAction
{
    public override string Type => Kind;
    public override string DisplayName => Kind == "help" ? "Show Help" : "Do Nothing";
}

public sealed class BasicFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("noop", "Do Nothing"), new("help", "Show Help")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow();
        return new BasicAction(f.Type);
    }

    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) => [];

    public IReadOnlyList<JsonObject> Schema(string type) => [ActionSchema.Variant(type, new JsonObject())];

    /// Built-in actions `help` and `noop` (spec v11).
    public ConfigAction? BuiltIn(string type, string? value) => value is null ? new BasicAction(type) : null;

    public IReadOnlyList<string?> BuiltInValues(string type) => [null];

    /// Do Nothing ends the sequence and does nothing else, which holds a key's place
    /// or shows a note in the Guide.
    public ActionPlan Plan(ConfigAction action, PlanContext context) =>
        ((BasicAction)action).Kind == "help" ? new ActionPlan.ShowHelp() : new ActionPlan.Nothing();
}
