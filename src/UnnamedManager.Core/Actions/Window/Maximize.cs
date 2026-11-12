using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Maximize (S-022): fill the work area, and with `toggle`, go back.
public sealed record MaximizeAction(bool Toggle = true) : ConfigAction
{
    public override string Type => "window.maximize";
    public override string DisplayName => "Maximize";
}

public sealed class MaximizeFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("window.maximize", "Maximize")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("toggle");
        return new MaximizeAction(f.Bool("toggle", true));
    }

    /// `toggle` is left out for the default, true.
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) =>
        ((MaximizeAction)action).Toggle ? [] : [new("toggle", false)];

    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        ActionSchema.Variant(type, new JsonObject
        {
            ["toggle"] = new JsonObject
            {
                ["description"] = "true (default): a window that already fills the usable area goes back to where it was.",
                ["type"] = "boolean",
            },
        }),
    ];

    public ActionPlan Plan(ConfigAction action, PlanContext context) =>
        new ActionPlan.Window(new WindowCommand.Maximize(((MaximizeAction)action).Toggle));
}
