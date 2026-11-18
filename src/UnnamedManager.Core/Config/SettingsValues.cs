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

    public const double MaxDistance = 200;

    /// Maximum columns or rows: a whole number from 1 to 8 (spec v8).
    public static int Limit(double value) =>
        double.IsNaN(value) ? 2 : (int)Math.Clamp(Math.Round(value), WindowSettings.MinLimit, WindowSettings.MaxLimit);

    /// One of Focus Window's distances (spec v12): whole pixels from 0 to 200.
    public static double Distance(double value) => double.IsNaN(value) ? 0 : Math.Clamp(Math.Round(value), 0, MaxDistance);

    public static Config WithWindows(this Config config, Func<WindowSettings, WindowSettings> change) => config with { Windows = change(config.Windows) };

    /// Adds an app to `windows.apps`, or changes its rules (spec v15), keeping the list's order.
    public static Config WithAppRules(this Config config, string app, AppRules rules)
    {
        var apps = config.Windows.Apps.ToList();
        var at = apps.FindIndex(a => string.Equals(a.Key, app, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) apps[at] = KeyValuePair.Create(apps[at].Key, rules);
        else apps.Add(KeyValuePair.Create(app, rules));
        return config.WithWindows(w => w with { Apps = apps });
    }

    public static Config WithoutAppRules(this Config config, string app) =>
        config.WithWindows(w => w with { Apps = [.. w.Apps.Where(a => a.Key != app)] });
}
