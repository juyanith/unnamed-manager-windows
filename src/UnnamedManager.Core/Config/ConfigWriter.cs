using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnnamedManager.Core.Config;

/// Writes a config as JSON with a fixed layout (S-048), the same as the Mac app's:
/// the same key order every time, one line per entry, action and context, and
/// defaults left out, so an edit changes only the lines it touches.
public static class ConfigWriter
{
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write(Config config, ActionRegistry? registry = null)
    {
        registry ??= ActionRegistry.Standard;
        var sections = new List<string>();
        if (config.Schema is { } schema) sections.Add($"  \"$schema\": {Quote(schema)}");
        sections.Add($"  \"version\": {config.Version}");
        if (Runtime(config.Runtime) is { } runtime) sections.Add($"  \"runtime\": {runtime}");
        if (UI(config.UI) is { } ui) sections.Add($"  \"ui\": {ui}");
        if (config.Windows != new WindowSettings()) sections.Add($"  \"windows\": {Object([("gap", Number(config.Windows.Gap))])}");
        sections.Add(List("entries", config.Entries.Select(Entry)));
        if (config.Actions.Count > 0)
        {
            var lines = config.Actions.Select(a => $"    {Quote(a.Key)}: {Action(a.Value, config.Contexts, registry)}");
            sections.Add("  \"actions\": {\n" + string.Join(",\n", lines) + "\n  }");
        }
        if (config.Contexts.Count > 0) sections.Add(List("contexts", config.Contexts.Select(Context)));
        return "{\n" + string.Join(",\n", sections) + "\n}\n";
    }

    /// One action as an inline object, `type` first.
    public static string Action(ConfigAction action, IReadOnlyList<ConfigContext> contexts, ActionRegistry? registry = null)
    {
        registry ??= ActionRegistry.Standard;
        var fields = new List<(string, string)> { ("type", Quote(action.Type)) };
        if (action is ContextualAction contextual)
        {
            // Variants follow the contexts' order, which decides which one wins.
            var order = contexts.Select(c => c.Id).ToList();
            var variants = contextual.Variants.OrderBy(v => order.IndexOf(v.Key) is var i and >= 0 ? i : int.MaxValue).ThenBy(v => v.Key, StringComparer.Ordinal);
            fields.Add(("variants", Object(variants.Select(v => (v.Key, Action(v.Value, contexts, registry))).ToList())));
            if (contextual.Fallback is { } fallback) fields.Add(("fallback", Action(fallback, contexts, registry)));
        }
        else
        {
            var family = registry.Family(action.Type) ?? throw new InvalidOperationException($"No family writes “{action.Type}”.");
            fields.AddRange(family.Write(action).Select(f => (f.Key, Node(f.Value))));
        }
        return Object(fields);
    }

    private static string List(string name, IEnumerable<string> lines)
    {
        var all = lines.ToList();
        return all.Count == 0 ? $"  {Quote(name)}: []" : $"  {Quote(name)}: [\n" + string.Join(",\n", all.Select(l => "    " + l)) + "\n  ]";
    }

    private static string Object(IReadOnlyList<(string Key, string Value)> fields) =>
        fields.Count == 0 ? "{}" : "{ " + string.Join(", ", fields.Select(f => $"{Quote(f.Key)}: {f.Value}")) + " }";

    private static string Entry(Entry entry)
    {
        var fields = new List<(string, string)> { ("keys", Quote(entry.Keys)), ("title", Quote(entry.Title)) };
        if (entry.Action is { } action) fields.Add(("action", Quote(action)));
        if (entry.Icon is { } icon) fields.Add(("icon", Quote(icon)));
        return Object(fields);
    }

    private static string Context(ConfigContext context) =>
        Object([("id", Quote(context.Id)), ("apps", "[" + string.Join(", ", context.Apps.Select(Quote)) + "]")]);

    private static string? Runtime(RuntimeSettings runtime)
    {
        var d = new RuntimeSettings();
        var fields = new List<(string, string)>();
        if (runtime.CancelKey != d.CancelKey) fields.Add(("cancelKey", Quote(runtime.CancelKey)));
        if (runtime.BackKey != d.BackKey) fields.Add(("backKey", Quote(runtime.BackKey)));
        if (runtime.ShowHud != d.ShowHud) fields.Add(("showHud", Bool(runtime.ShowHud)));
        if (runtime.HudDelayMs != d.HudDelayMs) fields.Add(("hudDelayMs", runtime.HudDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (runtime.TimeoutMs != d.TimeoutMs) fields.Add(("timeoutMs", runtime.TimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return fields.Count == 0 ? null : Object(fields);
    }

    private static string? UI(UISettings ui)
    {
        var d = new UISettings();
        var fields = new List<(string, string)>();
        if (ui.Theme != d.Theme) fields.Add(("theme", Quote(ui.Theme)));
        if (ui.Position != d.Position) fields.Add(("position", Quote(ui.Position)));
        if (ui.ErrorFlash != d.ErrorFlash) fields.Add(("errorFlash", Bool(ui.ErrorFlash)));
        if (ui.ErrorBell != d.ErrorBell) fields.Add(("errorBell", Bool(ui.ErrorBell)));
        return fields.Count == 0 ? null : Object(fields);
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Number(double value) => JsonValue.Create(value).ToJsonString();

    /// A family's field value, inline: objects and lists on one line.
    private static string Node(JsonNode? node) => node switch
    {
        JsonObject o => Object(o.Select(p => (p.Key, Node(p.Value))).ToList()),
        JsonArray a => "[" + string.Join(", ", a.Select(Node)) + "]",
        null => "null",
        _ => node.ToJsonString(Relaxed),
    };

    /// A JSON string literal; slashes and non-ASCII text stay as written.
    public static string Quote(string text)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            writer.WriteStringValue(text);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
