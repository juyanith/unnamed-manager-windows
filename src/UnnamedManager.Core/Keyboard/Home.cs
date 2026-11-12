using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Keyboard;

/// Home: the menu above the leaders, listing every leader and direct binding in groups
/// by their modifiers (S-021, a port of the Mac's S-050).
public sealed class Home
{
    /// The top-level entries that share exactly these modifiers, in file order.
    public sealed record Group(Modifiers Modifiers, IReadOnlyList<BindingNode> Entries)
    {
        /// "Hyper", "Meh", "Shift+Cmd", or "No modifiers".
        public string Title => Modifiers == Modifiers.None ? "No modifiers" : string.Join("+", Modifiers.Names());

        public BindingNode? Entry(KeyName key) => Entries.FirstOrDefault(e => e.Path.Leader.Key == key);
    }

    /// The word for Home in the Guide's path.
    public const string Title = "Home";

    /// Groups the top-level entries, the one with `current` modifiers first, then the
    /// others in order of their first entry.
    public Home(IReadOnlyList<BindingNode> leaders, Modifiers current)
    {
        var groups = new List<(Modifiers Modifiers, List<BindingNode> Entries)>();
        foreach (var leader in leaders)
        {
            var modifiers = leader.Path.Leader.Modifiers;
            var index = groups.FindIndex(g => g.Modifiers == modifiers);
            if (index < 0) groups.Add((modifiers, [leader]));
            else groups[index].Entries.Add(leader);
        }
        var currentIndex = groups.FindIndex(g => g.Modifiers == current);
        if (currentIndex > 0)
        {
            var group = groups[currentIndex];
            groups.RemoveAt(currentIndex);
            groups.Insert(0, group);
        }
        Groups = groups.Select(g => new Group(g.Modifiers, g.Entries)).ToList();
    }

    public IReadOnlyList<Group> Groups { get; }

    /// The group of the leader you came from.
    public Group? Current => Groups.Count > 0 ? Groups[0] : null;

    /// The entry a press picks, or null for a key that isn't bound. In the current group
    /// the key alone picks, with some, all or none of its modifiers held. Any other entry
    /// needs its exact chord. A bare key prefers the current group over the group with no
    /// modifiers.
    public BindingNode? Entry(Chord chord)
    {
        var exact = Groups.SelectMany(g => g.Entries).FirstOrDefault(e => e.Path.Leader == chord);
        BindingNode? inCurrent = null;
        if (Current is { } current && (chord.Modifiers & ~current.Modifiers) == Modifiers.None)
            inCurrent = current.Entry(chord.Key);
        return chord.Modifiers == Modifiers.None ? inCurrent ?? exact : exact ?? inCurrent;
    }
}
