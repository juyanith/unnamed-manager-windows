using System.Text.RegularExpressions;

namespace UnnamedManager.Core.Config;

/// A menu or binding in the tree, with its children in file order.
public sealed record BindingNode(KeyPath Path, string Title, string? Action, string? Icon, IReadOnlyList<BindingNode> Children)
{
    public bool IsMenu => Action is null;

    /// The child a step key selects, if any.
    public BindingNode? Child(KeyName key) => Children.FirstOrDefault(c => c.Path.LastStep?.Key == key);
}

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
                childCount[parent] = childCount.GetValueOrDefault(parent) + 1;
                var step = path.LastStep!.Value;
                if (step.Modifiers != Modifiers.None)
                    Error(Rule.StepModifiers, $"{name} “{path}” uses modifiers on a step key. Only the leader can have modifiers.", subject);
                if (step.Key == cancelKey || step.Key == backKey)
                    Error(Rule.StepIsRuntimeKey, $"{name} “{path}” uses {step.Key.Name()}, which is the {(step.Key == cancelKey ? "cancel" : "back")} key.", subject);
            }
            if (entry.Action is { } action && config.Resolve(action) is null)
                Error(Rule.UndefinedAction, $"Binding “{path}” uses action “{action}”, which isn't defined.", subject);
        }
        foreach (var (entry, path) in parsed)
            if (entry.IsMenu && childCount.GetValueOrDefault(path) == 0)
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

    /// Builds the tree, keeping file order at every level.
    private static IReadOnlyList<BindingNode> Tree(List<(Entry Entry, KeyPath Path)> parsed)
    {
        BindingNode Node((Entry Entry, KeyPath Path) item) => new(item.Path, item.Entry.Title, item.Entry.Action, item.Entry.Icon,
            parsed.Where(p => Equals(p.Path.Parent, item.Path)).Select(Node).ToList());
        return parsed.Where(p => p.Path.IsLeader).Select(Node).ToList();
    }
}
