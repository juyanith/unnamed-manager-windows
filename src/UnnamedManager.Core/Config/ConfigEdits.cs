namespace UnnamedManager.Core.Config;

/// Why a key can't be an entry's key.
public enum KeyProblem { None, Taken, CancelOrBack, ModifierInStep, ReservedByWindows }

/// The edits the Bindings window makes (M5), ported from the Mac's ConfigDocument.
/// Each takes a config and returns a new one; the entry list changes as little as it
/// can, so entries you didn't touch keep their place in the file.
public static class ConfigEdits
{
    /// The entries as a tree, without validating: what the Bindings page shows of a
    /// draft that may not load yet. Entries whose keys don't parse are left out.
    public static IReadOnlyList<BindingNode> Tree(this Config config)
    {
        var parsed = config.Entries.Select(e => (Entry: e, Path: KeyPath.Parse(e.Keys))).Where(p => p.Path is not null).ToList();
        List<BindingNode> Under(KeyPath? parent) => parsed
            .Where(p => Equals(p.Path!.Parent, parent))
            .Select(p => new BindingNode(p.Path!, p.Entry.Title, p.Entry.Action, p.Entry.Icon, Under(p.Path)))
            .ToList();
        return Under(null);
    }

    public static int IndexOf(this Config config, KeyPath path) =>
        config.Entries.ToList().FindIndex(e => KeyPath.Parse(e.Keys) is { } p && p.Equals(path));

    public static Entry? Entry(this Config config, KeyPath path) => config.IndexOf(path) is >= 0 and var i ? config.Entries[i] : null;

    /// The direct children of a menu, or the leaders and direct bindings for null, in file order.
    public static IReadOnlyList<KeyPath> Children(this Config config, KeyPath? parent) =>
        config.Entries.Select(e => KeyPath.Parse(e.Keys)).Where(p => p is not null && Equals(p.Parent, parent)).Select(p => p!).ToList();

    public static bool IsUnder(this KeyPath path, KeyPath ancestor) =>
        path.Leader == ancestor.Leader && path.Steps.Count > ancestor.Steps.Count && path.Steps.Take(ancestor.Steps.Count).SequenceEqual(ancestor.Steps);

    /// Indexes of an entry and everything under it, in file order.
    private static List<int> Block(Config config, KeyPath path) =>
        config.Entries.Select((e, i) => (Path: KeyPath.Parse(e.Keys), i)).Where(p => p.Path is not null && (p.Path.Equals(path) || p.Path.IsUnder(path))).Select(p => p.i).ToList();

    /// This path with its own key (the last step, or the leader) replaced.
    public static KeyPath WithKey(this KeyPath path, Chord key) =>
        path.IsLeader ? new KeyPath(key, []) : new KeyPath(path.Leader, [.. path.Steps.Take(path.Steps.Count - 1), key]);

    private static KeyPath Replace(KeyPath path, KeyPath old, KeyPath @new) =>
        path.Equals(old) || path.IsUnder(old) ? new KeyPath(@new.Leader, [.. @new.Steps, .. path.Steps.Skip(old.Steps.Count)]) : path;

    private static Config With(Config config, List<Entry> entries) => config with { Entries = entries };

    /// The first key no sibling has: A to Z, then 0 to 9, never the cancel or back
    /// key. A new leader gets Hyper with a letter.
    public static KeyPath FreePath(this Config config, KeyPath? parent)
    {
        var taken = config.Children(parent).Where(p => parent is not null || p.Leader.Modifiers == Modifiers.Hyper)
            .Select(p => (p.LastStep ?? p.Leader).Key).ToHashSet();
        var reserved = parent is null ? [] : RuntimeKeys(config);
        var candidates = Enumerable.Range(0, 26).Select(i => Keys.ParseKey(((char)('A' + i)).ToString())!.Value)
            .Concat(Enumerable.Range(0, 10).Select(i => Keys.ParseKey(((i + 1) % 10).ToString())!.Value));
        var key = candidates.FirstOrDefault(k => !taken.Contains(k) && !reserved.Contains(k) && !(parent is null && new Chord(Modifiers.Hyper, k).IsOfficeShortcut), KeyName.F13);
        return parent is null ? new KeyPath(new Chord(Modifiers.Hyper, key), []) : new KeyPath(parent.Leader, [.. parent.Steps, new Chord(Modifiers.None, key)]);
    }

    private static HashSet<KeyName> RuntimeKeys(Config config) =>
        new[] { config.Runtime.CancelKey, config.Runtime.BackKey }.Select(Keys.ParseKey).OfType<KeyName>().ToHashSet();

    /// Adds a menu (no action) or a binding at the end of a menu, or as a leader or
    /// direct binding for null, with the first free key.
    public static Config AddEntry(this Config config, KeyPath? parent, string title, string? action, out KeyPath added)
    {
        added = config.FreePath(parent);
        var entry = new Entry(added.ToString(), title, action);
        var entries = config.Entries.ToList();
        var block = parent is null ? [] : Block(config, parent);
        if (block.Count > 0) entries.Insert(block[^1] + 1, entry); else entries.Add(entry);
        return With(config, entries);
    }

    /// Removes an entry and everything under it. `removed` says how many entries went.
    public static Config RemoveEntry(this Config config, KeyPath path, out int removed)
    {
        var block = Block(config, path);
        removed = block.Count;
        return With(config, config.Entries.Where((_, i) => !block.Contains(i)).ToList());
    }

    public static Config SetTitle(this Config config, KeyPath path, string title) => Update(config, path, e => e with { Title = title });

    public static Config SetAction(this Config config, KeyPath path, string? action) => Update(config, path, e => e with { Action = action });

    private static Config Update(Config config, KeyPath path, Func<Entry, Entry> change)
    {
        var i = config.IndexOf(path);
        if (i < 0) return config;
        var entries = config.Entries.ToList();
        entries[i] = change(entries[i]);
        return With(config, entries);
    }

    /// Why `key` can't be this entry's own key (S-039, S-040).
    public static KeyProblem CheckKey(this Config config, KeyPath path, Chord key)
    {
        if (!path.IsLeader && key.Modifiers != Modifiers.None) return KeyProblem.ModifierInStep;
        if (path.IsLeader && key.IsReservedByWindows) return KeyProblem.ReservedByWindows;
        if (!path.IsLeader && RuntimeKeys(config).Contains(key.Key)) return KeyProblem.CancelOrBack;
        var renamed = path.WithKey(key);
        return !renamed.Equals(path) && config.Children(path.Parent).Contains(renamed) ? KeyProblem.Taken : KeyProblem.None;
    }

    /// Changes an entry's own key, and every path under it. Unchanged when the key can't be used.
    public static Config ChangeKey(this Config config, KeyPath path, Chord key, out KeyPath renamed)
    {
        renamed = path;
        if (config.CheckKey(path, key) != KeyProblem.None) return config;
        var to = renamed = path.WithKey(key);
        var block = Block(config, path);
        var entries = config.Entries.ToList();
        foreach (var i in block) entries[i] = entries[i] with { Keys = Replace(KeyPath.Parse(entries[i].Keys)!, path, to).ToString() };
        return With(config, entries);
    }

    /// Moves an entry, with everything under it, one place up or down among its
    /// siblings (S-043). It never leaves its menu, which would change its key path.
    public static Config Move(this Config config, KeyPath path, int by)
    {
        var siblings = config.Children(path.Parent).ToList();
        var at = siblings.IndexOf(path);
        var to = at + by;
        if (at < 0 || to < 0 || to >= siblings.Count) return config;
        siblings.RemoveAt(at);
        siblings.Insert(to, path);
        // Each sibling's block goes to the end of the menu, in the new order.
        foreach (var sibling in siblings) config = MoveToEnd(config, sibling);
        return config;
    }

    private static Config MoveToEnd(Config config, KeyPath path)
    {
        var block = Block(config, path);
        var moving = block.Select(i => config.Entries[i]).ToList();
        var entries = config.Entries.Where((_, i) => !block.Contains(i)).ToList();
        var rest = With(config, entries);
        var parentBlock = path.Parent is { } parent ? Block(rest, parent) : [];
        entries.InsertRange(parentBlock.Count > 0 ? parentBlock[^1] + 1 : entries.Count, moving);
        return With(config, entries);
    }

    // Actions (S-044)

    /// Bindings that use an action, in file order.
    public static IReadOnlyList<KeyPath> Users(this Config config, string actionId) =>
        config.Entries.Where(e => e.Action == actionId).Select(e => KeyPath.Parse(e.Keys)).OfType<KeyPath>().ToList();

    /// "new-action", then "new-action-2" and so on.
    public static string FreeActionId(this Config config, string start = "new-action")
    {
        var id = start;
        for (var n = 2; config.Action(id) is not null; n++) id = $"{start}-{n}";
        return id;
    }

    /// Adds or replaces an action, keeping its place in the file.
    public static Config SetActionDefinition(this Config config, string id, ConfigAction action)
    {
        var actions = config.Actions.ToList();
        var i = actions.FindIndex(a => a.Key == id);
        if (i >= 0) actions[i] = KeyValuePair.Create(id, action); else actions.Add(KeyValuePair.Create(id, action));
        return config with { Actions = actions };
    }

    /// Renames an action and every binding that uses it. Unchanged when the new id is taken.
    public static Config RenameAction(this Config config, string id, string newId)
    {
        if (id == newId || config.Action(id) is null || config.Action(newId) is not null) return config;
        return config with
        {
            Actions = config.Actions.Select(a => a.Key == id ? KeyValuePair.Create(newId, a.Value) : a).ToList(),
            Entries = config.Entries.Select(e => e.Action == id ? e with { Action = newId } : e).ToList(),
        };
    }

    /// Deletes an action no binding uses; unchanged while one does. An override of a
    /// built-in action can go while in use: its bindings get the built-in one back (spec v11).
    public static Config RemoveAction(this Config config, string id) =>
        config.Users(id).Count > 0 && !BuiltInActions.IsBuiltIn(id) ? config : config with { Actions = config.Actions.Where(a => a.Key != id).ToList() };

    // Contexts (S-046)

    /// "new-context", then "new-context-2" and so on.
    public static string FreeContextId(this Config config)
    {
        var id = "new-context";
        for (var n = 2; config.Contexts.Any(c => c.Id == id); n++) id = $"new-context-{n}";
        return id;
    }

    public static Config AddContext(this Config config, string id) => config with { Contexts = [.. config.Contexts, new ConfigContext(id, [])] };

    public static Config SetContextApps(this Config config, string id, IReadOnlyList<string> apps) =>
        config with { Contexts = config.Contexts.Select(c => c.Id == id ? c with { Apps = apps } : c).ToList() };

    /// The Context Aware actions with a variant for a context.
    public static IReadOnlyList<string> ContextUsers(this Config config, string id) =>
        config.Actions.Where(a => a.Value is ContextualAction c && c.Variants.Any(v => v.Key == id)).Select(a => a.Key).ToList();

    /// Renames a context and the variants that name it. Unchanged when the new id is taken.
    public static Config RenameContext(this Config config, string id, string newId)
    {
        if (id == newId || config.Contexts.All(c => c.Id != id) || config.Contexts.Any(c => c.Id == newId)) return config;
        return config with
        {
            Contexts = config.Contexts.Select(c => c.Id == id ? c with { Id = newId } : c).ToList(),
            Actions = config.Actions.Select(a => a.Value is ContextualAction ctx && ctx.Variants.Any(v => v.Key == id)
                ? KeyValuePair.Create(a.Key, (ConfigAction)(ctx with { Variants = ctx.Variants.Select(v => v.Key == id ? KeyValuePair.Create(newId, v.Value) : v).ToList() }))
                : a).ToList(),
        };
    }

    /// Deletes a context no Context Aware action uses; unchanged while one does.
    public static Config RemoveContext(this Config config, string id) =>
        config.ContextUsers(id).Count > 0 ? config : config with { Contexts = config.Contexts.Where(c => c.Id != id).ToList() };

    /// Moves a context one place up or down. The order decides which variant wins.
    public static Config MoveContext(this Config config, string id, int by)
    {
        var contexts = config.Contexts.ToList();
        var at = contexts.FindIndex(c => c.Id == id);
        if (at < 0 || at + by < 0 || at + by >= contexts.Count) return config;
        var context = contexts[at];
        contexts.RemoveAt(at);
        contexts.Insert(at + by, context);
        return config with { Contexts = contexts };
    }

    /// The last item of a binding's Action list, which makes a new action.
    public const string NewActionChoice = "New action…";

    /// A binding's Action list: every action alphabetically, then "New action…" (S-041).
    public static IReadOnlyList<string> ActionChoices(this Config config) => [.. config.SortedActionIds(), NewActionChoice];

    /// Action ids alphabetically for lists: case doesn't matter and numbers sort by
    /// value, so "open-2" comes before "open-10" (S-041).
    public static IReadOnlyList<string> SortedActionIds(this Config config) =>
        config.Actions.Select(a => a.Key).Order(NaturalOrder.Instance).ToList();
}

/// Case-insensitive, with runs of digits compared by value.
public sealed class NaturalOrder : IComparer<string>
{
    public static NaturalOrder Instance { get; } = new();

    public int Compare(string? a, string? b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0'); var nb = b[sj..j].TrimStart('0');
                var byValue = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (byValue != 0) return byValue;
                continue;
            }
            var c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
            if (c != 0) return c;
            i++; j++;
        }
        var length = (a.Length - i).CompareTo(b.Length - j);
        return length != 0 ? length : string.CompareOrdinal(a, b);
    }
}
