using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Keyboard;

/// One event as the low-level keyboard hook sees it. `IsOurs` marks an event
/// the app injected itself (dwExtraInfo), which always passes untouched.
public readonly record struct RawKey(int Vk, int ScanCode, bool Extended, bool IsUp, bool IsOurs = false);

/// What the hook does with one event. `SendMask` asks the hook to inject the
/// masking key now, so letting go of Win or Alt later opens neither Start, the
/// Office hub, nor the app's menu bar (S-003).
public readonly record struct HookDecision(bool Consume, SequenceEffect? Effect = null, bool SendMask = false)
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
                _engine.ModifiersChanged(held);
                if ((held & (Modifiers.Win | Modifiers.Alt)) == 0) _masked = false;
                return HookDecision.Pass;
            }

            var code = raw.ScanCode | (raw.Extended ? 0x100 : 0);
            bool repeat = false;
            if (raw.IsUp) _keysDown.Remove(code);
            else repeat = !_keysDown.Add(code);

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
