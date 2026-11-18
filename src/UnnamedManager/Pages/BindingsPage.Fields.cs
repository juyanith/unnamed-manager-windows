using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using UnnamedManager.Core.Config;

namespace UnnamedManager.Pages;

/// The Bindings page's title and action fields (S-041).
public sealed partial class BindingsPage
{
    private const string NewActionChoice = ConfigEdits.NewActionChoice;

    private static IReadOnlyList<string> ActionChoices(Config config) => config.ActionChoices();

    /// Rename (F2): the title field, with its text selected.
    public void FocusTitle()
    {
        if (Selected is null || !CanEdit) return;
        TitleBox.Focus(FocusState.Keyboard);
        TitleBox.SelectAll();
    }

    /// Sets the selected entry's title; an empty one isn't saved and says why.
    public void SetTitle(string title)
    {
        if (!CanEdit || Selected is not { } item) return;
        title = title.Trim();
        TitleError.Visibility = title.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (title.Length == 0 || title == item.Title) return;
        var path = item.Node.Path;
        Editor.Change("Rename", c => c.SetTitle(path, title));
        Refresh(path);
    }

    /// Sets the selected binding's action. "New action…" makes a Do Nothing action
    /// to fill in on the Actions page.
    public void SetAction(string choice)
    {
        if (!CanEdit || Selected is not { } item || choice == item.Node.Action) return;
        var path = item.Node.Path;
        if (choice == NewActionChoice)
        {
            var id = Editor.Draft.FreeActionId();
            Editor.Change("New action", c => c.SetActionDefinition(id, new BasicAction("noop")).SetAction(path, id));
        }
        else Editor.Change("Change action", c => c.SetAction(path, choice));
        Refresh(path);
    }

    public string? TitleErrorText => TitleError.Visibility == Visibility.Visible ? TitleError.Text : null;

    private void OnTitleLostFocus(object sender, RoutedEventArgs e) => SetTitle(TitleBox.Text);

    private void OnTitleKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) SetTitle(TitleBox.Text);
    }

    /// The Built in menu (spec v11): a submenu for each kind with values, then Fill,
    /// Minimize, Show Help and Do Nothing. Choosing one saves its name in the binding and
    /// nothing in `actions`. Numbered windows aren't listed, since they have no end.
    private void FillBuiltInMenu()
    {
        foreach (var group in BuiltInActions.Groups())
        {
            if (group.Entries.Count == 1)
            {
                BuiltInMenu.Items.Add(BuiltInItem(group.Name, group.Entries[0]));
                continue;
            }
            var sub = new MenuFlyoutSubItem { Text = group.Name };
            if (Core.Config.ActionGlyphs.For(group.Entries[0].Action) is { } glyph) sub.Icon = new FontIcon { Glyph = glyph };
            foreach (var entry in group.Entries)
                sub.Items.Add(BuiltInItem(BuiltInActions.ValueLabel(entry.Action) ?? group.Name, entry));
            BuiltInMenu.Items.Add(sub);
        }
    }

    private MenuFlyoutItem BuiltInItem(string text, BuiltInActions.Entry entry)
    {
        var item = new MenuFlyoutItem { Text = text, Tag = entry.Name };
        if (Core.Config.ActionGlyphs.For(entry.Action) is { } glyph) item.Icon = new FontIcon { Glyph = glyph };
        ToolTipService.SetToolTip(item, entry.Name);
        item.Click += (_, _) => SetAction(entry.Name);
        return item;
    }

    /// The Built in menu's groups and items, for the self-test.
    public IReadOnlyList<string> BuiltInMenuLabels() =>
        [.. BuiltInMenu.Items.Select(i => i is MenuFlyoutSubItem s ? $"{s.Text} ({s.Items.Count})" : ((MenuFlyoutItem)i).Text)];

    private void OnActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && ActionBox.SelectedItem is string choice) SetAction(choice);
    }

    private void OnRename(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        FocusTitle();
    }
}

/// Errors as links (S-049).
public sealed partial class BindingsPage
{
    /// Selects the entry an error names and focuses the field with the problem. An
    /// error about an action opens it on the Actions page; one the tree can't show
    /// opens the config file.
    public void GoTo(ConfigIssue issue)
    {
        if (issue.Subject is { } subject && KeyPath.Parse(subject) is { } path && Find(path) is { } item)
        {
            Select(item);
            // An error in the file is fixed in the file: editing here waits until it loads.
            if (!CanEdit) { Tree.Focus(FocusState.Keyboard); return; }
            switch (BindingLabels.FieldFor(issue))
            {
                case IssueField.Title: FocusTitle(); break;
                case IssueField.Action: ActionBox.Focus(FocusState.Keyboard); break;
                case IssueField.Key: KeyField.Focus(FocusState.Keyboard); break;
            }
            return;
        }
        if (issue.Subject is { } id && Editor.Draft.Action(id) is not null)
        {
            App.MainWindow.Show("actions");
            if (App.MainWindow.CurrentPage is ActionsPage actions) actions.Refresh(id);
            return;
        }
        Tray.ConfigFiles.OpenFile(App.Config.Path);
    }

    /// The error links above the tree, for the self-test.
    public IEnumerable<HyperlinkButton> ErrorLinks => ErrorList.Children.OfType<HyperlinkButton>();
}
