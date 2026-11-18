using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Focus Window: the nearest window in a direction (S-028), the next or previous one
/// (S-029), the first or last one, one by number, or the window used before, on every
/// display or only the focused window's (spec v5 to v13). `Field` is the name the file
/// gives the target: `target`, or `direction`, its old name before spec v11, which
/// saving keeps.
public sealed record FocusAction(WindowTarget Target, string Scope = WindowScope.All, string Field = FocusAction.CurrentField) : ConfigAction
{
    public const string CurrentField = "target", OldField = "direction";

    public FocusAction(string word, string scope = WindowScope.All) : this(WindowTarget.Of(word), scope) { }

    public override string Type => "window.focus";
    public override string DisplayName => "Focus Window";
}

public sealed class FocusFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("window.focus", "Focus Window")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow(FocusAction.CurrentField, FocusAction.OldField, "scope");
        if (f.Has(FocusAction.CurrentField) && f.Has(FocusAction.OldField)) throw f.Conflict(FocusAction.OldField, FocusAction.CurrentField);
        var field = f.Has(FocusAction.OldField) ? FocusAction.OldField : FocusAction.CurrentField;
        var target = f.Target(field) ?? throw f.Problem(Rule.MissingField, $"Missing “target” in {f.Place}.");
        return new FocusAction(target, f.Choice("scope", WindowScope.Values, Rule.BadWindowValue) ?? WindowScope.All, field);
    }

    /// The scope is left out when it's the default, every display.
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action)
    {
        var focus = (FocusAction)action;
        List<KeyValuePair<string, JsonNode>> fields = [new(focus.Field, focus.Target.ToJson())];
        if (focus.Scope != WindowScope.All) fields.Add(new("scope", focus.Scope));
        return fields;
    }

    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        ActionSchema.Variant(type, new JsonObject
        {
            [FocusAction.OldField] = WindowTarget.Schema("The old name of target, from before spec v11. The same values."),
            ["scope"] = WindowScope.Schema(),
            [FocusAction.CurrentField] = WindowTarget.Schema("The window to focus: a direction, next, previous, first, last, recent (the window used before), or its number in the order, from 1."),
        }),
    ];

    /// `window-focus-left`, `window-focus-recent`, `window-focus-2` and the others (spec v11).
    public ConfigAction? BuiltIn(string type, string? value) =>
        value is not null && WindowTarget.FromBuiltIn(value) is { } target ? new FocusAction(target) : null;

    public IReadOnlyList<string?> BuiltInValues(string type) => WindowTarget.Words;

    public ActionPlan Plan(ConfigAction action, PlanContext context) =>
        new ActionPlan.Window(new WindowCommand.Focus(((FocusAction)action).Target, ((FocusAction)action).Scope));
}
