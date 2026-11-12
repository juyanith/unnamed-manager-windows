using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Focus Window: the nearest window in a direction (S-028), or the next or previous one (S-029).
public sealed record FocusAction(string Direction) : ConfigAction
{
    public override string Type => "window.focus";
    public override string DisplayName => "Focus Window";

    public static readonly string[] Directions = ["left", "right", "up", "down", "next", "previous"];
}

public sealed class FocusFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("window.focus", "Focus Window")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("direction");
        var direction = f.Choice("direction", FocusAction.Directions, Rule.BadWindowValue)
            ?? throw f.Problem(Rule.MissingField, $"Missing “direction” in {f.Place}.");
        return new FocusAction(direction);
    }

    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) => [new("direction", ((FocusAction)action).Direction)];

    public IReadOnlyList<JsonObject> Schema(string type) =>
        [ActionSchema.Variant(type, new JsonObject { ["direction"] = new JsonObject { ["enum"] = new JsonArray([.. FocusAction.Directions.Select(d => (JsonNode)d)]) } }, "direction")];

    public ActionPlan? Plan(ConfigAction action, PlanContext context) =>
        action is FocusAction { Direction: "left" or "right" or "up" or "down" } focus ? new ActionPlan.Window(new WindowCommand.Focus(focus.Direction)) : null;
}
