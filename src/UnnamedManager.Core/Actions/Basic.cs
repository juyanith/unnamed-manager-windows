namespace UnnamedManager.Core.Config;

/// Do Nothing and Show Help: no fields.
public sealed record BasicAction(string Kind) : ConfigAction
{
    public override string Type => Kind;
    public override string DisplayName => Kind == "help" ? "Show Help" : "Do Nothing";
}

public sealed class BasicFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("noop", "Do Nothing"), new("help", "Show Help")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow();
        return new BasicAction(f.Type);
    }
}
