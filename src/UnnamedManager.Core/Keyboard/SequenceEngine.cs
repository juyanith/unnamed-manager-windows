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
    public sealed record ShowMenu(BindingNode Menu, IReadOnlyList<string> Path) : SequenceEffect;
    /// A binding was picked, or a direct binding's chord pressed: run its action. The sequence has ended.
    public sealed record Run(BindingNode Binding) : SequenceEffect;
    /// Cancel, or Back from a leader's menu. The sequence has ended.
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

    public LoadedConfig Config { get; private set; } = config;

    public bool IsActive => _stack.Count > 0;

    /// The titles from the leader down: ["Commands", "Applications"].
    public IReadOnlyList<string> Path => _stack.Select(m => m.Title).ToList();

    /// Swaps in a new config. A running sequence ends.
    public void ReplaceConfig(LoadedConfig config)
    {
        Config = config;
        Reset();
    }

    /// Ends the sequence from outside, such as Enabled turned off.
    public void Reset()
    {
        _stack.Clear();
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
        return Step(e);
    }

    private SequenceOutput Idle(KeyEvent e)
    {
        if (e.Key is not { } key || Config.Leader(new Chord(e.Modifiers, key)) is not { } leader)
            // A repeat of a consumed leader is still consumed.
            return new SequenceOutput(e.IsRepeat && _consumed.Contains(e.Code));
        if (e.IsRepeat) return new SequenceOutput(true);
        _consumed.Add(e.Code);
        if (!leader.IsMenu) return new SequenceOutput(true, new SequenceEffect.Run(leader));
        _heldLeaderModifiers = e.Modifiers;
        _stack.Add(leader);
        return new SequenceOutput(true, new SequenceEffect.ShowMenu(leader, Path));
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
            _stack.RemoveAt(_stack.Count - 1);
            // Home comes with S-021; until then Back from a leader's menu ends the sequence.
            if (_stack.Count == 0)
            {
                Reset();
                return new SequenceOutput(true, new SequenceEffect.Cancelled());
            }
            return new SequenceOutput(true, new SequenceEffect.ShowMenu(_stack[^1], Path));
        }
        if (extra != Modifiers.None || menu.Child(key) is not { } child)
            return new SequenceOutput(true, new SequenceEffect.Unbound(Path));
        if (child.IsMenu)
        {
            _stack.Add(child);
            return new SequenceOutput(true, new SequenceEffect.ShowMenu(child, Path));
        }
        Reset();
        return new SequenceOutput(true, new SequenceEffect.Run(child));
    }
}
