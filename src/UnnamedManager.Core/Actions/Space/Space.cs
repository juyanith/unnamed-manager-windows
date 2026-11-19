using System.Text.Json;
using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Where Switch Desktop goes and Send to Desktop puts the window (spec v17, v20): the
/// desktop to the left or right, or, for Send to Desktop, one by number from 1. Exactly
/// one of `Side` and `Number` is set.
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

/// Switch Desktop (spec v17): goes to the virtual desktop to the left or right.
public sealed record SpaceSwitchAction(string Target) : ConfigAction
{
    public override string Type => "space.switch";
    public override string DisplayName => "Switch Desktop";
}

/// Send to Desktop (spec v20): puts the focused window on another virtual desktop, and
/// with `Follow` goes there with it.
public sealed record SpaceSendAction(SpaceTarget Target, bool Follow = false) : ConfigAction
{
    public override string Type => "space.send";
    public override string DisplayName => "Send to Desktop";
}

public sealed class SpaceSwitchFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("space.switch", "Switch Desktop")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("target");
        return new SpaceSwitchAction(SpaceTarget.Read(f, numbers: false).Side!);
    }

    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) => [new("target", ((SpaceSwitchAction)action).Target)];

    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        ActionSchema.Variant(type, new JsonObject
        {
            ["target"] = new JsonObject
            {
                ["description"] = "The Space or desktop to go to: the one to the left or to the right of the current one.",
                ["enum"] = new JsonArray("left", "right"),
            },
        }, "target"),
    ];

    /// `space-switch-left` and `space-switch-right`.
    public ConfigAction? BuiltIn(string type, string? value) =>
        value is not null && SpaceTarget.FromBuiltIn(value, numbers: false) is { Side: { } side } ? new SpaceSwitchAction(side) : null;

    public IReadOnlyList<string?> BuiltInValues(string type) => SpaceTarget.Sides;

    public ActionPlan Plan(ConfigAction action, PlanContext context) =>
        new ActionPlan.Window(new WindowCommand.SwitchDesktop(((SpaceSwitchAction)action).Target));
}

public sealed class SpaceSendFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } = [new("space.send", "Send to Desktop")];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("target", "follow");
        return new SpaceSendAction(SpaceTarget.Read(f, numbers: true), f.Bool("follow", false));
    }

    /// `follow` is left out when it's off, the default.
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action)
    {
        var send = (SpaceSendAction)action;
        List<KeyValuePair<string, JsonNode>> fields = [new("target", send.Target.ToJson())];
        if (send.Follow) fields.Add(new("follow", true));
        return fields;
    }

    public IReadOnlyList<JsonObject> Schema(string type) =>
    [
        ActionSchema.Variant(type, new JsonObject
        {
            ["follow"] = new JsonObject
            {
                ["description"] = "Go to that Space or desktop with the window. Default false: stay where you are.",
                ["type"] = "boolean",
            },
            ["target"] = new JsonObject
            {
                ["description"] = "Where the window goes: the Space or desktop to the left or right of its own, or the one with that number, from 1.",
                ["oneOf"] = new JsonArray(
                    new JsonObject { ["enum"] = new JsonArray("left", "right") },
                    new JsonObject { ["minimum"] = 1, ["type"] = "integer" }),
            },
        }, "target"),
    ];

    /// `space-send-left`, `space-send-right` and `space-send-2` and the other numbers,
    /// all with `follow` off.
    public ConfigAction? BuiltIn(string type, string? value) =>
        value is not null && SpaceTarget.FromBuiltIn(value, numbers: true) is { } target ? new SpaceSendAction(target) : null;

    public IReadOnlyList<string?> BuiltInValues(string type) => SpaceTarget.Sides;

    public ActionPlan Plan(ConfigAction action, PlanContext context)
    {
        var send = (SpaceSendAction)action;
        return new ActionPlan.Window(new WindowCommand.SendToDesktop(send.Target, send.Follow));
    }
}
