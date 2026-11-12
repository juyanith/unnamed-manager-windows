using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core.Config;
using UnnamedManager.Tray;

namespace UnnamedManager.Pages;

/// One row of the tree: an entry with its key, title and action (S-038).
public sealed class BindingItem(BindingNode node)
{
    public BindingNode Node { get; } = node;
    public string Key { get; } = BindingLabels.Key(node);
    public string Title { get; } = node.Title;
    public string Action { get; } = BindingLabels.Action(node);
    public string Spoken { get; } = BindingLabels.Spoken(node);
    public List<BindingItem> Children { get; } = node.Children.Select(c => new BindingItem(c)).ToList();
}

/// The Bindings page: every leader, menu and binding in file order, from the last
/// valid config, with the file's errors above it when it has any. The tree runs in
/// node mode, so a reload keeps which menus you collapsed.
public sealed partial class BindingsPage : Page
{
    private readonly HashSet<KeyPath> _collapsed = [];

    public BindingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { App.Config.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => App.Config.Changed -= Refresh;
    }

    public IReadOnlyList<BindingItem> Items { get; private set; } = [];

    public BindingItem? Selected => Tree.SelectedNode?.Content as BindingItem;

    /// Shows the config in use and the last read's errors.
    public void Refresh()
    {
        var selected = Selected?.Node.Path;
        foreach (var node in Nodes(Tree.RootNodes))
        {
            var path = ((BindingItem)node.Content).Node.Path;
            if (node.IsExpanded) _collapsed.Remove(path); else _collapsed.Add(path);
        }
        Items = (App.Config.Current?.Leaders ?? []).Select(n => new BindingItem(n)).ToList();
        Tree.RootNodes.Clear();
        foreach (var item in Items) Tree.RootNodes.Add(NodeFor(item));
        Errors.IsOpen = App.Config.Errors.Count > 0;
        ErrorList.Children.Clear();
        foreach (var error in App.Config.Errors)
            ErrorList.Children.Add(new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap });
        Select(selected is null ? null : All().FirstOrDefault(i => i.Node.Path.Equals(selected)));
    }

    private TreeViewNode NodeFor(BindingItem item)
    {
        var node = new TreeViewNode { Content = item, IsExpanded = !_collapsed.Contains(item.Node.Path) };
        foreach (var child in item.Children) node.Children.Add(NodeFor(child));
        return node;
    }

    private static IEnumerable<TreeViewNode> Nodes(IEnumerable<TreeViewNode> nodes) =>
        nodes.SelectMany(n => Nodes(n.Children).Prepend(n));

    /// Every row, parents before children.
    public IEnumerable<BindingItem> All() => Nodes(Tree.RootNodes).Select(n => (BindingItem)n.Content);

    /// Selects a row, opening the menus above it, and shows it in the detail card.
    public void Select(BindingItem? item)
    {
        var node = Nodes(Tree.RootNodes).FirstOrDefault(n => n.Content == item);
        for (var up = node?.Parent; up is not null; up = up.Parent) up.IsExpanded = true;
        Tree.SelectedNode = node;
        ShowDetail(node is null ? null : item);
    }

    private void ShowDetail(BindingItem? item)
    {
        Detail.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is null) return;
        DetailTitle.Text = item.Title;
        DetailKeys.Text = item.Node.Path.ToString();
        DetailAction.Text = item.Node.Action is { } id && App.Config.Current?.Config.Action(id) is { } action
            ? $"{action.DisplayName}: {id}"
            : item.Node.IsMenu ? $"Menu with {item.Children.Count} items" : item.Action;
    }

    private void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args) =>
        ShowDetail((args.InvokedItem as TreeViewNode)?.Content as BindingItem);

    private void OnReload(object sender, RoutedEventArgs e) => App.Config.Reload();

    private void OnOpenFile(object sender, RoutedEventArgs e) => ConfigFiles.OpenFile(App.Config.Path);
}
