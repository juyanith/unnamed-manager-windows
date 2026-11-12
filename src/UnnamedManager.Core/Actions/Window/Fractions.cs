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
