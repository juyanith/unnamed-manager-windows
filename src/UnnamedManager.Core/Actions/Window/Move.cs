namespace UnnamedManager.Core.Config;

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
