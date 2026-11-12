using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Keyboard;

/// A key event, reduced to what the engine needs. `Code` is the physical key
/// (Keys.ScanCode), so a key with no name still pairs its down with its up.
/// `Modifiers` are the ones held when the key went down, not counting itself.
public readonly record struct KeyEvent(int Code, bool IsDown, bool IsRepeat, Modifiers Modifiers)
{
    public KeyName? Key => Keys.FromScanCode(Code);

    public static KeyEvent Down(KeyName key, Modifiers modifiers = Modifiers.None, bool repeat = false) => new(key.ScanCode(), true, repeat, modifiers);
    public static KeyEvent Up(KeyName key) => new(key.ScanCode(), false, false, Modifiers.None);
}

/// What the app shows or does after one event.
public abstract record SequenceEffect
{
    /// A sequence started or moved into a menu, Back included. Show it.
    /// `Front` is the app that was in front when the leader was pressed.
    public sealed record ShowMenu(BindingNode Menu, IReadOnlyList<string> Path, FrontApp? Front = null) : SequenceEffect;
    /// A binding was picked, or a direct binding's chord pressed: run its action for
    /// `Front`. The sequence has ended.
    public sealed record Run(BindingNode Binding, FrontApp? Front = null) : SequenceEffect;
    /// A Context Aware binding with nothing to run for the app in front. The sequence
    /// stays in its menu, and the key gives error feedback (S-014).
    public sealed record Unavailable(BindingNode Binding, IReadOnlyList<string> Path) : SequenceEffect;
    /// Back from a leader's menu opened Home (S-021). Show it.
    public sealed record ShowHome(Home Home, IReadOnlyList<string> Path, FrontApp? Front = null) : SequenceEffect;
    /// Cancel, or Back from a leader's menu with one top-level entry, or Back or Cancel
    /// in Home. The sequence has ended.
    public sealed record Cancelled : SequenceEffect;
    /// A key that isn't bound. The sequence stays where it is.
    public sealed record Unbound(IReadOnlyList<string> Path) : SequenceEffect;
}

public readonly record struct SequenceOutput(bool Consume, SequenceEffect? Effect = null)
{
    public static readonly SequenceOutput Pass = new(false);
}

/// The sequence state machine (spec, Behavior both apps share). Pure: key
/// events in, decisions out, no Win32 and no timers, so the hook callback stays
/// fast and every rule is unit-tested. Not thread-safe: KeyboardTracker owns it.
public sealed class SequenceEngine(LoadedConfig config)
{
    private readonly List<BindingNode> _stack = [];
    /// Leader modifiers still held. They're ignored on step keys.
    private Modifiers _heldLeaderModifiers;
    /// Key-downs that were consumed, so their repeats and key-ups are consumed too.
    private readonly HashSet<int> _consumed = [];
    /// The app in front right now, kept up to date by the app.
    private FrontApp? _front;

    public LoadedConfig Config { get; private set; } = config;

    /// Home, while the sequence is in it (S-021).
    private Home? _home;
    /// True once the sequence has been to Home, so its path starts there.
    private bool _viaHome;

    public bool IsActive => _stack.Count > 0 || _home is not null;

    /// The app that was in front when the leader was pressed. Actions go to it.
    public FrontApp? SequenceFront { get; private set; }

    /// The titles from the leader down: ["Commands", "Applications"].
    public IReadOnlyList<string> Path => (_viaHome ? [Home.Title] : Array.Empty<string>()).Concat(_stack.Select(m => m.Title)).ToList();

    /// Swaps in a new config. A running sequence ends.
    public void ReplaceConfig(LoadedConfig config)
    {
        Config = config;
        Reset();
    }

    /// The app in front. Call it whenever that changes.
    public void SetFrontApp(FrontApp? front) => _front = front;

    /// Ends the sequence from outside, such as Enabled turned off.
    public void Reset()
    {
        _stack.Clear();
        _home = null;
        _viaHome = false;
        _heldLeaderModifiers = Modifiers.None;
    }

    /// The modifiers held changed. Once a leader modifier is let go, it stops being ignored.
    public void ModifiersChanged(Modifiers held) => _heldLeaderModifiers &= held;

    public SequenceOutput Handle(KeyEvent e)
    {
        if (!e.IsDown) return new SequenceOutput(_consumed.Remove(e.Code));
        if (!IsActive) return Idle(e);
        _consumed.Add(e.Code);
        if (e.IsRepeat) return new SequenceOutput(true);
        return _home is { } home ? Pick(home, e) : Step(e);
    }

    private SequenceOutput Idle(KeyEvent e)
    {
        if (e.Key is not { } key || Config.Leader(new Chord(e.Modifiers, key)) is not { } leader)
            // A repeat of a consumed leader is still consumed.
            return new SequenceOutput(e.IsRepeat && _consumed.Contains(e.Code));
        if (!leader.IsMenu)
        {
            // A direct binding runs at once, with no sequence. One with nothing to run
            // for the app in front lets the chord through, decided before the key-down is eaten.
            if (!Config.IsAvailable(leader, _front)) return SequenceOutput.Pass;
            if (e.IsRepeat) return new SequenceOutput(true);
            _consumed.Add(e.Code);
            return new SequenceOutput(true, new SequenceEffect.Run(leader, _front));
        }
        if (e.IsRepeat) return new SequenceOutput(true);
        _consumed.Add(e.Code);
        _heldLeaderModifiers = e.Modifiers;
        SequenceFront = _front;
        _stack.Add(leader);
        return new SequenceOutput(true, new SequenceEffect.ShowMenu(leader, Path, SequenceFront));
    }

    private SequenceOutput Step(KeyEvent e)
    {
        var menu = _stack[^1];
        if (e.Key is not { } key) return new SequenceOutput(true, new SequenceEffect.Unbound(Path));
        var extra = e.Modifiers & ~_heldLeaderModifiers;

        if (extra == Modifiers.None && key == Config.CancelKey)
        {
            Reset();
            return new SequenceOutput(true, new SequenceEffect.Cancelled());
        }
        if (extra == Modifiers.None && key == Config.BackKey)
        {
            var leaving = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            if (_stack.Count == 0)
            {
                // Above a leader's menu is Home, when there's more than the one top-level entry (S-021).
                if (Config.Leaders.Count < 2)
                {
                    Reset();
                    return new SequenceOutput(true, new SequenceEffect.Cancelled());
                }
                _home = new Home(Config.Leaders, leaving.Path.Leader.Modifiers);
                _viaHome = true;
                return new SequenceOutput(true, new SequenceEffect.ShowHome(_home, Path, SequenceFront));
            }
            return new SequenceOutput(true, new SequenceEffect.ShowMenu(_stack[^1], Path, SequenceFront));
        }
        if (extra != Modifiers.None || menu.Child(key) is not { } child)
            return new SequenceOutput(true, new SequenceEffect.Unbound(Path));
        if (child.IsMenu)
        {
            _stack.Add(child);
            return new SequenceOutput(true, new SequenceEffect.ShowMenu(child, Path, SequenceFront));
        }
        if (!Config.IsAvailable(child, SequenceFront))
            return new SequenceOutput(true, new SequenceEffect.Unavailable(child, Path));
        var front = SequenceFront;
        Reset();
        return new SequenceOutput(true, new SequenceEffect.Run(child, front));
    }

    /// A key in Home: Cancel and Back end the sequence, a leader opens its menu, and a
    /// direct binding runs for the app that was in front when the sequence started.
    private SequenceOutput Pick(Home home, KeyEvent e)
    {
        if (e.Key is not { } key) return new SequenceOutput(true, new SequenceEffect.Unbound(Path));
        var extra = e.Modifiers & ~_heldLeaderModifiers;
        if (extra == Modifiers.None && (key == Config.CancelKey || key == Config.BackKey))
        {
            Reset();
            return new SequenceOutput(true, new SequenceEffect.Cancelled());
        }
        if (home.Entry(new Chord(e.Modifiers, key)) is not { } entry)
            return new SequenceOutput(true, new SequenceEffect.Unbound(Path));
        if (entry.IsMenu)
        {
            _home = null;
            _heldLeaderModifiers = e.Modifiers;
            _stack.Add(entry);
            return new SequenceOutput(true, new SequenceEffect.ShowMenu(entry, Path, SequenceFront));
        }
        if (!Config.IsAvailable(entry, SequenceFront))
            return new SequenceOutput(true, new SequenceEffect.Unavailable(entry, Path));
        var front = SequenceFront;
        Reset();
        return new SequenceOutput(true, new SequenceEffect.Run(entry, front));
    }
}
