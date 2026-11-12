namespace UnnamedManager.Core.Config;

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
