using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Keyboard;

/// What the app does with the Guide and the sound.
public abstract record GuideCommand
{
    /// Show this menu, or update the Guide to it if it's already up. `Highlight` is the
    /// highlighted item of a held menu, 0 for Cancel (spec v18), or null.
    public sealed record Show(BindingNode Menu, IReadOnlyList<string> Path, FrontApp? Front, int? Highlight = null) : GuideCommand;
    /// Show Home, or update the Guide to it (S-021).
    public sealed record ShowHome(Home Home, IReadOnlyList<string> Path, FrontApp? Front) : GuideCommand;
    /// Hide the Guide. `Cancelled` when the sequence was cancelled, for Narrator.
    public sealed record Hide(bool Cancelled = false) : GuideCommand;
    /// Flash the Guide's outline for a key that isn't bound (S-033).
    public sealed record Flash : GuideCommand;
    /// Play Windows' "Default Beep" for a key that isn't bound (S-033).
    public sealed record Bell : GuideCommand;
    /// The timeout ran out: end the sequence in the key engine (S-032).
    public sealed record EndSequence : GuideCommand;
}

/// When the Guide shows, updates and hides. Pure: sequence effects and timer fires
/// in, with the time in milliseconds; commands out. The app arms one timer for
/// `NextDeadline` and calls `Tick`. Every Guide timing rule lives here, so unit
/// tests check them with a made-up clock.
public sealed class GuideSchedule(RuntimeSettings runtime, UISettings ui)
{
    public RuntimeSettings Runtime { get; } = runtime;
    public UISettings UI { get; } = ui;

    /// True while the Guide is on screen.
    public bool IsVisible { get; private set; }

    /// The menu to show once the delay passes, while the Guide isn't up yet.
    private GuideCommand? _pending;
    /// When the pending menu shows.
    private long? _showAt;

    /// When the visible Guide times out.
    private long? _timeoutAt;
    /// True while a held menu is held: there's no timeout then (spec v18).
    private bool _holding;

    /// The next time `Tick` has something to do, if any.
    public long? NextDeadline => _showAt is { } show && _timeoutAt is { } timeout ? Math.Min(show, timeout) : _showAt ?? _timeoutAt;

    public IReadOnlyList<GuideCommand> Handle(SequenceEffect effect, long now)
    {
        switch (effect)
        {
            case SequenceEffect.ShowMenu m:
                _holding = false;
                return Show(new GuideCommand.Show(m.Menu, m.Path, m.Front), now);
            case SequenceEffect.ShowHeld h:
                _holding = true;
                return Show(new GuideCommand.Show(h.Menu, h.Path, h.Front, h.Highlight), now);
            case SequenceEffect.ShowHome h:
                _holding = false;
                return Show(new GuideCommand.ShowHome(h.Home, h.Path, h.Front), now);
            case SequenceEffect.Run:
                return End(new GuideCommand.Hide());
            case SequenceEffect.Cancelled:
                return End(new GuideCommand.Hide(Cancelled: true));
            case SequenceEffect.Unbound or SequenceEffect.Unavailable:
                // Unavailable only comes once the modifiers are let go.
                if (effect is SequenceEffect.Unavailable) _holding = false;
                return Error(now);
            default:
                return [];
        }
    }

    public IReadOnlyList<GuideCommand> Tick(long now)
    {
        if (_showAt is { } show && now >= show) return Reveal(now);
        if (_timeoutAt is { } timeout && now >= timeout) return [.. End(new GuideCommand.Hide(Cancelled: true)), new GuideCommand.EndSequence()];
        return [];
    }

    /// A key that isn't bound, or a binding with nothing to run here: the sequence
    /// stays where it was, the bell plays, and the Guide flashes.
    private IReadOnlyList<GuideCommand> Error(long now)
    {
        List<GuideCommand> commands = UI.ErrorBell ? [new GuideCommand.Bell()] : [];
        if (!Runtime.ShowHud) return commands;
        // A key is still a key: the timeout starts again.
        if (IsVisible) RestartTimeout(now);
        // A wrong key suggests the sequence was forgotten: show it at once.
        else commands.AddRange(Reveal(now));
        if (UI.ErrorFlash && IsVisible) commands.Add(new GuideCommand.Flash());
        return commands;
    }

    /// Shows a menu or Home now, or once the delay passes with no key.
    private IReadOnlyList<GuideCommand> Show(GuideCommand show, long now)
    {
        if (!Runtime.ShowHud) return [];
        // Once shown, the Guide stays and follows the sequence.
        if (IsVisible)
        {
            RestartTimeout(now);
            return [show];
        }
        _pending = show;
        if (Runtime.HudDelayMs <= 0) return Reveal(now);
        // Every key, Back included, restarts the delay.
        _showAt = now + Runtime.HudDelayMs;
        return [];
    }

    /// Shows the pending menu now and starts the timeout.
    private IReadOnlyList<GuideCommand> Reveal(long now)
    {
        if (_pending is not { } pending) return [];
        _pending = null;
        _showAt = null;
        IsVisible = true;
        RestartTimeout(now);
        return [pending];
    }

    /// The timeout runs only while the Guide is visible, from the last key.
    private void RestartTimeout(long now) =>
        _timeoutAt = Runtime.TimeoutMs > 0 && IsVisible && !_holding ? now + Runtime.TimeoutMs : null;

    /// Forgets the sequence, such as when Enabled is turned off.
    public void Reset() => End(new GuideCommand.Hide());

    private IReadOnlyList<GuideCommand> End(GuideCommand.Hide hide)
    {
        var was = IsVisible;
        _holding = false;
        IsVisible = false;
        _pending = null;
        _showAt = null;
        _timeoutAt = null;
        return was ? [hide] : [];
    }
}
