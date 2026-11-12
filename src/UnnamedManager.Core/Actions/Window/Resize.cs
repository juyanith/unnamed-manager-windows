namespace UnnamedManager.Core.Config;

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
