namespace UnnamedManager.Core.Config;

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
