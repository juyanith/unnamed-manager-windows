using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Place Window (S-024): a preset, or a frame of fractions.
public sealed record PlaceAction(string? Preset, FractionFrame? Frame) : ConfigAction
{
    public override string Type => "window.place";
    public override string DisplayName => "Place Window";

    public static readonly string[] Presets =
    [
        "leftHalf", "rightHalf", "topHalf", "bottomHalf",
        "leftThird", "centerThird", "rightThird", "leftTwoThirds", "rightTwoThirds",
        "topLeft", "topRight", "bottomLeft", "bottomRight", "center",
    ];

    /// A frame that adds up to 1 within this counts as 1.
    public const double Slack = 0.001;

    /// A preset as exact fractions, so thirds line up.
    public static FractionFrame PresetFrame(string preset)
    {
        const double third = 1.0 / 3, twoThirds = 2.0 / 3, sixth = 1.0 / 6;
        return preset switch
        {
            "leftHalf" => new(0, 0, 0.5, 1),
            "rightHalf" => new(0.5, 0, 0.5, 1),
            "topHalf" => new(0, 0, 1, 0.5),
            "bottomHalf" => new(0, 0.5, 1, 0.5),
            "leftThird" => new(0, 0, third, 1),
            "centerThird" => new(third, 0, third, 1),
            "rightThird" => new(twoThirds, 0, third, 1),
            "leftTwoThirds" => new(0, 0, twoThirds, 1),
            "rightTwoThirds" => new(third, 0, twoThirds, 1),
            "topLeft" => new(0, 0, 0.5, 0.5),
            "topRight" => new(0.5, 0, 0.5, 0.5),
            "bottomLeft" => new(0, 0.5, 0.5, 0.5),
            "bottomRight" => new(0.5, 0.5, 0.5, 0.5),
            "center" => new(sixth, sixth, twoThirds, twoThirds),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };
    }

    public FractionFrame Fractions => Preset is { } p ? PresetFrame(p) : Frame!.Value;
}

public sealed class PlaceFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("window.place", "Place Window")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("preset", "frame");
        if (f.OneOf("preset", "frame") == "preset")
            return new PlaceAction(f.Choice("preset", PlaceAction.Presets, Rule.BadWindowValue), null);
        var n = f.NumberObject("frame", ["x", "y", "width", "height"])!;
        var frame = new FractionFrame(n["x"], n["y"], n["width"], n["height"]);
        Fractions.From0To1(f, "frame", ("x", frame.X), ("y", frame.Y));
        Fractions.Size(f, "frame", ("width", frame.Width), ("height", frame.Height));
        if (frame.X + frame.Width > 1 + PlaceAction.Slack || frame.Y + frame.Height > 1 + PlaceAction.Slack)
            throw f.Problem(Rule.BadFraction, $"The frame runs past the usable area in {f.Place}.");
        return new PlaceAction(null, frame);
    }

    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action)
    {
        var place = (PlaceAction)action;
        if (place.Preset is { } preset) return [new("preset", preset)];
        var f = place.Frame!.Value;
        return [new("frame", new JsonObject { ["x"] = f.X, ["y"] = f.Y, ["width"] = f.Width, ["height"] = f.Height })];
    }

    public IReadOnlyList<JsonObject> Schema(string type)
    {
        JsonObject Fraction(bool size) => size
            ? new JsonObject { ["exclusiveMinimum"] = 0, ["maximum"] = 1, ["type"] = "number" }
            : new JsonObject { ["maximum"] = 1, ["minimum"] = 0, ["type"] = "number" };
        return
        [
            ActionSchema.Variant(type, new JsonObject
            {
                ["frame"] = new JsonObject
                {
                    ["additionalProperties"] = false,
                    ["properties"] = new JsonObject { ["height"] = Fraction(true), ["width"] = Fraction(true), ["x"] = Fraction(false), ["y"] = Fraction(false) },
                    ["required"] = new JsonArray("x", "y", "width", "height"),
                    ["type"] = "object",
                },
                ["preset"] = new JsonObject { ["enum"] = new JsonArray([.. PlaceAction.Presets.Select(p => (JsonNode)p)]) },
            }),
        ];
    }

    public ActionPlan Plan(ConfigAction action, PlanContext context) =>
        new ActionPlan.Window(new WindowCommand.Place(((PlaceAction)action).Fractions));
}
