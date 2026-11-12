namespace UnnamedManager.Core.Config;

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
