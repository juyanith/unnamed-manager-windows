using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Keyboard;

/// One event as the low-level keyboard hook sees it. `IsOurs` marks an event
/// the app injected itself (dwExtraInfo), which always passes untouched.
public readonly record struct RawKey(int Vk, int ScanCode, bool Extended, bool IsUp, bool IsOurs = false);

/// What the hook does with one event. `SendMask` asks the hook to inject the
/// masking key now, so letting go of Win or Alt later opens neither Start, the
/// Office hub, nor the app's menu bar (S-003).
public readonly record struct HookDecision(bool Consume, SequenceEffect? Effect = null, bool SendMask = false, Chord? Recorded = null)
{
    public static readonly HookDecision Pass = new(false);
}

/// Sits between the hook and the engine: tracks which modifier keys are held
/// and which keys are down (Windows reports a repeat as another key-down),
/// turns scan codes into physical keys, and decides when to mask. Thread-safe:
/// the hook thread calls Process while the UI thread may swap the config.
public sealed class KeyboardTracker(LoadedConfig config)
{
    // Virtual keys of the modifier keys, left and right.
    private const int LShift = 0xA0, RShift = 0xA1, LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, RAlt = 0xA5, LWin = 0x5B, RWin = 0x5C;
    // The generic ones some injected events use.
    private const int Shift = 0x10, Ctrl = 0x11, Alt = 0x12;

    private readonly object _lock = new();
    private readonly SequenceEngine _engine = new(config);
    private readonly HashSet<int> _modifierKeysDown = [];
    private readonly HashSet<int> _keysDown = [];
    /// True once the mask was sent since Win or Alt last went down.
    private bool _masked;

    public bool IsActive { get { lock (_lock) return _engine.IsActive; } }

    private bool _recording;
    /// Keys taken by the key recorder whose key-up is still to come.
    private readonly HashSet<int> _recordedDown = [];

    /// While a key field records (S-040), no leader matches: the next key with the
    /// modifiers held goes to the recorder, and the app in front never sees it.
    public bool Recording
    {
        get { lock (_lock) return _recording; }
        set
        {
            lock (_lock)
            {
                _recording = value;
                if (value) _engine.Reset();
            }
        }
    }

    public void ReplaceConfig(LoadedConfig config)
    {
        lock (_lock) _engine.ReplaceConfig(config);
    }

    /// The app in front, from the app's foreground watcher (S-014).
    public void SetFrontApp(FrontApp? front)
    {
        lock (_lock) _engine.SetFrontApp(front);
    }

    /// Ends any sequence and forgets held keys, for when the hook is removed.
    public void Reset()
    {
        lock (_lock)
        {
            _engine.Reset();
            _modifierKeysDown.Clear();
            _keysDown.Clear();
            _recordedDown.Clear();
            _masked = false;
        }
    }

    /// The modifier keys physically held now, by virtual key (left and right apart).
    public IReadOnlyList<int> HeldModifierKeys
    {
        get { lock (_lock) return [.. _modifierKeysDown]; }
    }

    public Modifiers Held
    {
        get
        {
            lock (_lock) return HeldModifiers();
        }
    }

    private Modifiers HeldModifiers()
    {
        var m = Modifiers.None;
        foreach (var vk in _modifierKeysDown)
        {
            m |= vk switch
            {
                LShift or RShift or Shift => Modifiers.Shift,
                LCtrl or RCtrl or Ctrl => Modifiers.Ctrl,
                LAlt or RAlt or Alt => Modifiers.Alt,
                LWin or RWin => Modifiers.Win,
                _ => Modifiers.None,
            };
        }
        return m;
    }

    /// The held leader's modifiers while a held menu waits for them to be let go (spec
    /// v18), so the app can check they're still down. None otherwise.
    public Modifiers HoldModifiers
    {
        get { lock (_lock) return _engine.HoldModifiers; }
    }

    /// A missed key-up (spec v18: noticed within 200 ms): forgets the modifier keys that
    /// `isDown` says are up, and returns what letting go of them chooses.
    public SequenceEffect? Resync(Func<int, bool> isDown)
    {
        lock (_lock)
        {
            if (_modifierKeysDown.RemoveWhere(vk => !isDown(vk)) == 0) return null;
            var held = HeldModifiers();
            if ((held & (Modifiers.Win | Modifiers.Alt)) == 0) _masked = false;
            return _engine.ModifiersChanged(held);
        }
    }

    /// Fills the list the sequence waits on (spec v18), and returns what it shows and the
    /// keys pressed meanwhile do.
    public IReadOnlyList<SequenceEffect> Fill(IReadOnlyList<ListItem> items, ListItem? front)
    {
        lock (_lock) return _engine.Fill(items, front);
    }

    /// An item's window closed while its list is up.
    public SequenceEffect? RemoveItem(long id)
    {
        lock (_lock) return _engine.RemoveItem(id);
    }

    /// The list on screen, if the sequence is in one.
    public BindingNode? CurrentList
    {
        get { lock (_lock) return _engine.Current is { List: not null } list ? list : null; }
    }

    public static bool IsModifierKey(int vk) => vk is LShift or RShift or LCtrl or RCtrl or LAlt or RAlt or LWin or RWin or Shift or Ctrl or Alt;

    public HookDecision Process(RawKey raw)
    {
        if (raw.IsOurs) return HookDecision.Pass;
        lock (_lock)
        {
            if (IsModifierKey(raw.Vk))
            {
                // Modifiers always reach the app in front, so its idea of what's held stays right.
                if (raw.IsUp) _modifierKeysDown.Remove(raw.Vk); else _modifierKeysDown.Add(raw.Vk);
                var held = HeldModifiers();
                var chosen = _engine.ModifiersChanged(held);
                if ((held & (Modifiers.Win | Modifiers.Alt)) == 0) _masked = false;
                return new HookDecision(false, chosen);
            }

            var code = raw.ScanCode | (raw.Extended ? 0x100 : 0);
            bool repeat = false;
            if (raw.IsUp) _keysDown.Remove(code);
            else repeat = !_keysDown.Add(code);

            if (raw.IsUp && _recordedDown.Remove(code)) return new HookDecision(true);
            if (_recording && !raw.IsUp && Keys.FromScanCode(code) is { } recorded)
            {
                _recordedDown.Add(code);
                var held = HeldModifiers();
                var sendMask = !_masked && (held & (Modifiers.Win | Modifiers.Alt)) != 0;
                if (sendMask) _masked = true;
                return new HookDecision(true, SendMask: sendMask, Recorded: repeat ? null : new Chord(held, recorded));
            }

            var output = _engine.Handle(new KeyEvent(code, !raw.IsUp, repeat, HeldModifiers()));
            var mask = false;
            if (output.Consume && !raw.IsUp && !_masked && (HeldModifiers() & (Modifiers.Win | Modifiers.Alt)) != 0)
            {
                mask = true;
                _masked = true;
            }
            return new HookDecision(output.Consume, output.Effect, mask);
        }
    }
}
