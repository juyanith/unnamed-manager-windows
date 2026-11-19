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
    /// A held menu opened, or its highlight moved (spec v18). `Highlight` is 0 for
    /// Cancel, and 1 for the menu's first child.
    public sealed record ShowHeld(BindingNode Menu, int Highlight, IReadOnlyList<string> Path, FrontApp? Front = null) : SequenceEffect;
    /// A list opened (spec v18): fill it with `Fill`, which shows it. Keys wait until then.
    public sealed record FillList(BindingNode Menu, IReadOnlyList<string> Path, FrontApp? Front = null) : SequenceEffect;
    /// A binding was picked, or a direct binding's chord pressed: run its action for
    /// `Front`. The sequence has ended. A list's item runs too, with `Binding.Item` set.
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

/// The sequence state machine (spec, Behavior both apps share, Held menus, Lists). Pure:
/// key events in, decisions out, no Win32 and no timers, so the hook callback stays
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

    /// The highlighted item while a held menu is held (spec v18): 0 is Cancel, 1 the
    /// first child. Null when the sequence isn't held.
    public int? Highlight { get; private set; }
    /// The held leader's modifiers. Letting go of any of them chooses.
    private Modifiers _holdModifiers;
    /// While a list waits for its items, the key-downs and modifier changes since it
    /// opened, replayed once it's filled. Null otherwise.
    private List<(KeyEvent? Key, Modifiers Held)>? _pending;
    /// The list waiting for its items opens held.
    private bool _pendingHeld;

    public bool IsActive => _stack.Count > 0 || _home is not null;

    /// True while a held menu waits for its modifiers to be let go.
    public bool IsHeld => Highlight is not null;

    /// The held leader's modifiers while held, for the app's check that they're still down.
    public Modifiers HoldModifiers => IsHeld || _pendingHeld && IsFilling ? _holdModifiers : Modifiers.None;

    /// True while a list waits for its items.
    public bool IsFilling => _pending is not null;

    /// The app that was in front when the leader was pressed. Actions go to it.
    public FrontApp? SequenceFront { get; private set; }

    /// The titles from the leader down: ["Commands", "Applications"]. A list with pages adds its page.
    public IReadOnlyList<string> Path => (_viaHome ? [Home.Title] : Array.Empty<string>()).Concat(_stack.SelectMany(m => m.PathTitles)).ToList();

    /// The menu the sequence is in, if any.
    public BindingNode? Current => _stack.Count > 0 ? _stack[^1] : null;

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
        Highlight = null;
        _pending = null;
        _pendingHeld = false;
    }

    /// The modifiers held changed. Once a leader modifier is let go, it stops being
    /// ignored. In a held menu, letting go of any of the leader's modifiers chooses: the
    /// set is compared, not the order, so a remapper's burst chooses the same (spec v18).
    public SequenceEffect? ModifiersChanged(Modifiers held)
    {
        // While a list fills, the change waits its turn with the keys.
        if (_pending is not null)
        {
            _pending.Add((null, held));
            return null;
        }
        _heldLeaderModifiers &= held;
        if (!IsHeld || (held & _holdModifiers) == _holdModifiers) return null;
        return Choose();
    }

    public SequenceOutput Handle(KeyEvent e)
    {
        if (!e.IsDown) return new SequenceOutput(_consumed.Remove(e.Code));
        if (!IsActive) return Idle(e);
        _consumed.Add(e.Code);
        if (e.IsRepeat) return new SequenceOutput(true);
        if (_pending is not null)
        {
            _pending.Add((e, e.Modifiers));
            return new SequenceOutput(true);
        }
        return Press(e);
    }

    private SequenceOutput Press(KeyEvent e) => _home is { } home ? Pick(home, e) : Step(e);

    /// Fills the list that's waiting with its items, shows it, and replays the keys
    /// pressed since it opened. Returns every effect, in order.
    public IReadOnlyList<SequenceEffect> Fill(IReadOnlyList<ListItem> items, ListItem? front)
    {
        if (_pending is not { } queued || Current is not { List: not null } list) return [];
        _pending = null;
        var filled = list.Filled(items, front);
        _stack[^1] = filled;
        var effects = new List<SequenceEffect> { _pendingHeld ? OpenHeld(filled) : new SequenceEffect.ShowMenu(filled, Path, SequenceFront) };
        _pendingHeld = false;
        for (var i = 0; i < queued.Count && IsActive; i++)
        {
            var (key, held) = queued[i];
            var effect = key is { } k ? Press(k).Effect : ModifiersChanged(held);
            if (effect is not null) effects.Add(effect);
            // Another list opened: the rest waits for it.
            if (_pending is not null)
            {
                _pending.AddRange(queued.Skip(i + 1));
                break;
            }
        }
        return effects;
    }

    /// An item's window went away while its list is up (spec v18): it leaves the list,
    /// the keys after it move up, and a highlight on it goes to the next item, else the
    /// one before, else Cancel.
    public SequenceEffect? RemoveItem(long id)
    {
        if (_pending is not null || Current is not { List: not null } list) return null;
        var index = list.Items.ToList().FindIndex(i => i.Id == id);
        if (index < 0) return null;
        var items = list.Items.Where((_, i) => i != index).ToList();
        var filled = list.Filled(items, list.Front);
        if (Highlight is not { } highlight)
        {
            _stack[^1] = filled.OnPage(list.Page);
            return new SequenceEffect.ShowMenu(_stack[^1], Path, SequenceFront);
        }
        // Items count from 0 across pages; null is Cancel.
        int? position = highlight == 0 ? null : list.Page * BindingNode.PageSize + highlight - 1;
        if (position is { } at && at >= index)
        {
            position = at == index && index < items.Count ? index : at - 1;
            if (position < 0) position = null;
        }
        filled = filled.OnPage(position is { } p ? p / BindingNode.PageSize : 0);
        _stack[^1] = filled;
        Highlight = position is { } q ? q % BindingNode.PageSize + 1 : 0;
        return new SequenceEffect.ShowHeld(filled, Highlight.Value, Path, SequenceFront);
    }

    /// Shows a menu that's just become the current one: held, or not. A list asks to be
    /// filled first.
    private SequenceEffect Open(BindingNode menu, bool held)
    {
        if (menu.List is not null)
        {
            _pending = [];
            _pendingHeld = held;
            Highlight = null;
            return new SequenceEffect.FillList(menu, Path, SequenceFront);
        }
        return held ? OpenHeld(menu) : new SequenceEffect.ShowMenu(menu, Path, SequenceFront);
    }

    /// Puts the highlight on a held menu's first child, or on Cancel when it has none.
    private SequenceEffect OpenHeld(BindingNode menu)
    {
        Highlight = menu.Children.Count == 0 ? 0 : 1;
        return new SequenceEffect.ShowHeld(menu, Highlight.Value, Path, SequenceFront);
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
        var held = leader.Hold && e.Modifiers != Modifiers.None;
        if (held) _holdModifiers = e.Modifiers;
        return new SequenceOutput(true, Open(leader, held));
    }

    /// Moves the highlight one item, wrapping through Cancel. In a list it goes across
    /// pages, and after the last item to Cancel on the first page.
    private SequenceEffect MoveHighlight(BindingNode menu, int step)
    {
        if (menu.List is null)
        {
            var count = menu.Children.Count + 1;
            Highlight = ((Highlight ?? 0) + step + count) % count;
            return new SequenceEffect.ShowHeld(menu, Highlight.Value, Path, SequenceFront);
        }
        // 0 is Cancel, then every item on every page.
        var all = menu.Items.Count + 1;
        var current = Highlight is null or 0 ? 0 : menu.Page * BindingNode.PageSize + Highlight.Value;
        var next = (current + step + all) % all;
        var page = next == 0 ? 0 : (next - 1) / BindingNode.PageSize;
        Highlight = next == 0 ? 0 : (next - 1) % BindingNode.PageSize + 1;
        return Turn(page, menu);
    }

    /// Shows another page of the list in front, keeping the highlight.
    private SequenceEffect Turn(int page, BindingNode menu)
    {
        var turned = menu.OnPage(page);
        _stack[^1] = turned;
        return Highlight is { } h ? new SequenceEffect.ShowHeld(turned, h, Path, SequenceFront) : new SequenceEffect.ShowMenu(turned, Path, SequenceFront);
    }

    /// A key while held (spec, Held menus): the repeat key, with Shift, Down and Up move
    /// the highlight; a binding's key moves it there; a menu's key opens that menu, held.
    private SequenceOutput StepHeld(BindingNode menu, KeyName key, Modifiers extra)
    {
        var reverse = extra == Modifiers.Shift && (_holdModifiers & Modifiers.Shift) == 0;
        if (key == menu.RepeatKey && (extra == Modifiers.None || reverse))
            return new SequenceOutput(true, MoveHighlight(menu, reverse ? -1 : 1));
        if (extra != Modifiers.None) return new SequenceOutput(true, new SequenceEffect.Unbound(Path));
        var index = menu.Children.ToList().FindIndex(c => c.Path.LastStep?.Key == key);
        if (index >= 0)
        {
            var child = menu.Children[index];
            if (child.IsMenu)
            {
                _stack.Add(child);
                return new SequenceOutput(true, Open(child, held: true));
            }
            Highlight = index + 1;
            return new SequenceOutput(true, new SequenceEffect.ShowHeld(menu, index + 1, Path, SequenceFront));
        }
        // More: the next page, its first item highlighted.
        if (key == KeyName.D0 && menu.HasMore)
        {
            Highlight = 1;
            return new SequenceOutput(true, Turn(menu.Page + 1, menu));
        }
        // An item on Down or Up wins over moving the highlight.
        if (key is KeyName.Down or KeyName.Up)
            return new SequenceOutput(true, MoveHighlight(menu, key == KeyName.Down ? 1 : -1));
        return new SequenceOutput(true, new SequenceEffect.Unbound(Path));
    }

    /// Letting go chose the highlighted item: a binding runs, a menu goes on as an
    /// ordinary menu, and Cancel ends the sequence.
    private SequenceEffect? Choose()
    {
        if (Current is not { } menu || Highlight is not { } index) return null;
        Highlight = null;
        if (index <= 0 || index > menu.Children.Count)
        {
            Reset();
            return new SequenceEffect.Cancelled();
        }
        var child = menu.Children[index - 1];
        if (child.IsMenu)
        {
            _stack.Add(child);
            return Open(child, held: false);
        }
        if (!Config.IsAvailable(child, SequenceFront)) return new SequenceEffect.Unavailable(child, Path);
        var front = SequenceFront;
        Reset();
        return new SequenceEffect.Run(child, front);
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
        if (extra == Modifiers.None && key == Config.BackKey && menu.List is not null && menu.Page > 0)
        {
            // Back goes to the page before.
            if (IsHeld) Highlight = 1;
            return new SequenceOutput(true, Turn(menu.Page - 1, menu));
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
                // Home isn't held.
                Highlight = null;
                return new SequenceOutput(true, new SequenceEffect.ShowHome(_home, Path, SequenceFront));
            }
            var parent = _stack[^1];
            if (IsHeld)
            {
                // The highlight goes on the menu you came from.
                Highlight = parent.Children.ToList().FindIndex(c => c.Path.Equals(leaving.Path)) + 1;
                if (Highlight == 0) Highlight = 1;
                return new SequenceOutput(true, new SequenceEffect.ShowHeld(parent, Highlight.Value, Path, SequenceFront));
            }
            return new SequenceOutput(true, new SequenceEffect.ShowMenu(parent, Path, SequenceFront));
        }
        if (IsHeld) return StepHeld(menu, key, extra);
        if (extra == Modifiers.None && key == KeyName.D0 && menu.HasMore) return new SequenceOutput(true, Turn(menu.Page + 1, menu));
        if (extra != Modifiers.None || menu.Child(key) is not { } child)
            return new SequenceOutput(true, new SequenceEffect.Unbound(Path));
        if (child.IsMenu)
        {
            _stack.Add(child);
            return new SequenceOutput(true, Open(child, held: false));
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
            // From Home a held leader's menu is an ordinary menu.
            return new SequenceOutput(true, Open(entry, held: false));
        }
        if (!Config.IsAvailable(entry, SequenceFront))
            return new SequenceOutput(true, new SequenceEffect.Unavailable(entry, Path));
        var front = SequenceFront;
        Reset();
        return new SequenceOutput(true, new SequenceEffect.Run(entry, front));
    }
}
