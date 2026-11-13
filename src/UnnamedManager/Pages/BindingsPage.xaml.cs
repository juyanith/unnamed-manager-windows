using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core.Config;
using UnnamedManager.Tray;

namespace UnnamedManager.Pages;

/// One row of the tree: an entry with its key, title and action (S-038).
public sealed class BindingItem(BindingNode node, bool needsAction)
{
    public BindingNode Node { get; } = node;
    public string Key { get; } = BindingLabels.Key(node);
    public string Title { get; } = node.Title;
    public string Action { get; } = needsAction ? "No action yet" : BindingLabels.Action(node);
    public string Spoken { get; } = BindingLabels.Spoken(node, needsAction ? "no action yet" : null);
    public bool NeedsAction { get; } = needsAction;
    public bool IsMenu => Node.IsMenu && !NeedsAction;
    public List<BindingItem> Children { get; } = [];
}

/// The Bindings page: every leader, menu and binding of the draft in file order,
/// with the file's and the draft's errors above it. The tree runs in node mode, so
/// a reload keeps which menus you collapsed.
public sealed partial class BindingsPage : Page
{
    private readonly HashSet<KeyPath> _collapsed = [];

    public BindingsPage()
    {
        InitializeComponent();
        // Delete only from the tree, so it never fires in a text box (rule 19).
        DeleteKey.ScopeOwner = Tree;
        Loaded += (_, _) => { App.Config.Changed += Refresh; App.Editor.Changed += Refresh; App.Keys.Recorded += OnRecorded; Refresh(); };
        Unloaded += (_, _) =>
        {
            App.Config.Changed -= Refresh;
            App.Editor.Changed -= Refresh;
            App.Keys.Recorded -= OnRecorded;
            StopRecording();
        };
    }

    private static ConfigEditor Editor => App.Editor;

    /// Edits wait while the file has errors, so the file you're fixing is never written over.
    public static bool CanEdit => !App.Config.State.HasErrors;

    public IReadOnlyList<BindingItem> Items { get; private set; } = [];

    public BindingItem? Selected => Tree.SelectedNode?.Content as BindingItem;

    /// The row whose key is being recorded (S-040).
    public KeyPath? Recording { get; private set; }

    /// Shows the draft and every error.
    public void Refresh() => Refresh(Selected?.Node.Path);

    public void Refresh(KeyPath? select)
    {
        foreach (var node in Nodes(Tree.RootNodes))
        {
            var path = ((BindingItem)node.Content).Node.Path;
            if (node.IsExpanded) _collapsed.Remove(path); else _collapsed.Add(path);
        }
        Items = Editor.Draft.Tree().Select(Item).ToList();
        Tree.RootNodes.Clear();
        foreach (var item in Items) Tree.RootNodes.Add(NodeFor(item));
        var errors = App.Config.Errors.Concat(Editor.DraftErrors).ToList();
        Errors.IsOpen = errors.Count > 0;
        Errors.Title = App.Config.State.HasErrors ? "The config file has errors" : "These changes aren't saved yet";
        Errors.Message = App.Config.State.HasErrors ? "Fix them in the file, then reload. Editing here waits until it loads." : "";
        ErrorList.Children.Clear();
        foreach (var error in errors)
        {
            // Each error is a link to what it names (S-049).
            var link = new HyperlinkButton { Content = new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap }, Padding = new Thickness(0) };
            ToolTipService.SetToolTip(link, "Show what this error is about");
            link.Click += (_, _) => GoTo(error);
            ErrorList.Children.Add(link);
        }
        NewBindingButton.IsEnabled = NewMenuButton.IsEnabled = CanEdit;

        UndoButton.IsEnabled = CanEdit && Editor.UndoName is not null;
        RedoButton.IsEnabled = CanEdit && Editor.RedoName is not null;
        UndoButton.Label = Editor.UndoName is { } u ? $"Undo {u.ToLowerInvariant()}" : "Undo";
        RedoButton.Label = Editor.RedoName is { } r ? $"Redo {r.ToLowerInvariant()}" : "Redo";
        Select(select is null ? null : All().FirstOrDefault(i => i.Node.Path.Equals(select)));
    }

    private BindingItem Item(BindingNode node)
    {
        var item = new BindingItem(node, Editor.NeedsAction.Contains(node.Path));
        item.Children.AddRange(node.Children.Select(Item));
        return item;
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

    public BindingItem? Find(KeyPath path) => All().FirstOrDefault(i => i.Node.Path.Equals(path));

    /// Selects a row, opening the menus above it, and shows it in the form.
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
        DeleteButton.IsEnabled = CanEdit && item is not null;
        if (item is null) return;
        DetailTitle.Text = item.Title;
        DetailKeys.Text = item.Node.Path.ToString();
        KeyFieldText.Text = Recording?.Equals(item.Node.Path) == true ? "Press a key…" : item.Key;
        KeyField.IsEnabled = CanEdit;
        _showing = true;
        TitleBox.Text = item.Title;
        TitleBox.IsEnabled = ActionBox.IsEnabled = CanEdit;
        TitleError.Visibility = Visibility.Collapsed;
        ActionBox.Visibility = item.IsMenu ? Visibility.Collapsed : Visibility.Visible;
        ActionBox.ItemsSource = ActionChoices(Editor.Draft);
        ActionBox.SelectedItem = item.Node.Action;
        _showing = false;
        DetailAction.Text = item.Node.Action is { } id && Editor.Draft.Action(id) is { } action
            ? action.DisplayName
            : item.IsMenu ? $"Menu with {item.Children.Count} {(item.Children.Count == 1 ? "item" : "items")}" : "";
    }

    private bool _showing;

    /// Shows why a key can't be used under the key field, or hides the line.
    public void ShowKeyError(string? message)
    {
        KeyError.Text = message ?? "";
        KeyError.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public string? KeyErrorText => KeyError.Visibility == Visibility.Visible ? KeyError.Text : null;

    private void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args) =>
        ShowDetail((args.InvokedItem as TreeViewNode)?.Content as BindingItem);


    private void OnReload(object sender, RoutedEventArgs e) => App.Config.Reload();

    private void OnOpenFile(object sender, RoutedEventArgs e) => ConfigFiles.OpenFile(App.Config.Path);
}
