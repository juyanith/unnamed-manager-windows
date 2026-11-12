namespace UnnamedManager.Core.Config;

/// A key, named by its physical position on the US ANSI layout (spec, Key names).
/// Names are case-insensitive; <see cref="Keys.Name"/> gives the canonical spelling.
public enum KeyName
{
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
    Space, Tab, Enter, Backspace, Delete, Escape,
    Left, Right, Up, Down, Home, End, PageUp, PageDown,
    Grave, Minus, Equal, LeftBracket, RightBracket, Backslash,
    Semicolon, Quote, Comma, Period, Slash,
}

/// The four modifiers. `Cmd` is the Win key on Windows, and `Primary` is Ctrl
/// (spec, Modifiers).
[Flags]
public enum Modifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
    Hyper = Ctrl | Alt | Shift | Win,
    Meh = Ctrl | Alt | Shift,
    Cag = Ctrl | Alt | Win,
}

public static class Keys
{
    private static readonly Dictionary<string, KeyName> ByName = Enum.GetValues<KeyName>()
        .ToDictionary(k => Name(k).ToLowerInvariant());

    /// The canonical spelling used in the file and the Guide: "A", "0", "PageUp".
    public static string Name(this KeyName key) => key switch
    {
        >= KeyName.D0 and <= KeyName.D9 => ((int)(key - KeyName.D0)).ToString(),
        _ => key.ToString(),
    };

    public static KeyName? ParseKey(string text) => ByName.TryGetValue(text.ToLowerInvariant(), out var key) ? key : null;

    /// What a keycap shows, the way Windows writes keys in its own shortcut hints:
    /// "Esc", "PgUp", arrows as ← → ↑ ↓, and punctuation as the character it types
    /// on a US keyboard.
    public static string Label(this KeyName key) => key switch
    {
        KeyName.Escape => "Esc",
        KeyName.PageUp => "PgUp",
        KeyName.PageDown => "PgDn",
        KeyName.Delete => "Del",
        KeyName.Left => "←",
        KeyName.Right => "→",
        KeyName.Up => "↑",
        KeyName.Down => "↓",
        KeyName.Grave => "`",
        KeyName.Minus => "-",
        KeyName.Equal => "=",
        KeyName.LeftBracket => "[",
        KeyName.RightBracket => "]",
        KeyName.Backslash => "\\",
        KeyName.Semicolon => ";",
        KeyName.Quote => "'",
        KeyName.Comma => ",",
        KeyName.Period => ".",
        KeyName.Slash => "/",
        _ => key.Name(),
    };

    /// What Narrator reads for a key: its name in words, never a symbol.
    public static string SpokenName(this KeyName key) => key switch
    {
        KeyName.Escape => "Escape",
        KeyName.PageUp => "Page Up",
        KeyName.PageDown => "Page Down",
        KeyName.Left => "Left arrow",
        KeyName.Right => "Right arrow",
        KeyName.Up => "Up arrow",
        KeyName.Down => "Down arrow",
        KeyName.LeftBracket => "Left bracket",
        KeyName.RightBracket => "Right bracket",
        _ => key.Name(),
    };

    /// A physical key on Windows: its set 1 scan code, plus 0x100 for keys with
    /// the extended flag (the arrows, Home, End, Page Up, Page Down, Delete).
    /// Matching on these, not on virtual keys, keeps names on their US ANSI
    /// positions whatever the active layout (spec, Key names).
    public static int ScanCode(this KeyName key) => ScanCodes[key];

    /// The key at a physical position, or null for keys with no name here
    /// (the numeric keypad, the ISO key next to Left Shift, media keys).
    public static KeyName? FromScanCode(int code) => ByScanCode.TryGetValue(code, out var key) ? key : null;

    private const int Ext = 0x100;

    private static readonly Dictionary<KeyName, int> ScanCodes = new()
    {
        [KeyName.Escape] = 0x01, [KeyName.D1] = 0x02, [KeyName.D2] = 0x03, [KeyName.D3] = 0x04, [KeyName.D4] = 0x05,
        [KeyName.D5] = 0x06, [KeyName.D6] = 0x07, [KeyName.D7] = 0x08, [KeyName.D8] = 0x09, [KeyName.D9] = 0x0A,
        [KeyName.D0] = 0x0B, [KeyName.Minus] = 0x0C, [KeyName.Equal] = 0x0D, [KeyName.Backspace] = 0x0E, [KeyName.Tab] = 0x0F,
        [KeyName.Q] = 0x10, [KeyName.W] = 0x11, [KeyName.E] = 0x12, [KeyName.R] = 0x13, [KeyName.T] = 0x14,
        [KeyName.Y] = 0x15, [KeyName.U] = 0x16, [KeyName.I] = 0x17, [KeyName.O] = 0x18, [KeyName.P] = 0x19,
        [KeyName.LeftBracket] = 0x1A, [KeyName.RightBracket] = 0x1B, [KeyName.Enter] = 0x1C,
        [KeyName.A] = 0x1E, [KeyName.S] = 0x1F, [KeyName.D] = 0x20, [KeyName.F] = 0x21, [KeyName.G] = 0x22,
        [KeyName.H] = 0x23, [KeyName.J] = 0x24, [KeyName.K] = 0x25, [KeyName.L] = 0x26, [KeyName.Semicolon] = 0x27,
        [KeyName.Quote] = 0x28, [KeyName.Grave] = 0x29, [KeyName.Backslash] = 0x2B,
        [KeyName.Z] = 0x2C, [KeyName.X] = 0x2D, [KeyName.C] = 0x2E, [KeyName.V] = 0x2F, [KeyName.B] = 0x30,
        [KeyName.N] = 0x31, [KeyName.M] = 0x32, [KeyName.Comma] = 0x33, [KeyName.Period] = 0x34, [KeyName.Slash] = 0x35,
        [KeyName.Space] = 0x39,
        [KeyName.F1] = 0x3B, [KeyName.F2] = 0x3C, [KeyName.F3] = 0x3D, [KeyName.F4] = 0x3E, [KeyName.F5] = 0x3F,
        [KeyName.F6] = 0x40, [KeyName.F7] = 0x41, [KeyName.F8] = 0x42, [KeyName.F9] = 0x43, [KeyName.F10] = 0x44,
        [KeyName.F11] = 0x57, [KeyName.F12] = 0x58,
        [KeyName.F13] = 0x64, [KeyName.F14] = 0x65, [KeyName.F15] = 0x66, [KeyName.F16] = 0x67, [KeyName.F17] = 0x68,
        [KeyName.F18] = 0x69, [KeyName.F19] = 0x6A, [KeyName.F20] = 0x6B, [KeyName.F21] = 0x6C, [KeyName.F22] = 0x6D,
        [KeyName.F23] = 0x6E, [KeyName.F24] = 0x76,
        [KeyName.Home] = Ext | 0x47, [KeyName.Up] = Ext | 0x48, [KeyName.PageUp] = Ext | 0x49, [KeyName.Left] = Ext | 0x4B,
        [KeyName.Right] = Ext | 0x4D, [KeyName.End] = Ext | 0x4F, [KeyName.Down] = Ext | 0x50, [KeyName.PageDown] = Ext | 0x51,
        [KeyName.Delete] = Ext | 0x53,
    };

    private static readonly Dictionary<int, KeyName> ByScanCode = ScanCodes.ToDictionary(p => p.Value, p => p.Key);

    /// One modifier or group name, case-insensitive. The Mac's symbols aren't
    /// accepted on Windows (spec, Syntax).
    public static Modifiers? ParseModifier(string text) => text.ToLowerInvariant() switch
    {
        "ctrl" or "control" or "primary" => Modifiers.Ctrl,
        "alt" or "opt" or "option" => Modifiers.Alt,
        "shift" => Modifiers.Shift,
        "cmd" or "command" => Modifiers.Win,
        "hyper" => Modifiers.Hyper,
        "meh" => Modifiers.Meh,
        "cag" => Modifiers.Cag,
        _ => null,
    };

    /// Canonical names: the group name when the set is exactly a group, else
    /// Ctrl, Alt, Shift, Cmd in that order.
    public static IEnumerable<string> Names(this Modifiers modifiers)
    {
        switch (modifiers)
        {
            case Modifiers.Hyper: yield return "Hyper"; yield break;
            case Modifiers.Meh: yield return "Meh"; yield break;
            case Modifiers.Cag: yield return "CAG"; yield break;
        }
        if (modifiers.HasFlag(Modifiers.Ctrl)) yield return "Ctrl";
        if (modifiers.HasFlag(Modifiers.Alt)) yield return "Alt";
        if (modifiers.HasFlag(Modifiers.Shift)) yield return "Shift";
        if (modifiers.HasFlag(Modifiers.Win)) yield return "Cmd";
    }
}

/// Zero or more modifiers and one key: "Hyper+Space", "Ctrl+Alt+L", "E".
public readonly record struct Chord(Modifiers Modifiers, KeyName Key)
{
    public static Chord? Parse(string token)
    {
        var parts = token.Split('+');
        if (Keys.ParseKey(parts[^1]) is not { } key) return null;
        var modifiers = Modifiers.None;
        foreach (var part in parts[..^1])
        {
            if (Keys.ParseModifier(part) is not { } modifier) return null;
            modifiers |= modifier;
        }
        return new Chord(modifiers, key);
    }

    /// Windows takes these before any hook sees them: Win+L locks the PC, and
    /// Ctrl+Alt+Delete is the secure attention sequence (spec v4, Reserved chords).
    public bool IsReservedByWindows =>
        (Modifiers == Modifiers.Win && Key == KeyName.L) ||
        (Key == KeyName.Delete && Modifiers.HasFlag(Modifiers.Ctrl) && Modifiers.HasFlag(Modifiers.Alt));

    private static readonly KeyName[] OfficeKeys =
        [KeyName.W, KeyName.X, KeyName.P, KeyName.O, KeyName.T, KeyName.D, KeyName.N, KeyName.Y, KeyName.L];

    /// A chord Windows gives to Office: Hyper with W, X, P, O, T, D, N, Y or L (spec v4).
    public bool IsOfficeShortcut => Modifiers == Modifiers.Hyper && OfficeKeys.Contains(Key);

    public override string ToString() => string.Join("+", Modifiers.Names().Append(Key.Name()));
}

/// An entry's full key path: the leader chord, then zero or more step keys.
/// Two paths are equal when their canonical forms are, so "hyper+space a" and
/// "Hyper+Space A" are the same path.
public sealed class KeyPath : IEquatable<KeyPath>
{
    private readonly string _text;

    public KeyPath(Chord leader, IReadOnlyList<Chord> steps)
    {
        Leader = leader;
        Steps = steps;
        _text = string.Join(" ", new[] { leader }.Concat(steps));
    }

    public Chord Leader { get; }
    public IReadOnlyList<Chord> Steps { get; }

    public static KeyPath? Parse(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || Chord.Parse(tokens[0]) is not { } leader) return null;
        var steps = new List<Chord>();
        foreach (var token in tokens.Skip(1))
        {
            if (Chord.Parse(token) is not { } step) return null;
            steps.Add(step);
        }
        return new KeyPath(leader, steps);
    }

    public bool IsLeader => Steps.Count == 0;

    /// The path one level up, or null for a leader.
    public KeyPath? Parent => IsLeader ? null : new KeyPath(Leader, Steps.Take(Steps.Count - 1).ToList());

    /// The key that selects this entry inside its parent menu, or null for a leader.
    public Chord? LastStep => IsLeader ? null : Steps[^1];

    public override string ToString() => _text;
    public bool Equals(KeyPath? other) => other is not null && _text == other._text;
    public override bool Equals(object? obj) => Equals(obj as KeyPath);
    public override int GetHashCode() => _text.GetHashCode(StringComparison.Ordinal);
}
