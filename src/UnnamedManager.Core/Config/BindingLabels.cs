namespace UnnamedManager.Core.Config;

/// What a row of the Bindings page says about an entry (S-038): its own key, its
/// title, and its action id, or "Menu".
public static class BindingLabels
{
    /// The key that picks the entry: the leader chord, or the last step.
    public static string Key(BindingNode node) => (node.Path.LastStep ?? node.Path.Leader).ToString();

    public static string Action(BindingNode node) => node.Action ?? "Menu";

    /// What Narrator reads for the row: "key, title, action" (S-052).
    public static string Spoken(BindingNode node, string? action = null)
    {
        var chord = node.Path.LastStep ?? node.Path.Leader;
        var key = string.Join(" ", chord.Modifiers.Names().Append(chord.Key.SpokenName()));
        return $"{key}, {node.Title}, {action ?? Action(node)}";
    }

    /// Every node in the tree, parents before children, in file order.
    public static IEnumerable<BindingNode> All(IEnumerable<BindingNode> nodes) =>
        nodes.SelectMany(n => All(n.Children).Prepend(n));
}
