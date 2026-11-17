using System.Text.Json;
using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Arrange Windows (spec v8, v9): the windows used last, on the focused window's
/// display, in a grid of columns or rows. `Count` is 2 to 8, or null for "all";
/// `Groups` null lets the app pick, up to the settings' limit.
public sealed record ArrangeAction(int? Count = 2, int? Groups = null, string Layout = "auto") : ConfigAction
{
    public const int MinCount = 2, MaxCount = 8, MaxGroups = 8;
    public static readonly string[] Layouts = ["auto", "columns", "rows"];

    public bool IsAll => Count is null;

    public override string Type => "window.arrange";
    public override string DisplayName => "Arrange Windows";
}

public sealed class ArrangeFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("window.arrange", "Arrange Windows")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("count", "groups", "layout");
        int? count = f.Raw("count") switch
        {
            null => 2,
            { ValueKind: JsonValueKind.String } v when v.GetString() == "all" => null,
            { ValueKind: JsonValueKind.Number } v when v.TryGetInt32(out var n) && n is >= ArrangeAction.MinCount and <= ArrangeAction.MaxCount => n,
            { ValueKind: JsonValueKind.Number } v when v.TryGetInt32(out _) => throw CountProblem(f),
            { ValueKind: JsonValueKind.String } => throw CountProblem(f),
            _ => throw f.Problem(Rule.WrongType, $"“count” in {f.Place} should be a whole number or \"all\"."),
        };
        var groups = f.Integer("groups");
        if (groups is { } g)
        {
            if (g is < 1 or > ArrangeAction.MaxGroups)
                throw f.Problem(Rule.BadWindowValue, $"“groups” in {f.Place} must be from 1 to {ArrangeAction.MaxGroups}.");
            if (count is { } c && g > c)
                throw f.Problem(Rule.BadWindowValue, $"“groups” in {f.Place} can't be more than “count”, {c}.");
        }
        return new ArrangeAction(count, groups, f.Choice("layout", ArrangeAction.Layouts, Rule.BadWindowValue) ?? "auto");
    }

    private static ConfigProblem CountProblem(ActionFields f) =>
        f.Problem(Rule.BadWindowValue, $"“count” in {f.Place} must be from {ArrangeAction.MinCount} to {ArrangeAction.MaxCount}, or \"all\".");

    /// Defaults are left out: a count of 2, no groups, the auto layout.
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action)
    {
        var a = (ArrangeAction)action;
        var fields = new List<KeyValuePair<string, JsonNode>>();
        if (a.Count is null) fields.Add(new("count", "all"));
        else if (a.Count != 2) fields.Add(new("count", a.Count.Value));
        if (a.Groups is { } groups) fields.Add(new("groups", groups));
        if (a.Layout != "auto") fields.Add(new("layout", a.Layout));
        return fields;
    }

    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        ActionSchema.Variant(type, new JsonObject
        {
            ["count"] = new JsonObject
            {
                ["description"] = "How many windows: the focused one and the ones used before it. \"all\" is every window on the display, up to windows.maxColumns times windows.maxRows. Default 2.",
                ["oneOf"] = new JsonArray(
                    new JsonObject { ["maximum"] = ArrangeAction.MaxCount, ["minimum"] = ArrangeAction.MinCount, ["type"] = "integer" },
                    new JsonObject { ["enum"] = new JsonArray("all") }),
            },
            ["groups"] = new JsonObject
            {
                ["description"] = "How many columns or rows. Without it, as many as there are windows, up to windows.maxColumns or windows.maxRows.",
                ["maximum"] = ArrangeAction.MaxGroups,
                ["minimum"] = 1,
                ["type"] = "integer",
            },
            ["layout"] = new JsonObject
            {
                ["description"] = "auto (default) is columns on a wide screen and rows on a tall one.",
                ["enum"] = new JsonArray([.. ArrangeAction.Layouts.Select(l => (JsonNode)l)]),
            },
        }),
    ];

    /// `window-arrange`, `window-arrange-3` to `window-arrange-8`, and `window-arrange-all` (spec v11).
    public ConfigAction? BuiltIn(string type, string? value) => value switch
    {
        null => new ArrangeAction(),
        "all" => new ArrangeAction(null),
        _ => BuiltInActions.Number(value) is { } n and >= ArrangeAction.MinCount and <= ArrangeAction.MaxCount ? new ArrangeAction(n) : null,
    };

    /// `window-arrange-2` is the same as `window-arrange`, so it isn't listed.
    public IReadOnlyList<string?> BuiltInValues(string type) =>
        [null, .. Enumerable.Range(3, ArrangeAction.MaxCount - 2).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)), "all"];

    public ActionPlan Plan(ConfigAction action, PlanContext context) => new ActionPlan.Window(new WindowCommand.Arrange((ArrangeAction)action));
}
