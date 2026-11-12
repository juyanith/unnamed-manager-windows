namespace UnnamedManager.Core.Config;

/// Open: a file, folder or URL with its default app.
public sealed record OpenAction(string Target) : ConfigAction
{
    public override string Type => "open";
    public override string DisplayName => "Open";
}

public sealed class OpenFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("open", "Open")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("target");
        return new OpenAction(f.RequiredString("target"));
    }
}
