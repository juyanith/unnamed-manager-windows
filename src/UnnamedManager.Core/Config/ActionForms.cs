using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnnamedManager.Core.Config;

public enum FieldKind { Text, Lines, Pairs, Choice, Toggle, Number }

/// One choice in a Choice field: the value in the file and the words on screen.
public sealed record FieldChoice(string Value, string Label);

/// One field of an action's form on the Actions page (S-044). `Name` is the field in
/// the file. Lines is a list of text, one per line; Pairs is NAME=value per line.
public sealed record FormField(string Name, string Label, FieldKind Kind, string Description = "", IReadOnlyList<FieldChoice>? Choices = null, string Default = "")
{
    /// A Number field's range and step; values outside it are clamped as you type.
    public double Min { get; init; } = double.MinValue;
    public double Max { get; init; } = double.MaxValue;
    public double Step { get; init; } = 1;

    /// Shown only while the field `ShowWhen.Field` has one of these values.
    public (string Field, string[] Values)? ShowWhen { get; init; }

    /// Written even when it's the default, because the file requires it.
    public bool Required { get; init; }

    /// Changing this number turns that Choice field to "custom" (a preset's numbers).
    public string? Customizes { get; init; }

    public bool IsShown(IReadOnlyDictionary<string, string> values) =>
        ShowWhen is not { } when || (values.TryGetValue(when.Field, out var v) && when.Values.Contains(v));
}

/// The Actions page's forms: which fields each type has, an action's values as text,
/// and text back to an action through the family's own reader, so a form checks
/// exactly what the file does.
public static class ActionForms
{
    private static readonly FieldChoice[] Modes =
        [new("focusOrStart", "Focus, or start it"), new("focus", "Only focus"), new("start", "Always start a new one")];

    /// Focus Window's and Swap Window's targets (spec v6 to v13), and "number" for a place in the order.
    private static readonly FieldChoice[] Targets =
    [
        new("left", "Left"), new("right", "Right"), new("up", "Up"), new("down", "Down"),
        new("next", "Next window"), new("previous", "Previous window"), new("first", "First window"), new("last", "Last window"),
        new("recent", "Window used before"), new("number", "Window number"),
    ];

    private static readonly FieldChoice[] Sides = [new("left", "Left"), new("right", "Right")];

    private static readonly FieldChoice[] Scopes = [new("all", "On every display"), new("display", "On the same display")];

    private static readonly FieldChoice[] ArrangeCounts =
        [.. Enumerable.Range(2, 7).Select(n => new FieldChoice($"{n}", $"Last {n}")), new("all", "All on the display")];

    private static readonly FieldChoice[] ArrangeGroups =
        [new("", "Automatic"), .. Enumerable.Range(1, 8).Select(n => new FieldChoice($"{n}", $"{n}"))];

    private static readonly FieldChoice[] ArrangeLayouts = [new("auto", "Automatic"), new("columns", "Columns"), new("rows", "Rows")];

    /// What a target is called on screen: "Left", "Window used before", "Window 3".
    public static string TargetLabel(Windows.WindowTarget target) =>
        target.IsNumber ? $"Window {target.Number}" : Targets.First(t => t.Value == target.Word).Label;

    /// What a preset is called on screen: "Left half".
    public static string PresetLabel(string preset) => PlacePresets.FirstOrDefault(p => p.Value == preset)?.Label ?? preset;

    private const string FocusHelp = "Next window and Previous window go through the windows you can see, display by display, in rows as you read; " +
        "First window and Last window go to the two ends. Windows hidden behind others are skipped. Window used before goes by when you used it.";

    private const string SwapHelp = "The window you're in trades places with this one and keeps focus, so pressing again keeps moving it. " +
        "With no window there, you hear the alert sound.";

    private static IReadOnlyList<FormField> TargetFields(string label, string help) =>
    [
        new("_target", label, FieldKind.Choice, help, Targets, "right") { Required = true },
        new("_number", "Number", FieldKind.Number, Default: "1") { Min = 1, Max = 99, Step = 1, ShowWhen = ("_target", ["number"]) },
        new("scope", "Windows", FieldKind.Choice, "On the same display keeps it to the display you're on.", Scopes, "all"),
    ];

    /// The types in the Type list, alphabetical by their glossary name.
    /// An old name such as `window.maximize` isn't listed: its action shows as the type it was renamed to.
    public static IReadOnlyList<ActionKind> Types(ActionRegistry? registry = null) =>
        (registry ?? ActionRegistry.Standard).Kinds.Where(k => k.ReplacedBy is null).OrderBy(k => k.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();

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
        "window.fill" or "window.maximize" => [new("toggle", "Press again to restore", FieldKind.Toggle,
            "Fills the screen without the taskbar, less the gap from Settings. Not full screen.", Default: "true")],
        "space.switch" => [new("target", "Go to", FieldKind.Choice, "The desktop to the left or right of the one you're on, at once, with no slide.", Sides, "right") { Required = true }],
        "space.send" =>
        [
            new("_target", "Send to", FieldKind.Choice, "The desktop to the left or right of the window's own, or one by number in Task View's order.", [.. Sides, new("number", "Desktop number")], "right") { Required = true },
            new("_number", "Number", FieldKind.Number, Default: "2") { Min = 1, Max = 99, Step = 1, ShowWhen = ("_target", ["number"]) },
            new("follow", "Go with the window", FieldKind.Toggle, "Off, you stay where you are and focus goes to the window you used before here.", Default: "false"),
        ],
        "window.focus" => TargetFields("Focus on", FocusHelp),
        "window.swap" => TargetFields("Swap with", SwapHelp),
        "window.arrange" =>
        [
            new("count", "Windows", FieldKind.Choice, "The window you're in and the ones you used before it, on its display.", ArrangeCounts, "2"),
            new("groups", "Groups", FieldKind.Choice, "Columns or rows. Automatic uses as many as there are windows, up to the maximums in Settings.", ArrangeGroups, ""),
            new("layout", "Layout", FieldKind.Choice, "Automatic is columns on a wide screen and rows on a tall one. The window you're in keeps its column or row and takes the spare room there.", ArrangeLayouts, "auto"),
        ],
        "window.place" =>
        [
            new("preset", "Preset", FieldKind.Choice, Choices: [.. PlacePresets, new("custom", "Custom")], Default: "leftHalf"),
            Percent("frame.x", "X, percent of the screen") with { Customizes = "preset" },
            Percent("frame.y", "Y, percent of the screen") with { Customizes = "preset" },
            Percent("frame.width", "Width, percent of the screen") with { Customizes = "preset" },
            Percent("frame.height", "Height, percent of the screen") with { Customizes = "preset", Description = WorkArea },
        ],
        "window.move" =>
        [
            new("_how", "Move", FieldKind.Choice, Choices: [new("to", "To a spot"), new("by", "By points")], Default: "to"),
            new("spot", "Spot", FieldKind.Choice, Choices: [.. Spots.Select(s => new FieldChoice(s.Value, s.Label)), new("custom", "Custom")], Default: "center") { ShowWhen = ("_how", ["to"]) },
            Percent("to.x", "Across, percent of the free space") with { Customizes = "spot", ShowWhen = ("_how", ["to"]), Description = "0 is the left edge, 50 the middle, 100 the right edge." },
            Percent("to.y", "Down, percent of the free space") with { Customizes = "spot", ShowWhen = ("_how", ["to"]), Description = "0 is the top, 50 the middle, 100 the bottom." },
            Points("by.dx", "Right, in points", "by"),
            Points("by.dy", "Down, in points", "by"),
        ],
        "window.resize" =>
        [
            new("_how", "Resize", FieldKind.Choice, Choices: [new("grow", "Grow through widths"), new("shrink", "Shrink through widths"), new("to", "To a size"), new("by", "By points")], Default: "grow"),
            new("widths", "Widths, percent of the screen", FieldKind.Lines, "One per line. Empty is 25, 33, 50, 67 and 75.") { ShowWhen = ("_how", ["grow", "shrink"]) },
            Percent("to.width", "Width, percent of the screen") with { ShowWhen = ("_how", ["to"]) },
            Percent("to.height", "Height, percent of the screen") with { ShowWhen = ("_how", ["to"]), Description = WorkArea },
            Points("by.dw", "Wider, in points", "by"),
            Points("by.dh", "Taller, in points", "by"),
            new("anchor", "Keep in place", FieldKind.Choice, Choices: Anchors, Default: "topLeft") { ShowWhen = ("_how", ["to", "by"]) },
        ],
        _ => null,
    };

    private static readonly FieldChoice[] PlacePresets =
    [
        new("leftHalf", "Left half"), new("rightHalf", "Right half"), new("topHalf", "Top half"), new("bottomHalf", "Bottom half"),
        new("leftThird", "Left third"), new("centerThird", "Center third"), new("rightThird", "Right third"),
        new("leftTwoThirds", "Left two thirds"), new("rightTwoThirds", "Right two thirds"),
        new("topLeft", "Top left quarter"), new("topRight", "Top right quarter"), new("bottomLeft", "Bottom left quarter"), new("bottomRight", "Bottom right quarter"),
        new("center", "Center"),
    ];

    /// Move Window's named spots (S-089): where in the free space, across and down.
    private static readonly (string Value, string Label, double X, double Y)[] Spots =
    [
        ("topLeft", "Top left", 0, 0), ("top", "Top", 0.5, 0), ("topRight", "Top right", 1, 0),
        ("left", "Left", 0, 0.5), ("center", "Center", 0.5, 0.5), ("right", "Right", 1, 0.5),
        ("bottomLeft", "Bottom left", 0, 1), ("bottom", "Bottom", 0.5, 1), ("bottomRight", "Bottom right", 1, 1),
    ];

    private static string SpotAt(double x, double y) =>
        Spots.Where(s => Math.Abs(s.X - x) < 1e-9 && Math.Abs(s.Y - y) < 1e-9).Select(s => s.Value).FirstOrDefault() ?? "custom";

    private static readonly FieldChoice[] Anchors =
    [
        new("topLeft", "Top left corner"), new("top", "Top edge"), new("topRight", "Top right corner"), new("left", "Left edge"), new("center", "Center"),
        new("right", "Right edge"), new("bottomLeft", "Bottom left corner"), new("bottom", "Bottom edge"), new("bottomRight", "Bottom right corner"),
    ];

    private static FormField Percent(string name, string label) =>
        new(name, label, FieldKind.Number) { Min = 0, Max = 100, Step = 5 };

    private const string WorkArea = "The screen here is its work area: the screen without the taskbar.";

    private static FormField Points(string name, string label, string how) =>
        new(name, label, FieldKind.Number, Default: "0") { Min = -10000, Max = 10000, Step = 10, ShowWhen = ("_how", [how]) };

    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    private static string N(double value) => Math.Round(value, 2).ToString(Invariant);

    private static double D(IReadOnlyDictionary<string, string> values, string name) =>
        double.TryParse(values.GetValueOrDefault(name), System.Globalization.NumberStyles.Float, Invariant, out var d) ? d : 0;

    /// The window forms' values: percents for fractions, and the way chosen in `_how`.
    private static Dictionary<string, string>? WindowValues(ConfigAction action) => action switch
    {
        PlaceAction p => Frame(p.Frame ?? PlaceAction.PresetFrame(p.Preset!), p.Preset ?? "custom"),
        MoveAction { IsTo: true } m => new() { ["_how"] = "to", ["spot"] = SpotAt(m.ToX!.Value, m.ToY!.Value), ["to.x"] = N(m.ToX!.Value * 100), ["to.y"] = N(m.ToY!.Value * 100), ["by.dx"] = "0", ["by.dy"] = "0" },
        MoveAction m => new() { ["_how"] = "by", ["by.dx"] = N(m.Dx), ["by.dy"] = N(m.Dy), ["spot"] = "center", ["to.x"] = "50", ["to.y"] = "50" },
        FocusAction f => Target(f.Target, f.Scope),
        SwapAction s => Target(s.Target, s.Scope),
        SpaceSendAction s => new() { ["_target"] = s.Target.Side ?? "number", ["_number"] = N(s.Target.IsNumber ? s.Target.Number : 2), ["follow"] = s.Follow ? "true" : "false" },
        ArrangeAction a => new() { ["count"] = a.Count?.ToString(Invariant) ?? "all", ["groups"] = a.Groups?.ToString(Invariant) ?? "", ["layout"] = a.Layout },
        ResizeAction r => new()
        {
            ["_how"] = r.Change == "step" ? r.Step! : r.Change,
            ["widths"] = string.Join("\n", (r.Widths ?? []).Select(w => N(w * 100))),
            ["to.width"] = N(r.Change == "to" ? r.A * 100 : 50), ["to.height"] = N(r.Change == "to" ? r.B * 100 : 100),
            ["by.dw"] = N(r.Change == "by" ? r.A : 0), ["by.dh"] = N(r.Change == "by" ? r.B : 0),
            ["anchor"] = r.Anchor,
        },
        _ => null,
    };

    private static Dictionary<string, string> Target(Windows.WindowTarget target, string scope) => new()
    {
        ["_target"] = target.Word ?? "number", ["_number"] = target.IsNumber ? N(target.Number) : "1", ["scope"] = scope,
    };

    private static Dictionary<string, string> Frame(FractionFrame f, string preset) => new()
    {
        ["preset"] = preset, ["frame.x"] = N(f.X * 100), ["frame.y"] = N(f.Y * 100), ["frame.width"] = N(f.Width * 100), ["frame.height"] = N(f.Height * 100),
    };

    /// The values a form starts from when its type is picked.
    public static Dictionary<string, string> Defaults(string type) => type switch
    {
        "window.place" => Frame(PlaceAction.PresetFrame("leftHalf"), "leftHalf"),
        "window.move" => WindowValues(new MoveAction(0.5, 0.5, 0, 0))!,
        "window.resize" => WindowValues(new ResizeAction("step", 0, 0, "grow", null, "topLeft"))!,
        "window.focus" or "window.swap" => Target(Windows.WindowTarget.Of("right"), "all"),
        "window.arrange" => WindowValues(new ArrangeAction())!,
        "space.send" => WindowValues(new SpaceSendAction(SpaceTarget.Of("right")))!,
        _ => (Fields(type) ?? []).ToDictionary(f => f.Name, f => f.Default),
    };

    /// A preset's numbers, for when the Preset list changes.
    public static Dictionary<string, string> PresetValues(string preset) => Frame(PlaceAction.PresetFrame(preset), preset);

    /// The numbers a named choice fills: a preset's, or a spot's (S-089). None for Custom
    /// or another field.
    public static IReadOnlyDictionary<string, string> Filled(string field, string choice) => field switch
    {
        "preset" when choice != "custom" => PresetValues(choice),
        "spot" when Spots.FirstOrDefault(s => s.Value == choice) is { Value: not null } spot =>
            new Dictionary<string, string> { ["to.x"] = N(spot.X * 100), ["to.y"] = N(spot.Y * 100) },
        _ => new Dictionary<string, string>(),
    };

    private static JsonObject? WindowJson(string type, IReadOnlyDictionary<string, string> v)
    {
        JsonObject Numbers(params (string Name, double Value)[] pairs) => new(pairs.Select(p => KeyValuePair.Create(p.Name, (JsonNode?)p.Value)));
        var how = v.GetValueOrDefault("_how");
        switch (type)
        {
            case "window.place":
                return v.GetValueOrDefault("preset") is { } preset and not "custom"
                    ? new JsonObject { ["type"] = type, ["preset"] = preset }
                    : new JsonObject { ["type"] = type, ["frame"] = Numbers(("x", D(v, "frame.x") / 100), ("y", D(v, "frame.y") / 100), ("width", D(v, "frame.width") / 100), ("height", D(v, "frame.height") / 100)) };
            case "window.move":
                return how == "by"
                    ? new JsonObject { ["type"] = type, ["by"] = Numbers(("dx", D(v, "by.dx")), ("dy", D(v, "by.dy"))) }
                    : new JsonObject { ["type"] = type, ["to"] = Numbers(("x", D(v, "to.x") / 100), ("y", D(v, "to.y") / 100)) };
            case "window.focus" or "window.swap":
                var json0 = new JsonObject { ["type"] = type };
                json0["target"] = v.GetValueOrDefault("_target") is "number" ? (JsonNode)(int)Math.Max(1, Math.Round(D(v, "_number"))) : v.GetValueOrDefault("_target") ?? "right";
                if (v.GetValueOrDefault("scope") is "display") json0["scope"] = "display";
                return json0;
            case "space.send":
                var send = new JsonObject { ["type"] = type };
                send["target"] = v.GetValueOrDefault("_target") is "number" ? (JsonNode)(int)Math.Max(1, Math.Round(D(v, "_number"))) : v.GetValueOrDefault("_target") ?? "right";
                if (v.GetValueOrDefault("follow") == "true") send["follow"] = true;
                return send;
            case "window.arrange":
                var arrange = new JsonObject { ["type"] = type };
                if (v.GetValueOrDefault("count") is { } count and not "2")
                    arrange["count"] = count == "all" ? "all" : (JsonNode)int.Parse(count, Invariant);
                if (int.TryParse(v.GetValueOrDefault("groups"), Invariant, out var groups)) arrange["groups"] = groups;
                if (v.GetValueOrDefault("layout") is { } layout and not "auto") arrange["layout"] = layout;
                return arrange;
            case "window.resize":
                var json = new JsonObject { ["type"] = type };
                if (how is "grow" or "shrink")
                {
                    json["step"] = how;
                    var widths = Lines(v.GetValueOrDefault("widths") ?? "")
                        .Select(l => double.TryParse(l.Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float, Invariant, out var d) ? d : -1).ToList();
                    if (widths.Count > 0) json["widths"] = new JsonArray([.. widths.Select(w => (JsonNode)(w / 100))]);
                    return json;
                }
                json[how == "by" ? "by" : "to"] = how == "by"
                    ? Numbers(("dw", D(v, "by.dw")), ("dh", D(v, "by.dh")))
                    : Numbers(("width", D(v, "to.width") / 100), ("height", D(v, "to.height") / 100));
                if (v.GetValueOrDefault("anchor") is { } anchor and not "topLeft") json["anchor"] = anchor;
                return json;
            default:
                return null;
        }
    }

    /// An action's fields as text, by field name. Defaults fill what the file leaves out.
    public static Dictionary<string, string> Values(ConfigAction action, ActionRegistry? registry = null)
    {
        if (WindowValues(action) is { } window) return window;
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

    /// The fields that can't show the action's value as text that reads back the same:
    /// an argument that's blank or has a line break, a variable's name with `=` or spaces
    /// around it, a line break in a one-line field. The form shows them read-only.
    public static IReadOnlySet<string> Unshowable(ConfigAction action)
    {
        static bool Breaks(string text) => text.AsSpan().IndexOfAny('\r', '\n') >= 0;
        var values = Values(action);
        var names = (Fields(FormType(action)) ?? [])
            .Where(f => f.Kind == FieldKind.Text && values.TryGetValue(f.Name, out var text) && Breaks(text))
            .Select(f => f.Name).ToHashSet();
        if (action is RunCommandAction run)
        {
            if (run.Command.Any(part => string.IsNullOrWhiteSpace(part) || Breaks(part))) names.Add("command");
            if (run.Env.Any(e => e.Key.Contains('=') || e.Key != e.Key.Trim() || Breaks(e.Key) || Breaks(e.Value))) names.Add("env");
        }
        return names;
    }

    /// Builds an action from a form. Empty fields are left out. The form rounds numbers
    /// and can't show every value, so with `before`, the action the form was filled from,
    /// whatever the form didn't change stays exactly as it was there. Throws the reader's
    /// ConfigProblem, with its message, when the form doesn't make a valid action.
    public static ConfigAction Build(string type, IReadOnlyDictionary<string, string> values, string id, ConfigAction? before = null, ActionRegistry? registry = null)
    {
        registry ??= ActionRegistry.Standard;
        CheckNames(type, values, id, before is null ? null : Values(before, registry));
        var json = FormJson(type, values);
        if (before is not null && FormType(before, registry) == type && registry.Family(before.Type) is { } family)
        {
            // A preset made custom starts from the preset's exact fractions.
            if (before is PlaceAction { Preset: { } preset } && json.ContainsKey("frame"))
                before = new PlaceAction(null, PlaceAction.PresetFrame(preset));
            var shown = FormJson(type, Values(before, registry));
            var written = new JsonObject { ["type"] = before.Type };
            foreach (var (name, node) in family.Write(before)) written[name] = node;
            json = (JsonObject)KeepUnchanged(json, shown, written);
        }
        return new ConfigReader(registry).ReadAction(json.ToJsonString(), id);
    }

    /// `edited` with each part that still reads as `shown` put back as `written`: the
    /// whole of it, a field of an object, or an item of a list wherever it moved to.
    private static JsonNode KeepUnchanged(JsonNode edited, JsonNode shown, JsonNode written)
    {
        if (JsonNode.DeepEquals(edited, shown)) return written.DeepClone();
        switch (edited, shown, written)
        {
            case (JsonObject e, JsonObject s, JsonObject w):
                var fields = new JsonObject();
                foreach (var (name, node) in e)
                    fields[name] = node is not null && s[name] is { } was && w[name] is { } exact ? KeepUnchanged(node, was, exact) : node?.DeepClone();
                return fields;
            case (JsonArray e, JsonArray s, JsonArray w) when s.Count == w.Count:
                var left = Enumerable.Range(0, s.Count).ToList();
                var items = new JsonArray();
                foreach (var node in e)
                {
                    var at = left.FindIndex(i => JsonNode.DeepEquals(node, s[i]));
                    items.Add((at < 0 ? node : w[left[at]])?.DeepClone());
                    if (at >= 0) left.RemoveAt(at);
                }
                return items;
            default:
                return edited.DeepClone();
        }
    }

    /// A name that's in an edited NAME=value field twice is a problem to fix. One that
    /// wasn't edited can look that way, when two values share a line, and stays as it is.
    private static void CheckNames(string type, IReadOnlyDictionary<string, string> values, string id, IReadOnlyDictionary<string, string>? shown)
    {
        foreach (var field in (Fields(type) ?? []).Where(f => f.Kind == FieldKind.Pairs))
        {
            if (!values.TryGetValue(field.Name, out var text)) continue;
            if (shown?.GetValueOrDefault(field.Name) is { } was && Lines(was).SequenceEqual(Lines(text))) continue;
            if (Lines(text).GroupBy(line => line.Split('=', 2)[0].Trim()).FirstOrDefault(g => g.Count() > 1) is { } twice)
                throw new ConfigProblem(Rule.WrongType, $"“{twice.Key}” is in {field.Label} twice. Keep one line for it.", id);
        }
    }

    /// NAME=value lines as an object.
    private static JsonObject Pairs(string text)
    {
        var pairs = new JsonObject();
        foreach (var parts in Lines(text).Select(line => line.Split('=', 2)))
            pairs[parts[0].Trim()] = parts.Length > 1 ? parts[1] : "";
        return pairs;
    }

    /// A form's values as the file would have them.
    private static JsonObject FormJson(string type, IReadOnlyDictionary<string, string> values)
    {
        if (WindowJson(type, values) is { } window) return window;
        var json = new JsonObject { ["type"] = type };
        foreach (var field in Fields(type) ?? [])
        {
            if (!values.TryGetValue(field.Name, out var text) || string.IsNullOrWhiteSpace(text)) continue;
            JsonNode? node = field.Kind switch
            {
                FieldKind.Lines => new JsonArray([.. Lines(text).Select(l => (JsonNode)l)]),
                FieldKind.Pairs => Pairs(text),
                FieldKind.Toggle => text == "true",
                _ => text.Trim(),
            };
            if (field.Kind is FieldKind.Choice or FieldKind.Toggle && text == field.Default && !field.Required) continue;
            json[field.Name] = node;
        }
        return json;
    }

    /// The type the Type list shows for an action: `window.maximize` shows as Fill.
    public static string FormType(ConfigAction action, ActionRegistry? registry = null) =>
        (registry ?? ActionRegistry.Standard).Kinds.FirstOrDefault(k => k.Type == action.Type)?.ReplacedBy ?? action.Type;

    /// A form's action with the names the file used before kept (spec v7, v11): Fill
    /// stays `window.maximize`, and Focus Window's target stays `direction`.
    public static ConfigAction KeepNames(ConfigAction built, ConfigAction before) => (built, before) switch
    {
        (FillAction fill, FillAction old) => fill with { Kind = old.Kind },
        (FocusAction focus, FocusAction old) => focus with { Field = old.Field },
        _ => built,
    };

    private static IEnumerable<string> Lines(string text) =>
        // A multi-line TextBox keeps its line breaks as \r.
        text.Split(['\r', '\n']).Where(l => l.Trim().Length > 0);
}
