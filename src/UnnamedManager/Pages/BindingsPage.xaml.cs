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
        FillBuiltInMenu();
        // Delete only from the tree, so it never fires in a text box (rule 19).
        DeleteKey.ScopeOwner = Tree;
        Empty.Invoked += NewBinding;
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


    private static bool CanEdit => App.CanEdit;

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
        // Clear the selection while its node is still in the tree. Selecting a new node after
        // the old one is gone can crash TreeView (an access violation in SelectedNode).
        _selecting = true;
        Tree.SelectedNode = null;
        Tree.RootNodes.Clear();
        foreach (var item in Items) Tree.RootNodes.Add(NodeFor(item));
        _selecting = false;
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
        ShowEmpty(Items.Count == 0);

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
        _selecting = true;
        Tree.SelectedNode = node;
        ShowDetail(node is null ? null : item);
        _selecting = false;
    }

    /// The empty state instead of the tree and the form (S-053).
    private void ShowEmpty(bool empty)
    {
        Empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ListCard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public UI.EmptyState EmptyState => Empty;

    private void ShowDetail(BindingItem? item)
    {
        // First: the tree can raise a selection's event late, part-way through here.
        _shownRow = item?.Node.Path;
        Detail.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.IsEnabled = DuplicateButton.IsEnabled = CanEdit && item is not null;
        SortButton.IsEnabled = CanEdit && CanSort(item);
        if (item is null) { _titleShown = null; return; }
        DetailTitle.Text = item.Title;
        DetailKeys.Text = item.Node.Path.ToString();
        KeyFieldText.Text = Recording?.Equals(item.Node.Path) == true ? "Press a key…" : item.Key;
        KeyField.IsEnabled = CanEdit;
        KeyHelp.Text = BindingLabels.KeyHelp(item.Node, item.NeedsAction);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(KeyField, KeyHelp.Text);
        _showing = true;
        // Before Text: a one-line box cuts it at the first line break.
        TitleBox.AcceptsReturn = item.Title.AsSpan().IndexOfAny('\r', '\n') >= 0;
        TitleBox.Text = item.Title;
        _titleShown = (item.Node.Path, TitleBox.Text);
        TitleBox.IsEnabled = ActionBox.IsEnabled = BuiltInButton.IsEnabled = CanEdit;
        TitleError.Visibility = Visibility.Collapsed;
        ActionBox.Visibility = BuiltInButton.Visibility = item.IsMenu ? Visibility.Collapsed : Visibility.Visible;
        // A built-in action (spec v11) isn't in `actions`, so it joins the list while a binding uses it.
        var choices = ActionChoices(Editor.Draft);
        ActionBox.ItemsSource = item.Node.Action is { } used && !choices.Contains(used) ? [used, .. choices] : choices;
        ActionBox.SelectedItem = item.Node.Action;
        ShowListFields(item);
        _showing = false;
        DetailAction.Text = item.Node.Action is { } id && Editor.Draft.Resolve(id) is { } action
            ? Editor.Draft.Action(id) is null ? $"{BuiltInActions.Label(action)}, built in"
                : BuiltInActions.IsBuiltIn(id) ? $"{action.DisplayName}. Overrides the built-in action of the same name." : action.DisplayName
            : item.Node.List is not null ? "A list of the windows you used last, filled when it opens."
            : item.IsMenu ? $"Menu with {item.Children.Count} {(item.Children.Count == 1 ? "item" : "items")}" : "";
    }

    /// The line under the Action list, for the self-test.
    public string DetailActionText => DetailAction.Text;

    private bool _showing;

    /// Shows why a key can't be used under the key field, or hides the line.
    public void ShowKeyError(string? message)
    {
        KeyError.Text = message ?? "";
        KeyError.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public string? KeyErrorText => KeyError.Visibility == Visibility.Visible ? KeyError.Text : null;

    /// True while the page itself rebuilds the tree or selects a row, and shows it itself.
    private bool _selecting;

    /// The row on the card.
    private KeyPath? _shownRow;

    /// The arrow keys and screen readers move the selection without invoking the row.
    private void OnTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (!_selecting) ShowRow(Selected);
    }

    private void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args) =>
        ShowRow((args.InvokedItem as TreeViewNode)?.Content as BindingItem);

    /// Shows the row you moved to. What you typed or were recording was for the row before.
    private void ShowRow(BindingItem? item)
    {
        if (Equals(item?.Node.Path, _shownRow)) return;
        KeepTypedTitle();
        StopRecording();
        ShowDetail(item);
    }


    private void OnReload(object sender, RoutedEventArgs e) => App.Config.Reload();

    private void OnOpenFile(object sender, RoutedEventArgs e) => ConfigFiles.OpenFile(App.Config.Path);
}
