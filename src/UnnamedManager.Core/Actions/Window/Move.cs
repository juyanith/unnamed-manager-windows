using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Move Window (S-026): to a place in the free space (fractions), or by points.
public sealed record MoveAction(double? ToX, double? ToY, double Dx, double Dy) : ConfigAction
{
    public override string Type => "window.move";
    public override string DisplayName => "Move Window";
    public bool IsTo => ToX is not null;
}

public sealed class MoveFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("window.move", "Move Window")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("to", "by");
        if (f.OneOf("to", "by") == "to")
        {
            var to = f.NumberObject("to", ["x", "y"])!;
            Fractions.From0To1(f, "to", ("x", to["x"]), ("y", to["y"]));
            return new MoveAction(to["x"], to["y"], 0, 0);
        }
        var by = f.NumberObject("by", [], ["dx", "dy"])!;
        return new MoveAction(null, null, by.GetValueOrDefault("dx"), by.GetValueOrDefault("dy"));
    }

    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) => action is MoveAction { IsTo: true } m
        ? [new("to", WindowSchema.Numbers(false, ("x", m.ToX!.Value), ("y", m.ToY!.Value)))]
        : [new("by", WindowSchema.Numbers(true, ("dx", ((MoveAction)action).Dx), ("dy", ((MoveAction)action).Dy)))];

    public IReadOnlyList<JsonObject> Schema(string type)
    {
        var to = WindowSchema.Object(new JsonObject { ["x"] = WindowSchema.Fraction(), ["y"] = WindowSchema.Fraction() }, "x", "y");
        to["description"] = "Fractions of the free space: 0 is the left or top edge, 0.5 centered, 1 the right or bottom edge.";
        var by = WindowSchema.Object(new JsonObject { ["dx"] = WindowSchema.Points(), ["dy"] = WindowSchema.Points() });
        by["description"] = "Points. Positive dy moves down.";
        return [ActionSchema.Variant(type, new JsonObject { ["by"] = by, ["to"] = to })];
    }

    public ActionPlan Plan(ConfigAction action, PlanContext context) => new ActionPlan.Window(new WindowCommand.Move((MoveAction)action));
}
