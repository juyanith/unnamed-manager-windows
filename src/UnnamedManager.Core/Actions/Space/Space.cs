using System.Text.Json;
using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Where Switch Desktop goes and Send and Follow to Desktop put the window (spec v17, v20, v22, v23): the
/// desktop to the left or right, or one by number from 1. Exactly one of `Side` and
/// `Number` is set.
public readonly record struct SpaceTarget(string? Side, int Number)
{
    public static readonly string[] Sides = ["left", "right"];

    public static SpaceTarget Of(string side) => new(side, 0);
    public static SpaceTarget Of(int number) => new(null, number);

    public bool IsNumber => Side is null;

    /// As the file writes it: a word in quotes, or a number.
    public JsonNode ToJson() => Side is { } s ? JsonValue.Create(s) : JsonValue.Create(Number);

    public override string ToString() => Side ?? Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// "Left", "Right", "Desktop 2".
    public string Label => Side switch
    {
        "left" => "Left",
        "right" => "Right",
        _ => $"Desktop {Number}",
    };

    /// The target in a built-in name: "left", "right", or a number with no leading zero.
    public static SpaceTarget? FromBuiltIn(string value, bool numbers)
    {
        if (numbers && BuiltInActions.Number(value) is { } number) return Of(number);
        return Sides.Contains(value) ? Of(value) : null;
    }

    /// The schema's `oneOf` for a target: left or right, or an integer from 1.
    public static JsonArray SchemaOneOf() => new(
        new JsonObject { ["enum"] = new JsonArray("left", "right") },
        new JsonObject { ["minimum"] = 1, ["type"] = "integer" });

    /// Reads `target`: left or right, and with `numbers` a whole number from 1. Any other
    /// value, the string "2" included, is `bad-window-value`; none is `missing-field`.
    public static SpaceTarget Read(ActionFields f, bool numbers)
    {
        var use = numbers ? "left, right, or a number from 1" : "left or right";
        switch (f.Raw("target"))
        {
            case null:
                throw f.Problem(Rule.MissingField, $"Missing “target” in {f.Place}.");
            case { ValueKind: JsonValueKind.String } v when Sides.Contains(v.GetString()):
                return Of(v.GetString()!);
            case { ValueKind: JsonValueKind.Number } v when numbers && v.TryGetInt32(out var n) && n >= 1:
                return Of(n);
            case { ValueKind: JsonValueKind.String or JsonValueKind.Number }:
                throw f.Problem(Rule.BadWindowValue, $"“target” in {f.Place} must be {use}.");
            default:
                throw f.Problem(Rule.WrongType, $"“target” in {f.Place} should be {use}.");
        }
    }
}

/// Switch Desktop (spec v17, v22): goes to the virtual desktop to the left or right, or to
/// one by number.
public sealed record SpaceSwitchAction(SpaceTarget Target) : ConfigAction
{
    public override string Type => "space.switch";
    public override string DisplayName => "Switch Desktop";
}

/// Send to Desktop (spec v20) puts the focused window on another virtual desktop, and you
/// stay. Follow to Desktop (spec v23, `space.follow`) does the same and goes there with it.
/// Both read and write only `target`, so one record holds them, with `Follow` for the type.
public sealed record SpaceSendAction(SpaceTarget Target, bool Follow = false) : ConfigAction
{
    public override string Type => Follow ? "space.follow" : "space.send";
    public override string DisplayName => Follow ? "Follow to Desktop" : "Send to Desktop";
}

public sealed class SpaceSwitchFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("space.switch", "Switch Desktop")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("target");
        return new SpaceSwitchAction(SpaceTarget.Read(f, numbers: true));
    }

    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) => [new("target", ((SpaceSwitchAction)action).Target.ToJson())];

    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        ActionSchema.Variant(type, new JsonObject
        {
            ["target"] = new JsonObject
            {
                ["description"] = "The Space or desktop to go to: the one to the left or to the right of the current one, or the one with that number, from 1.",
                ["oneOf"] = SpaceTarget.SchemaOneOf(),
            },
        }, "target"),
    ];

    /// `space-switch-left`, `space-switch-right` and `space-switch-1` and the other numbers (spec v22).
    public ConfigAction? BuiltIn(string type, string? value) =>
        value is not null && SpaceTarget.FromBuiltIn(value, numbers: true) is { } target ? new SpaceSwitchAction(target) : null;

    public IReadOnlyList<string?> BuiltInValues(string type) => SpaceTarget.Sides;

    public ActionPlan Plan(ConfigAction action, PlanContext context) =>
        new ActionPlan.Window(new WindowCommand.SwitchDesktop(((SpaceSwitchAction)action).Target));
}

/// Send to Desktop (`space.send`, spec v20) and Follow to Desktop (`space.follow`, spec v23).
public sealed class SpaceSendFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("space.send", "Send to Desktop"), new("space.follow", "Follow to Desktop")];

    public ConfigAction Read(ActionFields f)
    {
        // Spec v23 dropped Send to Desktop's `follow`: still `unknown-field`, with the way out.
        if (f.Type == "space.send" && f.Raw("follow") is not null)
            throw f.Problem(Rule.UnknownField, $"“follow” is gone from Send to Desktop in {f.Place}. To go with the window, use Follow to Desktop (“space.follow”); to stay, remove it.");
        f.Allow("target");
        return new SpaceSendAction(SpaceTarget.Read(f, numbers: true), f.Type == "space.follow");
    }

    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) => [new("target", ((SpaceSendAction)action).Target.ToJson())];

    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        ActionSchema.Variant(type, new JsonObject
        {
            ["target"] = new JsonObject
            {
                ["description"] = type == "space.follow"
                    ? "Where the window goes, and you with it: the Space or desktop to the left or right of its own, or the one with that number, from 1."
                    : "Where the window goes: the Space or desktop to the left or right of its own, or the one with that number, from 1.",
                ["oneOf"] = SpaceTarget.SchemaOneOf(),
            },
        }, "target"),
    ];

    /// `space-send-left`, `space-send-right`, `space-send-2` and the other numbers, and the
    /// same for `space-follow-` (spec v23).
    public ConfigAction? BuiltIn(string type, string? value) =>
        value is not null && SpaceTarget.FromBuiltIn(value, numbers: true) is { } target ? new SpaceSendAction(target, type == "space.follow") : null;

    public IReadOnlyList<string?> BuiltInValues(string type) => SpaceTarget.Sides;

    public ActionPlan Plan(ConfigAction action, PlanContext context)
    {
        var send = (SpaceSendAction)action;
        return new ActionPlan.Window(new WindowCommand.SendToDesktop(send.Target, send.Follow));
    }
}
