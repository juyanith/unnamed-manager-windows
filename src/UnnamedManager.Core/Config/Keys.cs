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
