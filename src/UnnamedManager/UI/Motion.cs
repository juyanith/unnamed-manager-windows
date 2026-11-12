using Windows.UI.ViewManagement;

namespace UnnamedManager.UI;

/// Durations, from Fluent's timings. Every animation goes through Motion, so "Animation effects"
/// off (Settings > Accessibility > Visual effects) applies everywhere. Rules: design/DESIGN-SYSTEM.md.
public static class Motion
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(83);
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(167);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(250);

    /// The Guide fades in and out this fast. A menu change inside it is instant.
    public static TimeSpan GuideFade => Animate(Fast);

    /// How long the accent outline stays for a key that isn't bound (S-033). It's a state,
    /// not an animation, so it still shows with animation effects off.
    public static readonly TimeSpan ErrorFlashHold = TimeSpan.FromMilliseconds(300);

    /// How long a failed action's message stays before the Guide closes (S-016).
    public static readonly TimeSpan ErrorMessageHold = TimeSpan.FromSeconds(2);

    private static readonly UISettings Settings = new();

    /// False when the person turned animation effects off.
    public static bool Enabled => Settings.AnimationsEnabled;

    /// The duration to use: zero when animation effects are off.
    public static TimeSpan Animate(TimeSpan duration) => Enabled ? duration : TimeSpan.Zero;
}
