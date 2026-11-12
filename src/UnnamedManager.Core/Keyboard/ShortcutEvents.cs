using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Keyboard;

/// One key event to inject. A modifier goes by its virtual key; any other key by
/// its scan code, so it lands on its US ANSI position whatever the layout (spec,
/// Key names). The app marks every one as its own.
public readonly record struct KeyStroke(int Vk, int Scan, bool Extended, bool Up)
{
    public override string ToString() => $"{(Vk != 0 ? $"vk{Vk:X2}" : $"sc{Scan:X2}{(Extended ? "e" : "")}")}{(Up ? "↑" : "↓")}";
}

/// The events that press Send Shortcut's chords in the app in front (S-013).
public static class ShortcutEvents
{
    public const int LShift = 0xA0, RShift = 0xA1, LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, RAlt = 0xA5, LWin = 0x5B, RWin = 0x5C;

    /// The unassigned virtual key KeyboardHook uses to mask Win and Alt.
    public const int MaskKey = 0xE8;

    private static readonly KeyStroke Mask = new(MaskKey, 0, false, false);
    private static readonly KeyStroke MaskUp = new(MaskKey, 0, false, true);

    /// First, key-ups for the leader modifiers still held, so the app in front sees only
    /// each chord's own modifiers. Then each chord: its modifiers down, the key down
    /// and up, its modifiers up. Whenever Win or Alt goes up, the masking key comes
    /// first, so neither Start nor a menu bar opens.
    public static List<KeyStroke> Build(IReadOnlyList<Chord> chords, IReadOnlyCollection<int> heldModifierKeys)
    {
        var events = new List<KeyStroke>();
        if (heldModifierKeys.Any(IsWinOrAlt)) events.AddRange([Mask, MaskUp]);
        foreach (var vk in heldModifierKeys) events.Add(Modifier(vk, up: true));

        foreach (var chord in chords)
        {
            var modifiers = Order.Where(m => chord.Modifiers.HasFlag(m.Modifier)).Select(m => m.Vk).ToList();
            foreach (var vk in modifiers) events.Add(Modifier(vk, up: false));
            var code = chord.Key.ScanCode();
            events.Add(new KeyStroke(0, code & 0xFF, code > 0xFF, false));
            events.Add(new KeyStroke(0, code & 0xFF, code > 0xFF, true));
            if (modifiers.Any(IsWinOrAlt)) events.AddRange([Mask, MaskUp]);
            foreach (var vk in Enumerable.Reverse(modifiers)) events.Add(Modifier(vk, up: true));
        }
        return events;
    }

    private static readonly (Modifiers Modifier, int Vk)[] Order =
        [(Modifiers.Ctrl, LCtrl), (Modifiers.Alt, LAlt), (Modifiers.Shift, LShift), (Modifiers.Win, LWin)];

    private static bool IsWinOrAlt(int vk) => vk is LWin or RWin or LAlt or RAlt or 0x12;

    private static KeyStroke Modifier(int vk, bool up) => vk switch
    {
        LShift => new(vk, 0x2A, false, up),
        RShift => new(vk, 0x36, false, up),
        LCtrl or 0x11 => new(vk, 0x1D, false, up),
        RCtrl => new(vk, 0x1D, true, up),
        LAlt or 0x12 => new(vk, 0x38, false, up),
        RAlt => new(vk, 0x38, true, up),
        LWin => new(vk, 0x5B, true, up),
        RWin => new(vk, 0x5C, true, up),
        _ => new(vk, 0, false, up),
    };
}
