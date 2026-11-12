namespace UnnamedManager.Core.Config;

/// The draft the Bindings window and Settings edit (M5), ported from the Mac's
/// BindingsEditor. Every change is one undoable step. A draft that validates is
/// saved at once (S-048); one with errors stays on screen with its errors, and the
/// file isn't touched until they're fixed.
public sealed class ConfigEditor(Config saved, Func<Config, IReadOnlyList<ConfigIssue>> save)
{
    private readonly Stack<Step> _undo = new(), _redo = new();
    private readonly HashSet<KeyPath> _needsAction = [];

    private sealed record Step(string Name, Config Config, KeyPath[] NeedsAction);

    /// What's on screen.
    public Config Draft { get; private set; } = saved;

    /// What's in the file: the draft, unless the draft has errors.
    public Config Saved { get; private set; } = saved;

    /// Errors in the draft that stop it being saved.
    public IReadOnlyList<ConfigIssue> DraftErrors { get; private set; } = [];

    /// New bindings that don't have an action yet (S-039).
    public IReadOnlySet<KeyPath> NeedsAction => _needsAction;

    public string? UndoName => _undo.TryPeek(out var s) ? s.Name : null;
    public string? RedoName => _redo.TryPeek(out var s) ? s.Name : null;

    /// Raised after every change, undo, redo and reload.
    public event Action? Changed;

    /// Makes one change as one undoable step named `name`, then saves if it's valid.
    public void Change(string name, Func<Config, Config> edit)
    {
        var before = new Step(name, Draft, [.. _needsAction]);
        var after = edit(Draft);
        if (ReferenceEquals(after, Draft) || ConfigWriter.Write(after) == ConfigWriter.Write(Draft)) return;
        _undo.Push(before);
        _redo.Clear();
        Draft = after;
        Commit();
    }

    /// Marks a new binding that still needs an action, so its error says so.
    public void NeedAction(KeyPath path)
    {
        _needsAction.Add(path);
        Commit();
    }

    /// A key change moves a binding still waiting for its action along with it.
    public void Renamed(KeyPath from, KeyPath to)
    {
        if (_needsAction.Remove(from)) _needsAction.Add(to);
    }

    public bool Undo() => Swap(_undo, _redo);

    public bool Redo() => Swap(_redo, _undo);

    private bool Swap(Stack<Step> from, Stack<Step> to)
    {
        if (!from.TryPop(out var step)) return false;
        to.Push(new Step(step.Name, Draft, [.. _needsAction]));
        Draft = step.Config;
        _needsAction.Clear();
        _needsAction.UnionWith(step.NeedsAction);
        Commit();
        return true;
    }

    /// Takes the file's version when it changed on disk: the file is the source of
    /// truth, so edits that couldn't be saved yet go.
    public void Reload(Config fromFile)
    {
        var text = ConfigWriter.Write(fromFile);
        if (text == ConfigWriter.Write(Draft)) { Saved = fromFile; return; }
        if (text == ConfigWriter.Write(Saved)) return;
        Draft = Saved = fromFile;
        DraftErrors = [];
        _needsAction.Clear();
        Changed?.Invoke();
    }

    private void Commit()
    {
        _needsAction.RemoveWhere(p => Draft.Entry(p) is not { Action: null });
        var errors = _needsAction.Order(Comparer<KeyPath>.Create((a, b) => string.CompareOrdinal(a.ToString(), b.ToString())))
            .Select(p => new ConfigIssue(Severity.Error, $"Choose an action for “{p}”.", p.ToString())).ToList();
        // A new binding without its action isn't an empty menu.
        errors.AddRange(ConfigValidator.Validate(Draft).Errors.Where(e => !(e.Rule == Rule.EmptyMenu && _needsAction.Any(p => p.ToString() == e.Subject))));
        DraftErrors = errors;
        if (errors.Count == 0)
        {
            var failed = save(Draft);
            if (failed.Count == 0) Saved = Draft; else DraftErrors = failed;
        }
        Changed?.Invoke();
    }
}
