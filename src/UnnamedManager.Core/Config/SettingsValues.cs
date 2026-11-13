namespace UnnamedManager.Core.Config;

/// Rules for values typed in Settings (S-047), so Settings never makes an invalid file.
public static class SettingsValues
{
    public const int MaxMilliseconds = 60_000;
    public const double MaxGap = 64, GapStep = 2;

    /// A delay or timeout in milliseconds: never below zero, at most a minute.
    public static int Milliseconds(double value) => double.IsNaN(value) ? 0 : (int)Math.Clamp(Math.Round(value), 0, MaxMilliseconds);

    /// The gap between windows: 0 to 64 epx in steps of 2.
    public static double Gap(double value) => double.IsNaN(value) ? 0 : Math.Clamp(Math.Round(value / GapStep, MidpointRounding.AwayFromZero) * GapStep, 0, MaxGap);

    /// Why a key can't be the cancel or back key, or null. `other` is the other one.
    public static string? RuntimeKeyProblem(Chord chord, string other)
    {
        if (chord.Modifiers != Modifiers.None) return "Use one key with no modifiers.";
        if (string.Equals(chord.Key.Name(), other, StringComparison.OrdinalIgnoreCase)) return $"{chord.Key.Name()} is already the other key.";
        return null;
    }

    public static Config WithRuntime(this Config config, Func<RuntimeSettings, RuntimeSettings> change) => config with { Runtime = change(config.Runtime) };
    public static Config WithUI(this Config config, Func<UISettings, UISettings> change) => config with { UI = change(config.UI) };
    public static Config WithGap(this Config config, double gap) => config with { Windows = config.Windows with { Gap = Gap(gap) } };
}
