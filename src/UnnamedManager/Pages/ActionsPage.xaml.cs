using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using UnnamedManager.Actions;
using UnnamedManager.Core.Config;
using UnnamedManager.UI;

namespace UnnamedManager.Pages;

/// One row of the Actions list: its id, its type and how many bindings use it.
public sealed class ActionItem(string id, ConfigAction action, int users)
{
    public string Id { get; } = id;
    public ConfigAction Action { get; } = action;
    public int Users { get; } = users;
    public string Detail { get; } = $"{action.DisplayName} · {(users == 1 ? "1 binding" : $"{users} bindings")}{(BuiltInActions.IsBuiltIn(id) ? " · override" : "")}";
    public string Spoken => $"{Id}, {Detail}";
}

/// The Actions page (S-044): every action alphabetically, and the selected one's form,
/// which changes with its type. Each finished field is one undoable change.
public sealed partial class ActionsPage : Page
{
    private readonly ActionFormView _form;
    private bool _showing;

    public ActionsPage()
    {
        InitializeComponent();
        _form = new ActionFormView(FieldPanel);
        _form.Edited += Apply;
        Empty.Invoked += New;
        DeleteKey.ScopeOwner = ActionList;
        foreach (var kind in ActionForms.Types()) TypeBox.Items.Add(new ComboBoxItem { Content = kind.DisplayName, Tag = kind.Type });
        Loaded += (_, _) => { App.Editor.Changed += Refresh; App.Config.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { App.Editor.Changed -= Refresh; App.Config.Changed -= Refresh; };
    }

    private static ConfigEditor Editor => App.Editor;

    public UI.EmptyState EmptyState => Empty;

    private static bool CanEdit => App.CanEdit;

    public IReadOnlyList<ActionItem> Items { get; private set; } = [];

    public ActionItem? Selected => ActionList.SelectedItem as ActionItem;

    /// Selects an action by id, for the self-test.
    public void Select(string id) => ActionList.SelectedItem = Items.FirstOrDefault(i => i.Id == id);

    /// The selected action's line in the list, for the self-test.
    public string? SelectedDetail => Selected?.Detail;

    /// The type the form shows, which may not be saved yet.
    public string? FormType => (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string;

    public ActionFormView Form => _form;

    /// Picks a type in the Type list, as a click would.
    public void SetType(string type) =>
        TypeBox.SelectedItem = TypeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == type);

    /// The names in the Type list, for the self-test.
    public IReadOnlyList<string> TypeNames() => [.. TypeBox.Items.OfType<ComboBoxItem>().Select(i => (string)i.Content)];

    public string? FormErrorText => FormError.Visibility == Visibility.Visible ? FormError.Text : null;

    /// The id to keep selected across refreshes, which rebuild the list.
    private string? _selectedId;

    public void Refresh() => Refresh(_selectedId);

    public void Refresh(string? select)
    {
        var draft = Editor.Draft;
        Items = draft.SortedActionIds().Select(id => new ActionItem(id, draft.Action(id)!, draft.Users(id).Count)).ToList();
        _selectedId = Items.FirstOrDefault(i => i.Id == select)?.Id ?? Items.FirstOrDefault()?.Id;
        _showing = true;
        ActionList.ItemsSource = Items;
        _showing = false;
        ActionList.SelectedIndex = Items.ToList().FindIndex(i => i.Id == _selectedId);
        NewButton.IsEnabled = CanEdit;
        Empty.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListCard.Visibility = Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UndoButton.IsEnabled = CanEdit && Editor.UndoName is not null;
        RedoButton.IsEnabled = CanEdit && Editor.RedoName is not null;
        ShowDetail();
    }

    private void ShowDetail()
    {
        DeleteButton.IsEnabled = CanEdit && Selected is not null;
        DetailCard.Visibility = Selected is null ? Visibility.Collapsed : Visibility.Visible;
        if (Selected is not { } item) return;
        _showing = true;
        IdBox.Text = item.Id;
        IdBox.IsEnabled = CanEdit;
        // Context Aware has no form yet, and picking another type would replace its variants.
        var contextual = item.Action is ContextualAction;
        TypeBox.IsEnabled = CanEdit && !contextual;
        TypeBox.PlaceholderText = contextual ? item.Action.DisplayName : "";
        FormNote.Visibility = contextual ? Visibility.Visible : Visibility.Collapsed;
        TypeBox.SelectedItem = TypeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == ActionForms.FormType(item.Action));
        ShowFields(item.Action.Type, ActionForms.Values(item.Action), ActionForms.Unshowable(item.Action));
        ShowError(null);
        var users = Editor.Draft.Users(item.Id);
        UsedBy.Text = users.Count == 0 ? "No binding uses this action yet."
            : "Used by " + string.Join(", ", users.Select(p => $"{p} ({Editor.Draft.Entry(p)?.Title})")) + ".";
        if (BuiltInActions.IsBuiltIn(item.Id))
            UsedBy.Text += " This one overrides the built-in action of the same name; delete it to use the built-in one again.";
        _showing = false;
    }

    private void ShowFields(string type, IReadOnlyDictionary<string, string> values, IReadOnlySet<string>? unshowable = null)
    {
        var fields = ActionForms.Fields(type);
        _form.Show(fields ?? [], values, unshowable);
        AppButtons.Visibility = type == "application" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string? message)
    {
        FormError.Text = message ?? "";
        FormError.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// Builds the action from the form and saves it, or says why it isn't valid.
    public void Apply()
    {
        if (_showing || !CanEdit || Selected is not { } item || FormType is not { } type || ActionForms.Fields(type) is null) return;
        ConfigAction action;
        try { action = ActionForms.KeepNames(ActionForms.Build(type, _form.Values, item.Id, item.Action), item.Action); }
        catch (ConfigProblem problem) { ShowError(problem.Message); return; }
        ShowError(null);
        if (ConfigWriter.Action(action, []) == ConfigWriter.Action(item.Action, [])) return;
        Editor.Change("Change action", c => c.SetActionDefinition(item.Id, action));
    }

    /// New action (Ctrl+N): a Do Nothing action named "new-action", with its id ready to type.
    public void New()
    {
        if (!CanEdit) return;
        var id = Editor.Draft.FreeActionId();
        Editor.Change("New action", c => c.SetActionDefinition(id, new BasicAction("noop")));
        Refresh(id);
        IdBox.Focus(FocusState.Programmatic);
        IdBox.SelectAll();
    }

    /// Renames the selected action and every binding that uses it.
    public void Rename(string newId)
    {
        if (!CanEdit || Selected is not { } item || newId == item.Id) return;
        newId = newId.Trim();
        if (newId.Length == 0) { ShowError("Give the action an id."); return; }
        if (Editor.Draft.Action(newId) is not null) { ShowError($"There's already an action “{newId}”."); return; }
        Editor.Change("Rename action", c => c.RenameAction(item.Id, newId));
        Refresh(newId);
    }

    /// Deletes the selected action; one in use stays, and the reason names its bindings.
    public void Delete()
    {
        if (!CanEdit || Selected is not { } item) return;
        var users = Editor.Draft.Users(item.Id);
        if (users.Count > 0 && !BuiltInActions.IsBuiltIn(item.Id))
        {
            Problem.Title = $"“{item.Id}” is in use";
            Problem.Message = $"Change {string.Join(", ", users.Select(p => $"{p} ({Editor.Draft.Entry(p)?.Title})"))} to another action first.";
            Problem.IsOpen = true;
            return;
        }
        var next = Items.SkipWhile(i => i != item).Skip(1).FirstOrDefault()?.Id;
        Editor.Change("Delete action", c => c.RemoveAction(item.Id));
        Refresh(next);
    }

    public bool ProblemShown => Problem.IsOpen;

    /// Fills Use Application from an app in Start: its exe, or its AppUserModelID.
    public void UseApp(StartApp app)
    {
        _form.Set("exe", app.Exe ?? "");
        _form.Set("appId", app.AppId ?? "");
        Apply();
    }

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || Selected is not { Action: not ContextualAction } item || FormType is not { } type) return;
        if (type == ActionForms.FormType(item.Action)) ShowFields(type, ActionForms.Values(item.Action), ActionForms.Unshowable(item.Action));
        else ShowFields(type, ActionForms.Defaults(type));
        Apply();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing) return;
        if (Selected is { } item) _selectedId = item.Id;
        ShowDetail();
    }

    private void OnIdLostFocus(object sender, RoutedEventArgs e) => Rename(IdBox.Text);

    private void OnIdKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) Rename(IdBox.Text);
    }

    private async void OnChooseApp(object sender, RoutedEventArgs e)
    {
        if (await AppPicker.ChooseAsync(XamlRoot, ActualTheme) is { } app) UseApp(app);
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (await AppPicker.BrowseAsync(App.MainWindow.Handle()) is { } path) UseApp(new StartApp(Path.GetFileNameWithoutExtension(path), path, null));
    }

    private void OnNew(object sender, RoutedEventArgs e) => New();

    private void OnDelete(object sender, RoutedEventArgs e) => Delete();

    private void OnUndo(object sender, RoutedEventArgs e) { if (CanEdit) Editor.Undo(); }

    private void OnRedo(object sender, RoutedEventArgs e) { if (CanEdit) Editor.Redo(); }
}
