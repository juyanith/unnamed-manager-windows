using System.Text.Json.Nodes;

namespace UnnamedManager.Core.Config;

/// Send Shortcut: presses one or more chords in the app that was in front (S-013).
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

    /// The chords as the app writes key paths: canonical names, groups for exact groups.
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) =>
        [KeyValuePair.Create("keys", (JsonNode)((SendShortcutAction)action).Keys.ToString())];

    public IReadOnlyList<JsonObject> Schema(string type) =>
        [ActionSchema.Variant("send", new JsonObject { ["keys"] = new JsonObject { ["pattern"] = KeyPattern.Path, ["type"] = "string" } }, "keys")];

    public ActionPlan Plan(ConfigAction action, PlanContext context)
    {
        var keys = ((SendShortcutAction)action).Keys;
        return new ActionPlan.Send([keys.Leader, .. keys.Steps]);
    }
}

/// The JSON Schema pattern for a key path, built from the same names the reader
/// knows, in the order the spec's schema lists them (spec, Syntax). JSON Schema
/// patterns have no ignore-case flag, so every letter is a [Xx] class.
public static class KeyPattern
{
    private static readonly string[] ModifierNames = ["Control", "Ctrl", "Option", "Opt", "Alt", "Shift", "Command", "Cmd", "Primary", "Hyper", "Meh", "CAG"];

    /// The Mac's modifier symbols, which the reader also takes (S-020).
    public const string Symbols = "[⇧⌃⌘⌥]";

    public static string Chord
    {
        get
        {
            var modifiers = string.Join("|", ModifierNames.Select(AnyCase));
            // Longest first, so "F10" isn't read as "F1" and a stray "0".
            var keys = string.Join("|", Enum.GetValues<KeyName>().Select(k => k.Name()).OrderByDescending(n => n.Length).Select(AnyCase));
            return $"(?:(?:{modifiers}|{Symbols}+)\\+)*{Symbols}*(?:{keys})";
        }
    }

    public static string Path => $"^\\s*{Chord}(?:\\s+{Chord})*\\s*$";

    private static string AnyCase(string word) => string.Concat(word.Select(c =>
        char.ToUpperInvariant(c) == char.ToLowerInvariant(c) ? c.ToString() : $"[{char.ToUpperInvariant(c)}{char.ToLowerInvariant(c)}]"));
}
