namespace UnnamedManager.Core.Config;

/// Run Command: starts a process directly, with no shell.
public sealed record RunCommandAction(IReadOnlyList<string> Command, string? Cwd, IReadOnlyList<KeyValuePair<string, string>> Env) : ConfigAction
{
    public override string Type => "exec";
    public override string DisplayName => "Run Command";
}

public sealed class RunCommandFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("exec", "Run Command")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("command", "cwd", "env");
        var command = f.Strings("command") ?? throw f.Problem(Rule.MissingField, $"Missing “command” in {f.Place}.");
        if (command.Count == 0) throw f.Problem(Rule.MissingField, $"“command” in {f.Place} is empty. Add the program to run.");
        return new RunCommandAction(command, f.OptionalString("cwd"), f.StringMap("env") ?? []);
    }
}
