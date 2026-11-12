namespace UnnamedManager.Core.Config;

/// Send Shortcut: presses one or more chords in the app that was in front.
public sealed record SendShortcutAction(KeyPath Keys) : ConfigAction
{
    public override string Type => "send";
    public override string DisplayName => "Send Shortcut";
}

public sealed class SendShortcutFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("send", "Send Shortcut")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("keys");
        var text = f.RequiredString("keys");
        var keys = KeyPath.Parse(text)
            ?? throw f.Problem(Rule.BadKeys, $"The keys “{text}” in {f.Place} have a key that isn't a known name. Check the spelling of each key and modifier.");
        return new SendShortcutAction(keys);
    }
}
