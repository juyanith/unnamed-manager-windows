namespace UnnamedManager.Core.Config;

// The window actions' fields (spec v2, Window actions). M3 builds what they do.

/// A frame as fractions of the usable area.
public readonly record struct FractionFrame(double X, double Y, double Width, double Height);

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
}

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
}

/// Place Window: a preset, or a frame of fractions.
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
}

/// Move Window: to a place in the free space (fractions), or by points.
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
}

/// Resize Window: to a size, by points, or a step through widths.
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
}

/// Focus Window: the nearest window in a direction, or the next or previous one.
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
}

internal static class Fractions
{
    public static void From0To1(ActionFields f, string field, params (string Name, double Value)[] values)
    {
        foreach (var (name, value) in values)
            if (value is < 0 or > 1) throw f.Problem(Rule.BadFraction, $"“{name}” must be from 0 to 1 in “{field}” in {f.Place}.");
    }

    public static void Size(ActionFields f, string field, params (string Name, double Value)[] values)
    {
        foreach (var (name, value) in values)
            if (!(value > 0 && value <= 1)) throw f.Problem(Rule.BadFraction, $"“{name}” must be above 0 and at most 1 in “{field}” in {f.Place}.");
    }
}
