using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Minimize (S-023): send the window in front to the taskbar.
public sealed record MinimizeAction : ConfigAction
{
    public override string Type => "window.minimize";
    public override string DisplayName => "Minimize";
}

public sealed class MinimizeFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("window.minimize", "Minimize")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow();
        return new MinimizeAction();
    }

    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) => [];

    public IReadOnlyList<JsonObject> Schema(string type) => [ActionSchema.Variant(type, new JsonObject())];

    public ActionPlan Plan(ConfigAction action, PlanContext context) => new ActionPlan.Window(new WindowCommand.Minimize());
}
