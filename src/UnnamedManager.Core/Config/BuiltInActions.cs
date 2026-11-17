namespace UnnamedManager.Core.Config;

/// Built-in actions (spec v11): actions a binding names without defining them, such as
/// `window-place-left-half`. A name is the kind's type with hyphens for its dots, then a
/// hyphen and one value of the kind's main field in lowercase words joined by hyphens.
/// Each family says which of its values have one (`IActionFamily.BuiltIn`).
public static class BuiltInActions
{
    /// One built-in action, for the Built in menu and Help.
    public sealed record Entry(string Name, ConfigAction Action);

    /// "window.place" → "window-place".
    public static string Prefix(string type) => type.Replace('.', '-');

    /// "leftTwoThirds" → "left-two-thirds". Numbers stay as they are.
    public static string Words(string value) =>
        string.Concat(value.Select(c => char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : c.ToString()));

    /// "left-two-thirds" → "leftTwoThirds", the way the file writes the value.
    public static string Value(string words)
    {
        var parts = words.Split('-');
        return parts[0] + string.Concat(parts.Skip(1).Select(p => p.Length == 0 ? "" : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    /// A number in a built-in name: ASCII digits, from 1, with no leading zero.
    public static int? Number(string text) =>
        text.Length is > 0 and < 10 && text[0] != '0' && text.All(char.IsAsciiDigit) ? int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : null;

    /// The action a built-in name stands for, or null when it isn't one. Names are
    /// case-sensitive, and the value must be in words: `window-place-leftHalf` isn't one.
    public static ConfigAction? Action(string name, ActionRegistry? registry = null)
    {
        registry ??= ActionRegistry.Standard;
        foreach (var kind in registry.Kinds.Where(k => k.ReplacedBy is null))
        {
            if (registry.Family(kind.Type) is not { } family) continue;
            var prefix = Prefix(kind.Type);
            ConfigAction? found = null;
            if (name == prefix) found = family.BuiltIn(kind.Type, null);
            else if (name.StartsWith(prefix + "-", StringComparison.Ordinal))
            {
                var rest = name[(prefix.Length + 1)..];
                var value = Value(rest);
                if (Words(value) == rest) found = family.BuiltIn(kind.Type, value);
            }
            if (found is not null) return found;
        }
        return null;
    }

    /// Every listed built-in action, kind by kind, in the registry's order.
    public static IReadOnlyList<Entry> Entries(ActionRegistry? registry = null)
    {
        registry ??= ActionRegistry.Standard;
        var entries = new List<Entry>();
        foreach (var kind in registry.Kinds.Where(k => k.ReplacedBy is null))
        {
            if (registry.Family(kind.Type) is not { } family) continue;
            foreach (var value in family.BuiltInValues(kind.Type))
                if (family.BuiltIn(kind.Type, value) is { } action)
                    entries.Add(new Entry(value is null ? Prefix(kind.Type) : $"{Prefix(kind.Type)}-{Words(value)}", action));
        }
        return entries;
    }

    /// One group of the Built in menu and Help: a kind with its values, or a kind with one action.
    public sealed record Group(string Name, IReadOnlyList<Entry> Entries);

    /// The kinds with several values first, in the Mac's order, then the single ones.
    private static readonly string[] MenuOrder =
        ["window.place", "window.focus", "window.swap", "window.arrange", "window.resize", "window.fill", "window.minimize", "help", "noop"];

    /// The Built in menu and Help's list: Place Window, Focus Window, Swap Window, Arrange
    /// Windows and Resize Window with their values, then Fill, Minimize, Show Help and Do Nothing.
    public static IReadOnlyList<Group> Groups(ActionRegistry? registry = null) =>
        [.. Entries(registry).GroupBy(e => e.Action.Type).OrderBy(g => Array.IndexOf(MenuOrder, g.Key) is var i and >= 0 ? i : int.MaxValue)
            .Select(g => new Group(g.First().Action.DisplayName, [.. g]))];

    /// Whether an action id is a built-in name, so an action in `actions` with it overrides it.
    public static bool IsBuiltIn(string id) => Action(id) is not null;

    /// The value a built-in action shows after its type, such as "Left half" in
    /// "Place Window: Left half", or null for one with no value.
    public static string? ValueLabel(ConfigAction action) => action switch
    {
        PlaceAction { Preset: { } p } => ActionForms.PresetLabel(p),
        ResizeAction { Step: "grow" } => "Grow",
        ResizeAction { Step: "shrink" } => "Shrink",
        ArrangeAction { IsAll: true } => "All on the display",
        ArrangeAction a => $"Last {a.Count}",
        FocusAction f => ActionForms.TargetLabel(f.Target),
        SwapAction s => ActionForms.TargetLabel(s.Target),
        _ => null,
    };

    /// "Place Window: Left half", "Fill", "Show Help".
    public static string Label(ConfigAction action) =>
        ValueLabel(action) is { } value ? $"{action.DisplayName}: {value}" : action.DisplayName;
}

public static class ConfigActions
{
    /// The action a binding names (spec v11): the one in `actions`, which overrides a
    /// built-in action of the same name, or else the built-in one.
    public static ConfigAction? Resolve(this Config config, string id) => config.Action(id) ?? BuiltInActions.Action(id);
}
