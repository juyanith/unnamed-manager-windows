using System.Text;
using System.Text.Json.Nodes;

namespace UnnamedManager.Core.Config;

/// Run Command: starts a process directly, with no shell (S-012).
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

    /// `command`, then `cwd` and `env` when they're set, `env` keys sorted.
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action)
    {
        var run = (RunCommandAction)action;
        var fields = new List<KeyValuePair<string, JsonNode>> { KeyValuePair.Create("command", (JsonNode)new JsonArray([.. run.Command.Select(c => (JsonNode)c)])) };
        if (run.Cwd is { } cwd) fields.Add(KeyValuePair.Create("cwd", (JsonNode)cwd));
        if (run.Env.Count > 0)
        {
            var env = new JsonObject();
            foreach (var (key, value) in run.Env.OrderBy(e => e.Key, StringComparer.Ordinal)) env[key] = value;
            fields.Add(KeyValuePair.Create("env", (JsonNode)env));
        }
        return fields;
    }

    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        ActionSchema.Variant("exec", new JsonObject
        {
            ["command"] = new JsonObject { ["items"] = new JsonObject { ["type"] = "string" }, ["minItems"] = 1, ["type"] = "array" },
            ["cwd"] = new JsonObject { ["type"] = "string" },
            ["env"] = new JsonObject { ["additionalProperties"] = new JsonObject { ["type"] = "string" }, ["type"] = "object" },
        }, "command"),
    ];

    /// Only a leading `~` in each part of `command` and in `cwd` is expanded; `cwd`
    /// defaults to the home folder.
    public ActionPlan Plan(ConfigAction action, PlanContext context)
    {
        var run = (RunCommandAction)action;
        var parts = run.Command.Select(context.Expand).ToList();
        return new ActionPlan.Run(parts[0], parts.Skip(1).ToList(), run.Cwd is { } cwd ? context.Expand(cwd) : context.Home, run.Env);
    }
}

/// Windows passes a program one command-line string; each program splits it back
/// into arguments, nearly always with CommandLineToArgvW's rules. Quoting each
/// argument by those rules gives it back exactly: spaces, quotes and backslashes.
public static class CommandLine
{
    public static string Join(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0) return argument;
        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\') { backslashes++; continue; }
            // Backslashes before a quote are doubled, and the quote is escaped.
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            quoted.Append(c);
        }
        // Backslashes before the closing quote are doubled, so it stays a closing quote.
        quoted.Append('\\', backslashes * 2).Append('"');
        return quoted.ToString();
    }
}
