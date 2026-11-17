using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Swap Window (spec v10): the focused window trades place and size with another and
/// keeps focus.
public sealed record SwapAction(WindowTarget Target, string Scope = WindowScope.All) : ConfigAction
{
    public SwapAction(string word, string scope = WindowScope.All) : this(WindowTarget.Of(word), scope) { }

    public override string Type => "window.swap";
    public override string DisplayName => "Swap Window";
}

public sealed class SwapFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("window.swap", "Swap Window")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("target", "scope");
        var target = f.Target("target") ?? throw f.Problem(Rule.MissingField, $"Missing “target” in {f.Place}.");
        return new SwapAction(target, f.Choice("scope", WindowScope.Values, Rule.BadWindowValue) ?? WindowScope.All);
    }

    /// The scope is left out when it's the default, every display.
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action)
    {
        var swap = (SwapAction)action;
        List<KeyValuePair<string, JsonNode>> fields = [new("target", swap.Target.ToJson())];
        if (swap.Scope != WindowScope.All) fields.Add(new("scope", swap.Scope));
        return fields;
    }

    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        ActionSchema.Variant(type, new JsonObject
        {
            ["scope"] = WindowScope.Schema(),
            ["target"] = WindowTarget.Schema("The window to swap with: a direction, next, previous, first, last, recent (the window used before), or its number in Focus Window's order, from 1."),
        }, "target"),
    ];

    /// `window-swap-left`, `window-swap-recent`, `window-swap-3` and the others (spec v11).
    public ConfigAction? BuiltIn(string type, string? value) =>
        value is not null && WindowTarget.FromBuiltIn(value) is { } target ? new SwapAction(target) : null;

    public IReadOnlyList<string?> BuiltInValues(string type) => WindowTarget.Words;

    public ActionPlan Plan(ConfigAction action, PlanContext context) =>
        new ActionPlan.Window(new WindowCommand.Swap(((SwapAction)action).Target, ((SwapAction)action).Scope));
}
