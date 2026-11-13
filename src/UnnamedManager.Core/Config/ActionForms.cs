using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnnamedManager.Core.Config;

public enum FieldKind { Text, Lines, Pairs, Choice, Toggle }

/// One choice in a Choice field: the value in the file and the words on screen.
public sealed record FieldChoice(string Value, string Label);

/// One field of an action's form on the Actions page (S-044). `Name` is the field in
/// the file. Lines is a list of text, one per line; Pairs is NAME=value per line.
public sealed record FormField(string Name, string Label, FieldKind Kind, string Description = "", IReadOnlyList<FieldChoice>? Choices = null, string Default = "");

/// The Actions page's forms: which fields each type has, an action's values as text,
/// and text back to an action through the family's own reader, so a form checks
/// exactly what the file does.
public static class ActionForms
{
    private static readonly FieldChoice[] Modes =
        [new("focusOrStart", "Focus, or start it"), new("focus", "Only focus"), new("start", "Always start a new one")];

    private static readonly FieldChoice[] Directions =
        [new("left", "Left"), new("right", "Right"), new("up", "Up"), new("down", "Down"), new("next", "Next"), new("previous", "Previous")];

    /// The types in the Type list, alphabetical by their glossary name.
    public static IReadOnlyList<ActionKind> Types(ActionRegistry? registry = null) =>
        (registry ?? ActionRegistry.Standard).Kinds.OrderBy(k => k.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// The fields of a type, or null when its form isn't built (it's edited in the file).
    public static IReadOnlyList<FormField>? Fields(string type) => type switch
    {
        "application" =>
        [
            new("exe", "Exe", FieldKind.Text, "A desktop app's exe, such as Code.exe, or its full path. Leave it empty for a Store app."),
            new("appId", "AppUserModelID", FieldKind.Text, "A Store app's id, such as Microsoft.WindowsTerminal_8wekyb3d8bbwe!App."),
            new("mode", "When it's running", FieldKind.Choice, Choices: Modes, Default: "focusOrStart"),
        ],
        "open" => [new("target", "File, folder or link", FieldKind.Text, "A path such as ~/Documents, or a link such as https://example.com.")],
        "exec" =>
        [
            new("command", "Command", FieldKind.Lines, "The program, then each argument on its own line."),
            new("cwd", "Start in", FieldKind.Text, "The folder it runs in. Empty is your home folder."),
            new("env", "Environment", FieldKind.Pairs, "NAME=value, one per line."),
        ],
        "send" => [new("keys", "Shortcut", FieldKind.Text, "Chords to send, such as Ctrl+Shift+T, or several with spaces.")],
        "noop" or "help" or "window.minimize" => [],
        "window.maximize" => [new("toggle", "Press again to restore", FieldKind.Toggle, Default: "true")],
        "window.focus" => [new("direction", "Direction", FieldKind.Choice, Choices: Directions, Default: "left")],
        _ => null,
    };

    /// An action's fields as text, by field name. Defaults fill what the file leaves out.
    public static Dictionary<string, string> Values(ConfigAction action, ActionRegistry? registry = null)
    {
        var values = (Fields(action.Type) ?? []).ToDictionary(f => f.Name, f => f.Default);
        if ((registry ?? ActionRegistry.Standard).Family(action.Type) is not { } family) return values;
        foreach (var (name, node) in family.Write(action))
        {
            values[name] = node switch
            {
                JsonArray a => string.Join("\n", a.Select(i => i?.GetValue<string>())),
                JsonObject o => string.Join("\n", o.Select(p => $"{p.Key}={p.Value?.GetValue<string>()}")),
                JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>() ? "true" : "false",
                JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
                _ => node?.ToJsonString() ?? "",
            };
        }
        return values;
    }

    /// Builds an action from a form. Empty fields are left out. Throws the reader's
    /// ConfigProblem, with its message, when the form doesn't make a valid action.
    public static ConfigAction Build(string type, IReadOnlyDictionary<string, string> values, string id, ActionRegistry? registry = null)
    {
        var json = new JsonObject { ["type"] = type };
        foreach (var field in Fields(type) ?? [])
        {
            if (!values.TryGetValue(field.Name, out var text) || string.IsNullOrWhiteSpace(text)) continue;
            JsonNode? node = field.Kind switch
            {
                FieldKind.Lines => new JsonArray([.. Lines(text).Select(l => (JsonNode)l)]),
                FieldKind.Pairs => new JsonObject(Lines(text).Select(l => l.Split('=', 2)).Select(p => KeyValuePair.Create(p[0].Trim(), (JsonNode?)(p.Length > 1 ? p[1] : "")))),
                FieldKind.Toggle => text == "true",
                _ => text.Trim(),
            };
            if (field.Kind is FieldKind.Choice or FieldKind.Toggle && text == field.Default) continue;
            json[field.Name] = node;
        }
        return new ConfigReader(registry ?? ActionRegistry.Standard).ReadAction(json.ToJsonString(), id);
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0);
}
