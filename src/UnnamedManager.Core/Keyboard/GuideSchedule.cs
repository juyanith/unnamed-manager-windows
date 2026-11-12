using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Keyboard;

/// What the app does with the Guide and the sound.
public abstract record GuideCommand
{
    /// Show this menu, or update the Guide to it if it's already up.
    public sealed record Show(BindingNode Menu, IReadOnlyList<string> Path, FrontApp? Front) : GuideCommand;
    /// Show Home, or update the Guide to it (S-021).
    public sealed record ShowHome(Home Home, IReadOnlyList<string> Path, FrontApp? Front) : GuideCommand;
    /// Hide the Guide. `Cancelled` when the sequence was cancelled, for Narrator.
    public sealed record Hide(bool Cancelled = false) : GuideCommand;
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

    /// The next time `Tick` has something to do, if any.
    public long? NextDeadline => _showAt is { } show && _timeoutAt is { } timeout ? Math.Min(show, timeout) : _showAt ?? _timeoutAt;

    public IReadOnlyList<GuideCommand> Handle(SequenceEffect effect, long now)
    {
        switch (effect)
        {
            case SequenceEffect.ShowMenu m:
                return Show(new GuideCommand.Show(m.Menu, m.Path, m.Front), now);
            case SequenceEffect.ShowHome h:
                return Show(new GuideCommand.ShowHome(h.Home, h.Path, h.Front), now);
            case SequenceEffect.Run:
                return End(new GuideCommand.Hide());
            case SequenceEffect.Cancelled:
                return End(new GuideCommand.Hide(Cancelled: true));
            default:
                // A key that isn't bound is still a key: the timeout starts again.
                RestartTimeout(now);
                return [];
        }
    }

    public IReadOnlyList<GuideCommand> Tick(long now)
    {
        if (_showAt is { } show && now >= show) return Reveal(now);
        if (_timeoutAt is { } timeout && now >= timeout) return [.. End(new GuideCommand.Hide(Cancelled: true)), new GuideCommand.EndSequence()];
        return [];
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
        _timeoutAt = Runtime.TimeoutMs > 0 && IsVisible ? now + Runtime.TimeoutMs : null;

    /// Forgets the sequence, such as when Enabled is turned off.
    public void Reset() => End(new GuideCommand.Hide());

    private IReadOnlyList<GuideCommand> End(GuideCommand.Hide hide)
    {
        var was = IsVisible;
        IsVisible = false;
        _pending = null;
        _showAt = null;
        _timeoutAt = null;
        return was ? [hide] : [];
    }
}
