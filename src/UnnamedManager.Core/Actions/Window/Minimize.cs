namespace UnnamedManager.Core.Config;

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
