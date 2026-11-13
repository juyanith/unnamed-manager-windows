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
