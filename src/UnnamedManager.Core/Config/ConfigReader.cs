using System.Text.Json;

namespace UnnamedManager.Core.Config;

/// Reads config.json into a <see cref="Config"/>, strictly: the first unknown
/// field, missing field or wrong type stops the read with a <see cref="ConfigProblem"/>.
public sealed class ConfigReader(ActionRegistry registry)
{
    public Config Read(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            var where = e.LineNumber is { } line ? $" on line {line + 1}" : "";
            throw new ConfigProblem(Rule.InvalidJson, $"The config file isn't valid JSON{where}. Check for a missing comma, quote or bracket.");
        }
        using (document) return ReadRoot(document.RootElement);
    }

    private Config ReadRoot(JsonElement root)
    {
        var o = new ObjectReader(root, "the file", null);
        o.Allow("$schema", "version", "runtime", "ui", "windows", "entries", "actions", "contexts");
        var runtime = o.Object("runtime") is { } r ? ReadRuntime(r) : new RuntimeSettings();
        var ui = o.Object("ui") is { } u ? ReadUI(u) : new UISettings();
        var windows = o.Object("windows") is { } w ? ReadWindows(w) : new WindowSettings();
        return new Config
        {
            Schema = o.String("$schema"),
            Version = o.Int("version") ?? throw o.Missing("version"),
            Runtime = runtime,
            UI = ui,
            Windows = windows,
            Entries = (o.Array("entries") ?? throw o.Missing("entries")).Select(ReadEntry).ToList(),
            Actions = (o.Object("actions") is { } a ? a.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, ReadAction(p.Value, p.Name))) : []).ToList(),
            Contexts = (o.Array("contexts") ?? []).Select(ReadContext).ToList(),
        };
    }

    private static RuntimeSettings ReadRuntime(JsonElement e)
    {
        var o = new ObjectReader(e, "runtime", null);
        o.Allow("cancelKey", "backKey", "showHud", "hudDelayMs", "timeoutMs");
        var d = new RuntimeSettings();
        return new RuntimeSettings
        {
            CancelKey = o.String("cancelKey") ?? d.CancelKey,
            BackKey = o.String("backKey") ?? d.BackKey,
            ShowHud = o.Bool("showHud") ?? d.ShowHud,
            HudDelayMs = o.Int("hudDelayMs") ?? d.HudDelayMs,
            TimeoutMs = o.Int("timeoutMs") ?? d.TimeoutMs,
        };
    }

    private static UISettings ReadUI(JsonElement e)
    {
        var o = new ObjectReader(e, "ui", null);
        o.Allow("theme", "position", "errorFlash", "errorBell");
        var d = new UISettings();
        return new UISettings
        {
            Theme = o.String("theme") ?? d.Theme,
            Position = o.String("position") ?? d.Position,
            ErrorFlash = o.Bool("errorFlash") ?? d.ErrorFlash,
            ErrorBell = o.Bool("errorBell") ?? d.ErrorBell,
        };
    }

    private static WindowSettings ReadWindows(JsonElement e)
    {
        var o = new ObjectReader(e, "windows", null);
        o.OtherPlatform("quitOnClose", "Quit on close is for the Mac only, since Windows apps quit when their last window closes");
        o.OtherPlatform("quitOnCloseDelayMs", "Quit on close is for the Mac only, since Windows apps quit when their last window closes");
        o.Allow("gap", "maxColumns", "maxRows", "minVisible", "rowTolerance", "tieTolerance", "apps");
        var d = new WindowSettings();
        return new WindowSettings
        {
            Gap = o.Number("gap") ?? d.Gap,
            MaxColumns = o.Int("maxColumns") ?? d.MaxColumns,
            MaxRows = o.Int("maxRows") ?? d.MaxRows,
            MinVisible = o.Number("minVisible") ?? d.MinVisible,
            RowTolerance = o.Number("rowTolerance") ?? d.RowTolerance,
            TieTolerance = o.Number("tieTolerance") ?? d.TieTolerance,
            Apps = o.Object("apps") is { } apps ? [.. apps.EnumerateObject().Select(ReadAppRules)] : [],
        };
    }

    /// One app's rules in `windows.apps` (spec v15), each true when it's left out.
    private static KeyValuePair<string, AppRules> ReadAppRules(JsonProperty app)
    {
        var o = new ObjectReader(app.Value, $"“{app.Name}” in windows.apps", null);
        o.OtherPlatform("quit", "The quit rule is for Quit on close, which is for the Mac only");
        o.Allow("focus", "swap", "arrange");
        return KeyValuePair.Create(app.Name, new AppRules(o.Bool("focus") ?? true, o.Bool("swap") ?? true, o.Bool("arrange") ?? true));
    }

    private static Entry ReadEntry(JsonElement e, int index)
    {
        var place = $"entry {index + 1}";
        string? subject = null;
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("keys", out var k) && k.ValueKind == JsonValueKind.String)
        {
            var text = k.GetString()!;
            subject = KeyPath.Parse(text)?.ToString() ?? text;
            place = $"entry “{subject}”";
        }
        var o = new ObjectReader(e, place, subject);
        o.Allow("keys", "title", "action", "icon", "hold", "list");
        var icon = o.String("icon");
        return new Entry(
            o.String("keys") ?? throw o.Missing("keys"),
            o.String("title") ?? throw o.Missing("title"),
            o.String("action"),
            string.IsNullOrEmpty(icon) ? null : icon,
            o.Bool("hold") ?? false,
            o.Object("list") is { } list ? ReadList(list, place, subject) : null);
    }

    /// A menu's `list` (spec v18, v19). Values are checked by the validator.
    private static ListSpec ReadList(JsonElement e, string place, string? subject)
    {
        var o = new ObjectReader(e, $"the list of {place}", subject);
        o.OtherPlatform("hidden", "Hidden apps are on the Mac only");
        o.Allow("type", "count", "minimized", "spaces", "scope", "choose");
        var d = new ListSpec();
        return new ListSpec(o.String("type") ?? throw o.Missing("type"), o.Int("count") ?? d.Count, o.Bool("minimized"), o.String("spaces"),
            o.String("scope") ?? d.Scope, o.String("choose") ?? d.Choose);
    }

    private static ConfigContext ReadContext(JsonElement e, int index)
    {
        string? id = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
        var o = new ObjectReader(e, id is null ? $"context {index + 1}" : $"context “{id}”", id);
        o.Allow("id", "apps");
        return new ConfigContext(o.String("id") ?? throw o.Missing("id"), o.Strings("apps") ?? throw o.Missing("apps"));
    }

    /// One action from its JSON object, as the file would have it (the Actions page's forms).
    public ConfigAction ReadAction(string json, string id)
    {
        using var document = JsonDocument.Parse(json);
        return ReadAction(document.RootElement, id);
    }

    /// One action, of any family or Context Aware. `id` is the action's id, or
    /// the id of the Context Aware action it's a variant of.
    private ConfigAction ReadAction(JsonElement e, string id)
    {
        var o = new ObjectReader(e, $"action “{id}”", id);
        var type = o.String("type") ?? throw o.Missing("type");
        var fields = new ActionFields(e, id, type, ReadAction);
        if (registry.Family(type) is { } family) return family.Read(fields);
        if (type == "contextual")
        {
            fields.Allow("variants", "fallback");
            return new ContextualAction(fields.ActionMap("variants"), fields.Action("fallback"));
        }
        var types = registry.Kinds.Select(k => k.Type).Append("contextual");
        throw new ConfigProblem(Rule.UnknownActionType, $"Action “{id}” has the unknown type “{type}”. Use {ActionFields.Join(types)}.", id);
    }

    /// The same strict checks for every object in the file.
    private readonly struct ObjectReader
    {
        private readonly JsonElement _e;
        private readonly string _place;
        private readonly string? _subject;

        public ObjectReader(JsonElement e, string place, string? subject)
        {
            _e = e;
            _place = place;
            _subject = subject;
            if (e.ValueKind != JsonValueKind.Object)
                throw new ConfigProblem(Rule.WrongType, $"{Cap(place)} should be an object in braces.", subject);
        }

        public void Allow(params string[] names)
        {
            foreach (var p in _e.EnumerateObject())
                if (!names.Contains(p.Name))
                    throw new ConfigProblem(Rule.UnknownField, $"Unknown field “{p.Name}” in {_place}. Check its spelling, or remove it.", _subject);
        }

        /// A field only the Mac reads is `other-platform-field` here (spec v16, v18).
        public void OtherPlatform(string name, string why)
        {
            if (_e.TryGetProperty(name, out _))
                throw new ConfigProblem(Rule.OtherPlatformField, $"“{name}” in {_place} isn't used on Windows: {why}. Remove it.", _subject);
        }

        public ConfigProblem Missing(string name) => new(Rule.MissingField, $"Missing “{name}” in {_place}.", _subject);

        private ConfigProblem Wrong(string name, string kind) => new(Rule.WrongType, $"“{name}” in {_place} should be {kind}.", _subject);

        private JsonElement? Get(string name) => _e.TryGetProperty(name, out var v) ? v : null;

        public string? String(string name) => Get(name) switch
        {
            null => null,
            { ValueKind: JsonValueKind.String } v => v.GetString(),
            _ => throw Wrong(name, "text in quotes"),
        };

        public int? Int(string name) => Get(name) switch
        {
            null => null,
            { ValueKind: JsonValueKind.Number } v when v.TryGetInt32(out var n) => n,
            _ => throw Wrong(name, "a whole number"),
        };

        public double? Number(string name) => Get(name) switch
        {
            null => null,
            { ValueKind: JsonValueKind.Number } v => v.GetDouble(),
            _ => throw Wrong(name, "a number"),
        };

        public bool? Bool(string name) => Get(name) switch
        {
            null => null,
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            _ => throw Wrong(name, "true or false"),
        };

        public JsonElement? Object(string name) => Get(name) switch
        {
            null => null,
            { ValueKind: JsonValueKind.Object } v => v,
            _ => throw Wrong(name, "an object in braces"),
        };

        public IEnumerable<JsonElement>? Array(string name) => Get(name) switch
        {
            null => null,
            { ValueKind: JsonValueKind.Array } v => v.EnumerateArray().ToList(),
            _ => throw Wrong(name, "a list in brackets"),
        };

        public IReadOnlyList<string>? Strings(string name)
        {
            if (Array(name) is not { } items) return null;
            var list = items.ToList();
            if (list.Any(i => i.ValueKind != JsonValueKind.String)) throw Wrong(name, "a list of text");
            return list.Select(i => i.GetString()!).ToList();
        }

        private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];
    }
}
