namespace UnnamedManager.Core.Config;

public enum IssueField { None, Key, Title, Action }

/// What a row of the Bindings page says about an entry (S-038): its own key, its
/// title, and its action id, or "Menu".
public static class BindingLabels
{
    /// The key that picks the entry: the leader chord, or the last step.
    public static string Key(BindingNode node) => (node.Path.LastStep ?? node.Path.Leader).ToString();

    public static string Action(BindingNode node) => node.Action ?? (node.List is not null ? "Recent Windows" : node.Hold ? "Menu, hold to choose" : "Menu");

    /// What Narrator reads for the row: "key, title, action" (S-052).
    public static string Spoken(BindingNode node, string? action = null)
    {
        var chord = node.Path.LastStep ?? node.Path.Leader;
        var key = string.Join(" ", chord.Modifiers.Names().Append(chord.Key.SpokenName()));
        return $"{key}, {node.Title}, {action ?? Action(node)}";
    }

    /// The field of an entry an error is about, so clicking it can focus that field (S-049).
    public static IssueField FieldFor(ConfigIssue issue) => issue.Rule switch
    {
        Rule.EmptyTitle => IssueField.Title,
        Rule.UndefinedAction or null => IssueField.Action,
        Rule.DuplicatePath or Rule.BadKeys or Rule.StepModifiers or Rule.StepIsRuntimeKey or Rule.StepIsRepeatKey or Rule.ReservedChord
            or Rule.NoParent or Rule.ParentIsBinding or Rule.KeyNotOnPlatform or Rule.OfficeShortcut => IssueField.Key,
        _ => IssueField.None,
    };

    /// Every node in the tree, parents before children, in file order.
    public static IEnumerable<BindingNode> All(IEnumerable<BindingNode> nodes) =>
        nodes.SelectMany(n => All(n.Children).Prepend(n));
}
