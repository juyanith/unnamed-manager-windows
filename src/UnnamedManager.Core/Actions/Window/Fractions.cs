namespace UnnamedManager.Core.Config;

// The window actions' fields (spec v2, Window actions). M3 builds what they do.

/// A frame as fractions of the usable area.
public readonly record struct FractionFrame(double X, double Y, double Width, double Height);

internal static class Fractions
{
    public static void From0To1(ActionFields f, string field, params (string Name, double Value)[] values)
    {
        foreach (var (name, value) in values)
            if (value is < 0 or > 1) throw f.Problem(Rule.BadFraction, $"“{name}” must be from 0 to 1 in “{field}” in {f.Place}.");
    }

    public static void Size(ActionFields f, string field, params (string Name, double Value)[] values)
    {
        foreach (var (name, value) in values)
            if (!(value > 0 && value <= 1)) throw f.Problem(Rule.BadFraction, $"“{name}” must be above 0 and at most 1 in “{field}” in {f.Place}.");
    }
}

/// The schema parts the window families share, in the spec's shape.
internal static class WindowSchema
{
    public static System.Text.Json.Nodes.JsonObject Fraction() => new() { ["maximum"] = 1, ["minimum"] = 0, ["type"] = "number" };
    public static System.Text.Json.Nodes.JsonObject Size() => new() { ["exclusiveMinimum"] = 0, ["maximum"] = 1, ["type"] = "number" };
    public static System.Text.Json.Nodes.JsonObject Points() => new() { ["type"] = "number" };

    public static System.Text.Json.Nodes.JsonObject Object(System.Text.Json.Nodes.JsonObject properties, params string[] required) => new()
    {
        ["additionalProperties"] = false,
        ["properties"] = properties,
        ["required"] = new System.Text.Json.Nodes.JsonArray([.. required.Select(r => (System.Text.Json.Nodes.JsonNode)r)]),
        ["type"] = "object",
    };

    /// `{ "dx": 3 }`: the named numbers, those that are 0 left out when `skipZero`.
    public static System.Text.Json.Nodes.JsonObject Numbers(bool skipZero, params (string Name, double Value)[] values)
    {
        var o = new System.Text.Json.Nodes.JsonObject();
        foreach (var (name, value) in values)
            if (!skipZero || value != 0) o[name] = value;
        return o;
    }
}
