using System.Text.Json.Nodes;
using UnnamedManager.Core.Windows;

namespace UnnamedManager.Core.Config;

/// Fill (S-022, S-061): fill the work area, less the gap, and with `toggle`, go back.
/// Before spec v7 this was Maximize, `window.maximize`, which still loads and is
/// written back under the name the file used.
public sealed record FillAction(bool Toggle = true, string Kind = FillAction.CurrentType) : ConfigAction
{
    public const string CurrentType = "window.fill", OldType = "window.maximize";

    public override string Type => Kind;
    public override string DisplayName => "Fill";
}

public sealed class FillFamily : IActionFamily
{
    public IReadOnlyList<ActionKind> Kinds { get; } =
        [new(FillAction.CurrentType, "Fill"), new(FillAction.OldType, "Fill", FillAction.CurrentType)];

    public ConfigAction Read(ActionFields f)
    {
        f.Allow("toggle");
        return new FillAction(f.Bool("toggle", true), f.Type);
    }

    /// `toggle` is left out for the default, true.
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) =>
        ((FillAction)action).Toggle ? [] : [new("toggle", false)];

    public IReadOnlyList<JsonObject> Schema(string type)
    {
        const string toggle = "true (default): a window that already fills the usable area goes back to where it was.";
        return
        [
            ActionSchema.Variant(type, new JsonObject
            {
                ["toggle"] = new JsonObject
                {
                    ["description"] = type == FillAction.OldType ? $"The old name of {FillAction.CurrentType}. {toggle}" : toggle,
                    ["type"] = "boolean",
                },
            }),
        ];
    }

    /// `window-fill`, with Press again to restore on. The old name has none of its own.
    public ConfigAction? BuiltIn(string type, string? value) => type == FillAction.CurrentType && value is null ? new FillAction() : null;

    public IReadOnlyList<string?> BuiltInValues(string type) => type == FillAction.CurrentType ? [null] : [];

    public ActionPlan Plan(ConfigAction action, PlanContext context) =>
        new ActionPlan.Window(new WindowCommand.Fill(((FillAction)action).Toggle));
}
