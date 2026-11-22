using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core.Config;

namespace UnnamedManager.Pages;

/// The Bindings page's commands on entries: new, delete, move (S-039, S-042, S-043).
public sealed partial class BindingsPage
{
    /// The menu new entries go into: the selected menu, or the menu of a selected binding
    /// or list. The app fills a list, so new entries go beside it.
    private KeyPath? TargetMenu => Selected is not { } s ? null : s.IsMenu && s.Node.List is null ? s.Node.Path : s.Node.Path.Parent;

    /// New binding (Ctrl+N): at the end of the selected menu, with its key recorder
    /// on, or a direct binding with nothing selected (S-039).
    public void NewBinding()
    {
        if (!CanEdit) return;
        KeyPath added = null!;
        var parent = TargetMenu;
        Editor.Change("New binding", c => c.AddEntry(parent, "New binding", null, out added));
        Editor.NeedAction(added);
        StartRecording(added);
    }

    /// New menu (Ctrl+Shift+N): a menu in the selected menu, or a new leader.
    public void NewMenu()
    {
        if (!CanEdit) return;
        KeyPath added = null!;
        var parent = TargetMenu;
        Editor.Change("New menu", c => c.AddEntry(parent, "New menu", null, out added));
        StartRecording(added);
    }

    /// Delete: the entry and everything under it. A menu with entries asks first,
    /// saying how many go; Ctrl+Z brings them back (S-042).
    public async Task DeleteAsync()
    {
        if (!CanEdit || Selected is not { } item) return;
        var path = item.Node.Path;
        var under = BindingLabels.All(item.Node.Children).Count();
        if (under > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"Delete “{item.Title}”?",
                Content = $"The {under} {(under == 1 ? "entry" : "entries")} under it go too. You can undo this with Ctrl+Z.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = ActualTheme,
            };
            if (!await ConfirmAsync(dialog)) return;
        }
        var siblings = Editor.Draft.Children(path.Parent).ToList();
        var next = siblings.SkipWhile(p => !p.Equals(path)).Skip(1).FirstOrDefault() ?? path.Parent;
        Editor.Change("Delete", c => c.RemoveEntry(path, out _));
        Refresh(next);
    }

    /// Duplicate (Ctrl+D): a copy right after the entry, with everything under it, selected (S-087).
    public void Duplicate()
    {
        if (!CanEdit || Selected is null) return;
        SaveTitle();
        if (Selected is not { } item) return;
        var path = item.Node.Path;
        var added = path;
        Editor.Change("Duplicate", c => c.Duplicate(path, out added));
        Editor.Copied(path, added);
        Refresh(added);
    }

    private void OnDuplicate(object sender, RoutedEventArgs e) => Duplicate();

    /// Whether Sort alphabetically has anything to sort: a menu with keys you add, two or more (S-088).
    private static bool CanSort(BindingItem? item) => item is { IsMenu: true, Node.List: null, Children.Count: > 1 };

    /// Sort alphabetically (Ctrl+Shift+L): the menu's keys, in the window and the Guide.
    public void Sort()
    {
        if (!CanEdit || !CanSort(Selected)) return;
        SaveTitle();
        if (Selected is not { } item) return;
        var path = item.Node.Path;
        Editor.Change("Sort", c => c.SortChildren(path));
        Refresh(path);
    }

    private void OnSort(object sender, RoutedEventArgs e) => Sort();

    /// The dialog on screen, if any.
    public ContentDialog? Dialog { get; private set; }

    private bool? _answer;

    private async Task<bool> ConfirmAsync(ContentDialog dialog)
    {
        Dialog = dialog;
        _answer = null;
        var result = await dialog.ShowAsync();
        Dialog = null;
        return _answer ?? result == ContentDialogResult.Primary;
    }

    /// Answers the open dialog, for the self-test.
    public void AnswerDialog(bool yes)
    {
        _answer = yes;
        Dialog?.Hide();
    }

    /// Move up and Move down (Alt+Up, Alt+Down): one place among the entry's siblings.
    /// The order is the Guide's order (S-043).
    public void Move(int by)
    {
        if (!CanEdit || Selected is not { } item) return;
        var path = item.Node.Path;
        Editor.Change(by < 0 ? "Move up" : "Move down", c => c.Move(path, by));
        Refresh(path);
    }

    /// A drag in the tree: within its menu it moves; into another menu it snaps back,
    /// since that would change its key path. Returns whether it moved.
    public bool Dropped(BindingItem item, BindingItem? newParent, int index)
    {
        var path = item.Node.Path;
        if (!CanEdit || !Equals(newParent?.Node.Path, path.Parent)) { Refresh(path); return false; }
        var at = Editor.Draft.Children(path.Parent).ToList().IndexOf(path);
        if (index == at || at < 0) { Refresh(path); return false; }
        Editor.Change("Move", c => c.Move(path, index - at));
        Refresh(path);
        return true;
    }

    private void OnDragCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
    {
        if (args.Items.FirstOrDefault() is not TreeViewNode { Content: BindingItem item } node) return;
        var parent = args.NewParentItem as TreeViewNode;
        var siblings = parent?.Children ?? Tree.RootNodes;
        Dropped(item, parent?.Content as BindingItem, siblings.IndexOf(node));
    }

    private void OnMoveUp(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; Move(-1); }

    private void OnMoveDown(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; Move(1); }

    private async void OnDelete(object sender, RoutedEventArgs e) => await DeleteAsync();

    private void OnUndo(object sender, RoutedEventArgs e) { if (CanEdit) Editor.Undo(); }

    private void OnRedo(object sender, RoutedEventArgs e) { if (CanEdit) Editor.Redo(); }

    private void OnNewBinding(object sender, RoutedEventArgs e) => NewBinding();

    private void OnNewMenu(object sender, RoutedEventArgs e) => NewMenu();
}

/// The tree's context menu (S-052).
public sealed partial class BindingsPage
{
    /// A right-click selects the row under the pointer first, so the menu acts on it.
    private void OnTreeContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        for (var element = args.OriginalSource as DependencyObject; element is not null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is TreeViewItem item && Tree.NodeFromContainer(item) is { Content: BindingItem row })
            {
                Select(row);
                return;
            }
        }
    }

    private void OnRowMenuOpening(object sender, object e)
    {
        var item = Selected;
        foreach (var entry in RowMenu.Items.OfType<MenuFlyoutItem>())
            entry.IsEnabled = CanEdit && (item is not null || entry.Text is "New binding" or "New menu");
        if (item is null) return;
        var siblings = Editor.Draft.Children(item.Node.Path.Parent).ToList();
        var at = siblings.IndexOf(item.Node.Path);
        SortItem.IsEnabled &= CanSort(item);
        MoveUpItem.IsEnabled &= at > 0;
        MoveDownItem.IsEnabled &= at < siblings.Count - 1;
    }

    public TreeView TreeControl => Tree;

    /// The context menu's items, for the self-test.
    public IEnumerable<MenuFlyoutItem> MenuItems => RowMenu.Items.OfType<MenuFlyoutItem>();

    private void OnRenameItem(object sender, RoutedEventArgs e) => FocusTitle();

    private void OnMoveUpItem(object sender, RoutedEventArgs e) => Move(-1);

    private void OnMoveDownItem(object sender, RoutedEventArgs e) => Move(1);
}
