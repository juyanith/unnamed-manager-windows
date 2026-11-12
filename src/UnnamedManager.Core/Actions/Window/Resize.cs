using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Resize Window: a step through widths (S-025), to a size or by points (S-027).
public sealed record ResizeAction(string Change, double A, double B, string? Step, IReadOnlyList<double>? Widths, string Anchor) : ConfigAction
{
    public override string Type => "window.resize";
    public override string DisplayName => "Resize Window";

    public static readonly string[] Anchors = ["topLeft", "top", "topRight", "left", "center", "right", "bottomLeft", "bottom", "bottomRight"];
    public static readonly string[] Steps = ["grow", "shrink"];
    public static readonly double[] DefaultWidths = [1.0 / 4, 1.0 / 3, 1.0 / 2, 2.0 / 3, 3.0 / 4];
}

public sealed class ResizeFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("window.resize", "Resize Window")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("to", "by", "step", "anchor", "widths");
        var which = f.OneOf("to", "by", "step");
        if (which == "step")
        {
            if (f.Has("anchor")) throw f.Conflict("anchor", "step");
            var widths = f.Numbers("widths");
            if (widths is { Count: 0 }) throw f.Problem(Rule.BadFraction, $"Add at least one width to “widths” in {f.Place}.");
            if (widths is not null && widths.Any(w => !(w > 0 && w <= 1)))
                throw f.Problem(Rule.BadFraction, $"Each width must be above 0 and at most 1 in “widths” in {f.Place}.");
            var step = f.Choice("step", ResizeAction.Steps, Rule.BadWindowValue)!;
            return new ResizeAction("step", 0, 0, step, widths, "topLeft");
        }
        if (f.Has("widths")) throw f.Conflict("widths", which);
        var anchor = f.Choice("anchor", ResizeAction.Anchors, Rule.BadWindowValue) ?? "topLeft";
        if (which == "to")
        {
            var to = f.NumberObject("to", ["width", "height"])!;
            Fractions.Size(f, "to", ("width", to["width"]), ("height", to["height"]));
            return new ResizeAction("to", to["width"], to["height"], null, null, anchor);
        }
        var by = f.NumberObject("by", [], ["dw", "dh"])!;
        return new ResizeAction("by", by.GetValueOrDefault("dw"), by.GetValueOrDefault("dh"), null, null, anchor);
    }

    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action)
    {
        var r = (ResizeAction)action;
        var fields = new List<KeyValuePair<string, JsonNode>>();
        switch (r.Change)
        {
            case "to": fields.Add(new("to", WindowSchema.Numbers(false, ("width", r.A), ("height", r.B)))); break;
            case "by": fields.Add(new("by", WindowSchema.Numbers(true, ("dw", r.A), ("dh", r.B)))); break;
            default:
                fields.Add(new("step", r.Step!));
                if (r.Widths is { } widths) fields.Add(new("widths", new JsonArray([.. widths.Select(w => (JsonNode)w)])));
                break;
        }
        if (r.Change != "step" && r.Anchor != "topLeft") fields.Add(new("anchor", r.Anchor));
        return fields;
    }

    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        ActionSchema.Variant(type, new JsonObject
        {
            ["anchor"] = new JsonObject
            {
                ["description"] = "The point that stays put with to and by. Default topLeft.",
                ["enum"] = new JsonArray([.. ResizeAction.Anchors.Select(a => (JsonNode)a)]),
            },
            ["by"] = WindowSchema.Object(new JsonObject { ["dh"] = WindowSchema.Points(), ["dw"] = WindowSchema.Points() }),
            ["step"] = new JsonObject { ["enum"] = new JsonArray([.. ResizeAction.Steps.Select(s => (JsonNode)s)]) },
            ["to"] = WindowSchema.Object(new JsonObject { ["height"] = WindowSchema.Size(), ["width"] = WindowSchema.Size() }, "width", "height"),
            ["widths"] = new JsonObject
            {
                ["description"] = "The widths step goes through. Default 1/4, 1/3, 1/2, 2/3, 3/4.",
                ["items"] = WindowSchema.Size(),
                ["minItems"] = 1,
                ["type"] = "array",
            },
        }),
    ];

    public ActionPlan Plan(ConfigAction action, PlanContext context) => new ActionPlan.Window(new WindowCommand.Resize((ResizeAction)action));
}
