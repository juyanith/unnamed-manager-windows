using System.Text.RegularExpressions;

namespace UnnamedManager.Core.Config;

/// A menu or binding in the tree, with its children in file order.
public sealed record BindingNode(KeyPath Path, string Title, string? Action, string? Icon, IReadOnlyList<BindingNode> Children)
{
    /// A leader whose menus are held menus (spec v18).
    public bool Hold { get; init; }
    /// A list the app fills when it opens (spec v18). Its children are its items once filled.
    public ListSpec? List { get; init; }
    /// One item of a filled list: the window it goes to.
    public ListItem? Item { get; init; }
    /// On an item, the list it came from, which says what choosing it does (spec v19).
    public ListSpec? ItemOf { get; init; }
    /// In a filled list, the focused window, which Cancel shows (spec v18).
    public ListItem? Front { get; init; }
    /// Every item of a filled list, on every page.
    public IReadOnlyList<ListItem> Items { get; init; } = [];
    /// The page of a filled list its children show, from 0.
    public int Page { get; init; }

    /// A list's item has no action but runs, like a binding.
    public bool IsMenu => Action is null && Item is null;

    /// The keys of a list's items, 1 to 9, in order (spec v18).
    public static readonly KeyName[] ItemKeys = [KeyName.D1, KeyName.D2, KeyName.D3, KeyName.D4, KeyName.D5, KeyName.D6, KeyName.D7, KeyName.D8, KeyName.D9];
    public static int PageSize => ItemKeys.Length;
    public int PageCount => Math.Max(1, (Items.Count + PageSize - 1) / PageSize);
    /// A list with a page after this one has More, on key 0.
    public bool HasMore => List is not null && Page < PageCount - 1;

    /// The child a step key selects, if any.
    public BindingNode? Child(KeyName key) => Children.FirstOrDefault(c => c.Path.LastStep?.Key == key);

    /// The key that opened this menu: the leader chord's key, or its own step key. In a
    /// held menu it moves the highlight (spec v18).
    public KeyName RepeatKey => Path.LastStep?.Key ?? Path.Leader.Key;

    /// A list filled with `items`, on its first page.
    public BindingNode Filled(IReadOnlyList<ListItem> items, ListItem? front) => (this with { Items = items, Front = front }).OnPage(0);

    /// The list on another page: its children are that page's items, keys 1 to 9.
    public BindingNode OnPage(int page)
    {
        var p = Math.Clamp(page, 0, PageCount - 1);
        var start = p * PageSize;
        var children = Items.Skip(start).Take(PageSize).Select((item, i) =>
            new BindingNode(new KeyPath(Path.Leader, [.. Path.Steps, new Chord(Modifiers.None, ItemKeys[i])]), item.Title, null, null, []) { Item = item, ItemOf = List }).ToList();
        return this with { Page = p, Children = children };
    }

    /// Its titles in the path: a list with pages adds the page.
    public IEnumerable<string> PathTitles => List is not null && PageCount > 1 ? [Title, $"Page {Page + 1} of {PageCount}"] : [Title];
}

/// What the Guide marks on a list's item (spec v18).
[Flags]
public enum ItemMarks
{
    None = 0,
    Minimized = 1,
    OtherDesktop = 2,
    OtherDisplay = 4,
}

/// One item of a list, as the app found it when the list opened (spec v18). `Id` finds
/// the window again; `App` is its exe's path, for the icon.
public sealed record ListItem(long Id, string Title, string? App = null, ItemMarks Marks = ItemMarks.None);

/// A valid config, ready to use: the tree of leaders and direct bindings, and
/// the resolved runtime keys.
public sealed record LoadedConfig(Config Config, IReadOnlyList<BindingNode> Leaders, KeyName CancelKey, KeyName? BackKey, IReadOnlyList<ConfigIssue> Warnings)
{
    public BindingNode? Leader(Chord chord) => Leaders.FirstOrDefault(l => l.Path.Leader == chord);
}

/// Checks the rules that need the whole file (spec, Validation).
public static partial class ConfigValidator
{
    [GeneratedRegex(@"^[Uu]\+[0-9A-Fa-f]{4,6}$")]
    private static partial Regex CodePoint();

    [GeneratedRegex(@"^([A-Za-z]:[\\/]|\\\\|~)")]
    private static partial Regex FilePath();

    /// A Segoe Fluent Icons code point such as "U+E756", or a file path (spec v4).
    public static bool IsWindowsIcon(string icon) => CodePoint().IsMatch(icon) || FilePath().IsMatch(icon);

    /// The loaded config, or every error found, in file order.
    public static (LoadedConfig? Loaded, IReadOnlyList<ConfigIssue> Errors) Validate(Config config)
    {
        var errors = new List<ConfigIssue>();
        void Error(string rule, string message, string? subject = null) => errors.Add(new ConfigIssue(Severity.Error, message, subject, rule));

        if (config.Version != Config.SupportedVersion)
            Error(Rule.Version, $"Version {config.Version} isn't supported. Use \"version\": {Config.SupportedVersion}.");

        // Runtime keys, timing and the Guide's settings.
        var cancelKey = Keys.ParseKey(config.Runtime.CancelKey);
        if (cancelKey is null) Error(Rule.BadRuntimeKey, $"The cancel key “{config.Runtime.CancelKey}” isn't a key name.");
        KeyName? backKey = null;
        if (config.Runtime.BackKey != "")
        {
            backKey = Keys.ParseKey(config.Runtime.BackKey);
            if (backKey is null) Error(Rule.BadRuntimeKey, $"The back key “{config.Runtime.BackKey}” isn't a key name.");
        }
        if (cancelKey is not null && cancelKey == backKey)
            Error(Rule.SameCancelBack, $"The cancel key and the back key are both {cancelKey.Value.Name()}. Choose two different keys.");
        if (config.Runtime.HudDelayMs < 0) Error(Rule.NegativeDelay, "hudDelayMs can't be negative.");
        if (config.Runtime.TimeoutMs < 0) Error(Rule.NegativeDelay, "timeoutMs can't be negative.");
        if (!new[] { "center", "top", "bottom" }.Contains(config.UI.Position.ToLowerInvariant()))
            Error(Rule.BadUIValue, $"The position “{config.UI.Position}” isn't one the Guide knows. Use center, top or bottom.");
        if (!new[] { "system", "light", "dark" }.Contains(config.UI.Theme.ToLowerInvariant()))
            Error(Rule.BadUIValue, $"The theme “{config.UI.Theme}” isn't one the app knows. Use system, light or dark.");
        if (config.Windows.Gap < 0) Error(Rule.NegativeGap, "The gap between windows can't be negative.");
        foreach (var (name, value) in new[] { ("maxColumns", config.Windows.MaxColumns), ("maxRows", config.Windows.MaxRows) })
            if (value is < WindowSettings.MinLimit or > WindowSettings.MaxLimit)
                Error(Rule.BadWindowLimit, $"windows.{name} is {value}. Use a number from {WindowSettings.MinLimit} to {WindowSettings.MaxLimit}.");
        foreach (var (name, value) in new[] { ("minVisible", config.Windows.MinVisible), ("rowTolerance", config.Windows.RowTolerance), ("tieTolerance", config.Windows.TieTolerance) })
            if (value < 0) Error(Rule.NegativeWindowDistance, $"windows.{name} can't be negative.");
        if (config.Windows.Apps.Any(a => string.IsNullOrWhiteSpace(a.Key)))
            Error(Rule.BadAppId, "windows.apps has a blank app. Use an exe such as “Code.exe”, or an AppUserModelID.");

        // Entries: parse, find duplicates.
        if (config.Entries.Count == 0)
            Error(Rule.NoEntries, "The config has no entries. Add a leader and at least one binding, or a direct binding.");
        var parsed = new List<(Entry Entry, KeyPath Path)>();
        var seen = new Dictionary<KeyPath, string>();
        foreach (var entry in config.Entries)
        {
            if (KeyPath.Parse(entry.Keys) is not { } path)
            {
                Error(Rule.BadKeys, $"Entry “{entry.Keys}” has a key that isn't a known name. Check the spelling of each key and modifier.", entry.Keys);
                continue;
            }
            if (string.IsNullOrWhiteSpace(entry.Title)) Error(Rule.EmptyTitle, $"Entry “{path}” has an empty title.", path.ToString());
            if (entry.Icon is { } icon && !IsWindowsIcon(icon))
                Error(Rule.BadIcon, $"Entry “{path}” has the icon “{icon}”, which Windows can't show. Use a Segoe Fluent Icons code point such as “U+E756”, or a file path.", path.ToString());
            if (seen.TryGetValue(path, out var first))
            {
                Error(Rule.DuplicatePath, $"Entries “{first}” and “{entry.Keys}” are the same key path.", path.ToString());
                continue;
            }
            seen[path] = entry.Keys;
            parsed.Add((entry, path));
        }

        var byPath = parsed.ToDictionary(p => p.Path, p => p.Entry);
        var childCount = new Dictionary<KeyPath, int>();
        foreach (var (entry, path) in parsed)
        {
            var name = entry.IsMenu ? "Menu" : "Binding";
            var subject = path.ToString();
            if (path.IsLeader && path.Leader.IsReservedByWindows)
                Error(Rule.ReservedChord, $"{(entry.IsMenu ? "Leader" : "Direct binding")} “{path}” uses a chord Windows keeps for itself, so Unnamed Manager never sees it. Choose another key.", subject);
            if (path.Parent is { } parent)
            {
                if (!byPath.TryGetValue(parent, out var parentEntry))
                {
                    Error(Rule.NoParent, $"{name} “{path}” has no parent. Add an entry for “{parent}”.", subject);
                    continue;
                }
                if (!parentEntry.IsMenu)
                    Error(Rule.ParentIsBinding, $"{name} “{path}” is under “{parent}”, which is a binding. Only menus can have keys under them.", subject);
                else if (parentEntry.List is not null)
                    Error(Rule.ListHasChildren, $"{name} “{path}” is under “{parent}”, which is a list. The app fills a list itself, so remove this entry.", subject);
                childCount[parent] = childCount.GetValueOrDefault(parent) + 1;
                var step = path.LastStep!.Value;
                if (step.Modifiers != Modifiers.None)
                    Error(Rule.StepModifiers, $"{name} “{path}” uses modifiers on a step key. Only the leader can have modifiers.", subject);
                if (step.Key == cancelKey || step.Key == backKey)
                    Error(Rule.StepIsRuntimeKey, $"{name} “{path}” uses {step.Key.Name()}, which is the {(step.Key == cancelKey ? "cancel" : "back")} key.", subject);
                var repeatKey = parent.LastStep?.Key ?? parent.Leader.Key;
                if (Held(path) && step.Key == repeatKey)
                    Error(Rule.StepIsRepeatKey, $"{name} “{path}” uses {step.Key.Name()}, which moves the highlight in the held menu “{parent}”. Choose another key.", subject);
            }
            if (entry.Hold)
            {
                if (!path.IsLeader || !entry.IsMenu)
                    Error(Rule.HoldNotLeader, $"{name} “{path}” has Hold to choose, but only a leader can have it.", subject);
                else if (path.Leader.Modifiers == Modifiers.None)
                    Error(Rule.HoldNoModifiers, $"Leader “{path}” has Hold to choose but no modifiers, so there's nothing to let go of. Add a modifier, or turn Hold to choose off.", subject);
            }
            if (entry.Action is { } action && config.Resolve(action) is null)
                Error(Rule.UndefinedAction, $"Binding “{path}” uses action “{action}”, which isn't defined.", subject);
            if (entry.List is { } list) CheckList(list, path, entry.Action is not null, Held(path), Error);
        }
        // Whether an entry is under a leader with `hold` (spec v18).
        bool Held(KeyPath path) => byPath.TryGetValue(new KeyPath(path.Leader, []), out var leader) && leader.Hold;
        foreach (var (entry, path) in parsed)
            // A list is never empty: the app fills it.
            if (entry.IsMenu && entry.List is null && childCount.GetValueOrDefault(path) == 0)
                Error(Rule.EmptyMenu, $"Menu “{path}” has no keys under it. Add a binding or remove the menu.", path.ToString());

        // Contexts and Context Aware actions.
        var contextIds = new HashSet<string>();
        foreach (var context in config.Contexts)
        {
            if (!contextIds.Add(context.Id))
                Error(Rule.DuplicateContext, $"Two contexts are named “{context.Id}”. Give each context its own id.", context.Id);
            if (context.Apps.Any(string.IsNullOrWhiteSpace))
                Error(Rule.BadAppId, $"Context “{context.Id}” has a blank app. Use an exe such as “Code.exe”, or an AppUserModelID.", context.Id);
        }
        var usedContexts = new HashSet<string>();
        foreach (var (id, action) in config.Actions)
        {
            if (action is not ContextualAction contextual) continue;
            foreach (var (contextId, variant) in contextual.Variants)
            {
                usedContexts.Add(contextId);
                if (!contextIds.Contains(contextId))
                    Error(Rule.UndefinedContext, $"Action “{id}” has a variant for context “{contextId}”, which isn't defined.", id);
                if (variant is ContextualAction)
                    Error(Rule.NestedContextual, $"Action “{id}” has a Context Aware variant inside it. Variants must be other kinds of action.", id);
            }
            if (contextual.Fallback is ContextualAction)
                Error(Rule.NestedContextual, $"Action “{id}” has a Context Aware fallback. The fallback must be another kind of action.", id);
        }

        if (errors.Count > 0 || cancelKey is null) return (null, errors);

        var warnings = new List<ConfigIssue>();
        void Warn(string rule, string message, string subject) => warnings.Add(new ConfigIssue(Severity.Warning, message, subject, rule));
        var usedActions = config.Entries.Select(e => e.Action).OfType<string>().ToHashSet();
        foreach (var (id, _) in config.Actions)
            if (!usedActions.Contains(id)) Warn(Rule.UnusedAction, $"Action “{id}” isn't used by any binding.", id);
        foreach (var context in config.Contexts)
            if (!usedContexts.Contains(context.Id)) Warn(Rule.UnusedContext, $"Context “{context.Id}” isn't used by any action.", context.Id);
        foreach (var (entry, path) in parsed)
            if (path.IsLeader && path.Leader.IsOfficeShortcut)
                Warn(Rule.OfficeShortcut, $"Windows also opens an Office app on {path}. It works while Unnamed Manager runs; turn off Office shortcuts to free it for good.", path.ToString());

        return (new LoadedConfig(config, Tree(parsed), cancelKey.Value, backKey, warnings), []);
    }

    /// A list's own rules (spec v18, v19, Lists).
    private static void CheckList(ListSpec list, KeyPath path, bool hasAction, bool held, Action<string, string, string?> error)
    {
        var subject = path.ToString();
        if (hasAction)
            error(Rule.ConflictingFields, $"Entry “{path}” has both an action and a list. Use one: an action runs, a list is a menu the app fills.", subject);
        if (list.Type != ListSpec.RecentWindows)
        {
            error(Rule.UnknownListType, $"Entry “{path}” has a list of type “{list.Type}”, which isn't one the app knows. Use {ListSpec.RecentWindows}.", subject);
            return;
        }
        if (list.Count is < ListSpec.MinCount or > ListSpec.MaxCount)
            error(Rule.BadListValue, $"The list of “{path}” has count {list.Count}. Use a number from {ListSpec.MinCount} to {ListSpec.MaxCount}.", subject);
        if (list.Spaces is { } spaces && !ListSpec.SpacesValues.Contains(spaces))
            error(Rule.BadListValue, $"The list of “{path}” has spaces “{spaces}”. Use all or current.", subject);
        if (!ListSpec.ScopeValues.Contains(list.Scope))
            error(Rule.BadListValue, $"The list of “{path}” has scope “{list.Scope}”. Use all or display.", subject);
        if (!ListSpec.ChooseValues.Contains(list.Choose))
            error(Rule.BadListValue, $"The list of “{path}” has choose “{list.Choose}”. Use focus or swap.", subject);
        // Swap only works with windows shown on the current desktop (spec v19).
        var field = list.Minimized is not null ? "minimized" : list.Spaces is not null ? "spaces" : null;
        if (list.Swaps && field is not null)
            error(Rule.ConflictingFields, $"The list of “{path}” swaps, so “{field}” doesn't go with it: Swap only works with windows shown on the current desktop. Remove “{field}”.", subject);
        // The digits are the items' keys, so a held list can't be opened by one.
        var repeatKey = path.LastStep?.Key ?? path.Leader.Key;
        if (held && repeatKey is >= KeyName.D0 and <= KeyName.D9)
            error(Rule.StepIsRepeatKey, $"The held list “{path}” is opened by {repeatKey.Name()}, which is also one of its items' keys. Choose a key that isn't a digit.", subject);
    }

    /// Builds the tree, keeping file order at every level.
    private static IReadOnlyList<BindingNode> Tree(List<(Entry Entry, KeyPath Path)> parsed)
    {
        BindingNode Node((Entry Entry, KeyPath Path) item) => new(item.Path, item.Entry.Title, item.Entry.Action, item.Entry.Icon,
            parsed.Where(p => Equals(p.Path.Parent, item.Path)).Select(Node).ToList()) { Hold = item.Entry.Hold, List = item.Entry.List };
        return parsed.Where(p => p.Path.IsLeader).Select(Node).ToList();
    }
}
