using System.Text.Json.Nodes;

namespace UnnamedManager.Core.Windows;

/// Which window Focus Window goes to and Swap Window trades places with (spec v10,
/// v11, v13): a word (a direction, next, previous, first, last, recent) or a place in
/// the order, from 1. Exactly one of `Word` and `Number` is set.
public readonly record struct WindowTarget(string? Word, int Number)
{
    public static readonly string[] Words = ["left", "right", "up", "down", "next", "previous", "first", "last", "recent"];
    public static readonly string[] Directions = ["left", "right", "up", "down"];

    public static WindowTarget Of(string word) => new(word, 0);
    public static WindowTarget Of(int number) => new(null, number);

    public bool IsNumber => Word is null;
    public bool IsDirection => Word is { } w && Directions.Contains(w);

    /// The target in a built-in name (spec v11): "left", "recent", or a number with no leading zero.
    public static WindowTarget? FromBuiltIn(string value)
    {
        if (Config.BuiltInActions.Number(value) is { } number) return Of(number);
        return Words.Contains(value) ? Of(value) : null;
    }

    /// As the file writes it: a word in quotes, or a number.
    public JsonNode ToJson() => Word is { } w ? JsonValue.Create(w) : JsonValue.Create(Number);

    public override string ToString() => Word ?? Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// How Focus Window says there's no window there.
    public string Failure => Word switch
    {
        "left" => "No window to the left.",
        "right" => "No window to the right.",
        "up" => "No window above.",
        "down" => "No window below.",
        "next" or "previous" => "No other window to focus.",
        "recent" => "No window used before.",
        _ => "No window to focus.",
    };

    /// What a number past the last window says.
    public static string PastTheEnd(int number, int count) => $"No window {number}. There {(count == 1 ? "is" : "are")} {count}.";

    /// The schema for a target, in the spec's shape.
    public static JsonObject Schema(string description) => new()
    {
        ["description"] = description,
        ["oneOf"] = new JsonArray(
            new JsonObject { ["enum"] = new JsonArray([.. Words.Select(w => (JsonNode)w)]) },
            new JsonObject { ["minimum"] = 1, ["type"] = "integer" }),
    };
}

/// Which windows Focus Window and Swap Window look at (spec v10): every display, or
/// only the focused window's.
public static class WindowScope
{
    public const string All = "all", Display = "display";
    public static readonly string[] Values = [All, Display];

    public static JsonObject Schema() => new()
    {
        ["description"] = "all (default): windows on every display. display: only the focused window's display.",
        ["enum"] = new JsonArray(All, Display),
    };
}
