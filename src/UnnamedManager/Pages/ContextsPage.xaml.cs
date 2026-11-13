using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using UnnamedManager.Actions;
using UnnamedManager.Core.Config;
using UnnamedManager.UI;

namespace UnnamedManager.Pages;

/// One app in a context: as the config names it, with its name and icon from Start.
public sealed class AppItem(string app)
{
    public string App { get; } = app;
    public string Name { get; } = AppIcons.Name(app);
    public ImageSource? Icon { get; } = AppIcons.For(app, (int)(double)Application.Current.Resources["ContextIconSize"]);
}

/// One row of the Contexts list: its id and its apps.
public sealed class ContextItem(ConfigContext context)
{
    public string Id { get; } = context.Id;
    public List<AppItem> Apps { get; } = context.Apps.Select(a => new AppItem(a)).ToList();
    public string Names => Apps.Count == 0 ? "No apps yet" : string.Join(", ", Apps.Select(a => a.Name));
    public string Spoken => $"{Id}, {Names}";
}

/// The Contexts page (S-046): the contexts in order, which decides which variant
/// wins, and the selected one's apps, added with Choose app and Browse.
public sealed partial class ContextsPage : Page
{
    private string? _selectedId;
    private bool _showing;

    public ContextsPage()
    {
        InitializeComponent();
        Empty.Invoked += New;
        DeleteKey.ScopeOwner = ContextList;
        Loaded += (_, _) => { App.Editor.Changed += Refresh; App.Config.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { App.Editor.Changed -= Refresh; App.Config.Changed -= Refresh; };
    }

    private static ConfigEditor Editor => App.Editor;

    public UI.EmptyState EmptyState => Empty;

    private static bool CanEdit => App.CanEdit;

    public IReadOnlyList<ContextItem> Items { get; private set; } = [];

    public ContextItem? Selected => ContextList.SelectedItem as ContextItem;

    public bool ProblemShown => Problem.IsOpen;

    public void Refresh() => Refresh(_selectedId);

    public void Refresh(string? select)
    {
        Items = Editor.Draft.Contexts.Select(c => new ContextItem(c)).ToList();
        _selectedId = Items.FirstOrDefault(i => i.Id == select)?.Id ?? Items.FirstOrDefault()?.Id;
        _showing = true;
        ContextList.ItemsSource = Items;
        _showing = false;
        ContextList.SelectedIndex = Items.ToList().FindIndex(i => i.Id == _selectedId);
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
        IdBox.Text = item.Id;
        IdBox.IsEnabled = ChooseButton.IsEnabled = BrowseButton.IsEnabled = RemoveButton.IsEnabled = CanEdit;
        AppList.ItemsSource = item.Apps;
        ShowError(null);
        var users = Editor.Draft.ContextUsers(item.Id);
        UsedBy.Text = users.Count == 0 ? "No Context Aware action checks this context yet." : $"Checked by {string.Join(", ", users)}.";
    }

    private void ShowError(string? message)
    {
        FormError.Text = message ?? "";
        FormError.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// New context (Ctrl+N), with its id ready to type.
    public void New()
    {
        if (!CanEdit) return;
        var id = Editor.Draft.FreeContextId();
        Editor.Change("New context", c => c.AddContext(id));
        Refresh(id);
        IdBox.Focus(FocusState.Programmatic);
        IdBox.SelectAll();
    }

    public void Rename(string newId)
    {
        if (!CanEdit || Selected is not { } item || newId.Trim() == item.Id) return;
        newId = newId.Trim();
        if (newId.Length == 0) { ShowError("Give the context an id."); return; }
        if (Editor.Draft.Contexts.Any(c => c.Id == newId)) { ShowError($"There's already a context “{newId}”."); return; }
        Editor.Change("Rename context", c => c.RenameContext(item.Id, newId));
        Refresh(newId);
    }

    /// Adds an app as the config names it: an exe file name or path, or an AppUserModelID.
    public void AddApp(StartApp app)
    {
        if (!CanEdit || Selected is not { } item) return;
        var name = app.AppId ?? (app.Exe is { } exe && AppSwitcher.Resolve(Path.GetFileName(exe)) is not null ? Path.GetFileName(exe) : app.Exe!);
        if (item.Apps.Any(a => string.Equals(a.App, name, StringComparison.OrdinalIgnoreCase))) { ShowError($"{app.Name} is already in this context."); return; }
        Editor.Change("Add app", c => c.SetContextApps(item.Id, [.. item.Apps.Select(a => a.App), name]));
        Refresh(item.Id);
    }

    public void RemoveApp(string app)
    {
        if (!CanEdit || Selected is not { } item) return;
        Editor.Change("Remove app", c => c.SetContextApps(item.Id, item.Apps.Select(a => a.App).Where(a => a != app).ToList()));
        Refresh(item.Id);
    }

    /// Move up and Move down (Alt+Up, Alt+Down). The order decides which variant wins.
    public void Move(int by)
    {
        if (!CanEdit || Selected is not { } item) return;
        Editor.Change(by < 0 ? "Move up" : "Move down", c => c.MoveContext(item.Id, by));
        Refresh(item.Id);
    }

    public void Delete()
    {
        if (!CanEdit || Selected is not { } item) return;
        var users = Editor.Draft.ContextUsers(item.Id);
        if (users.Count > 0)
        {
            Problem.Title = $"“{item.Id}” is in use";
            Problem.Message = $"Take its variant out of {string.Join(", ", users)} first.";
            Problem.IsOpen = true;
            return;
        }
        var next = Items.SkipWhile(i => i != item).Skip(1).FirstOrDefault()?.Id;
        Editor.Change("Delete context", c => c.RemoveContext(item.Id));
        Refresh(next);
    }

    private void OnDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.Items.FirstOrDefault() is not ContextItem item) return;
        var from = Editor.Draft.Contexts.ToList().FindIndex(c => c.Id == item.Id);
        var to = ContextList.Items.IndexOf(item);
        if (from < 0 || to < 0 || from == to) { Refresh(item.Id); return; }
        _selectedId = item.Id;
        Editor.Change("Move", c => c.MoveContext(item.Id, to - from));
        Refresh(item.Id);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing) return;
        if (Selected is { } item) _selectedId = item.Id;
        ShowDetail();
    }

    private void OnMoveUp(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; Move(-1); }

    private void OnMoveDown(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; Move(1); }

    private void OnIdLostFocus(object sender, RoutedEventArgs e) => Rename(IdBox.Text);

    private void OnIdKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) Rename(IdBox.Text);
    }

    private async void OnChooseApp(object sender, RoutedEventArgs e)
    {
        if (await AppPicker.ChooseAsync(XamlRoot, ActualTheme) is { } app) AddApp(app);
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (await AppPicker.BrowseAsync(App.MainWindow.Handle()) is { } path) AddApp(new StartApp(Path.GetFileNameWithoutExtension(path), path, null));
    }

    private void OnRemoveApp(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is AppItem app) RemoveApp(app.App);
    }

    private void OnNew(object sender, RoutedEventArgs e) => New();

    private void OnDelete(object sender, RoutedEventArgs e) => Delete();

    private void OnUndo(object sender, RoutedEventArgs e) { if (CanEdit) Editor.Undo(); }

    private void OnRedo(object sender, RoutedEventArgs e) { if (CanEdit) Editor.Redo(); }
}
