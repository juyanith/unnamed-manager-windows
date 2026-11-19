using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core.Config;

namespace UnnamedManager.Pages;

/// Hold to choose on a leader, and a menu's Contents: keys you add, or Recent Windows
/// with its options (S-073, S-074, S-076, spec v18, v19).
public sealed partial class BindingsPage
{
    /// Shows the hold switch for a leader menu, and Contents for a menu that's empty or a list.
    private void ShowListFields(BindingItem item)
    {
        var node = item.Node;
        var entry = Editor.Draft.Entry(node.Path);
        var leaderMenu = node.Path.IsLeader && node.IsMenu;
        HoldToggle.Visibility = HoldHelp.Visibility = leaderMenu ? Visibility.Visible : Visibility.Collapsed;
        // A switch that was collapsed draws its new state only once it has its template.
        HoldToggle.ApplyTemplate();
        HoldToggle.IsOn = entry?.Hold == true;
        HoldToggle.IsEnabled = CanEdit;
        var list = entry?.List;
        // A menu with keys under it keeps them; Contents shows once it's empty, or a list.
        var canList = node.IsMenu && entry?.Action is null && (list is not null || item.Children.Count == 0);
        ContentsBox.Visibility = canList ? Visibility.Visible : Visibility.Collapsed;
        ContentsBox.SelectedIndex = list is null ? 0 : 1;
        ContentsBox.IsEnabled = CanEdit;
        ListOptions.Visibility = list is null ? Visibility.Collapsed : Visibility.Visible;
        if (list is null) return;
        ChooseBox.SelectedIndex = list.Swaps ? 1 : 0;
        CountBox.Value = list.Count;
        MinimizedToggle.ApplyTemplate();
        MinimizedToggle.IsOn = list.Minimized ?? true;
        DesktopsBox.SelectedIndex = list.Spaces == "current" ? 1 : 0;
        ScopeBox.SelectedIndex = list.DisplayOnly ? 1 : 0;
        // Swap only works with windows shown on this desktop.
        MinimizedToggle.Visibility = DesktopsBox.Visibility = list.Swaps ? Visibility.Collapsed : Visibility.Visible;
        foreach (var control in new Control[] { ChooseBox, CountBox, MinimizedToggle, DesktopsBox, ScopeBox }) control.IsEnabled = CanEdit;
    }

    /// Turns Hold to choose on or off for the selected leader.
    public void SetHold(bool hold)
    {
        if (!CanEdit || Selected is not { } item || Editor.Draft.Entry(item.Node.Path) is not { } entry || entry.Hold == hold) return;
        var path = item.Node.Path;
        Editor.Change(hold ? "Hold to choose" : "Turn off Hold to choose", c => c.SetHold(path, hold));
        Refresh(path);
    }

    /// Makes the selected menu a list of recent windows, or a menu of keys you add.
    public void SetList(ListSpec? list)
    {
        if (!CanEdit || Selected is not { } item || Editor.Draft.Entry(item.Node.Path) is not { } entry || Equals(entry.List, list)) return;
        var path = item.Node.Path;
        Editor.Change(list is null ? "Keys you add" : entry.List is null ? "Recent windows" : "Change list", c => c.SetList(path, list));
        Refresh(path);
    }

    /// The list the option fields describe. Swap leaves out minimized and desktops, and
    /// a field at its default is left out, unless the file already had it.
    private ListSpec ListFromFields(ListSpec before)
    {
        var swaps = ChooseBox.SelectedIndex == 1;
        var count = double.IsNaN(CountBox.Value) ? before.Count : (int)Math.Clamp(Math.Round(CountBox.Value), ListSpec.MinCount, ListSpec.MaxCount);
        bool? minimized = swaps ? null : !MinimizedToggle.IsOn ? false : before.Minimized is null ? null : true;
        string? spaces = swaps ? null : DesktopsBox.SelectedIndex == 1 ? "current" : before.Spaces is null ? null : "all";
        return before with { Choose = swaps ? "swap" : "focus", Count = count, Minimized = minimized, Spaces = spaces, Scope = ScopeBox.SelectedIndex == 1 ? "display" : "all" };
    }

    private void ApplyListFields()
    {
        if (_showing || Selected is not { } item || Editor.Draft.Entry(item.Node.Path)?.List is not { } before) return;
        SetList(ListFromFields(before));
    }

    private void OnHoldToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing) SetHold(HoldToggle.IsOn);
    }

    private void OnContentsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing) SetList(ContentsBox.SelectedIndex == 1 ? new ListSpec() : null);
    }

    private void OnListChanged(object sender, SelectionChangedEventArgs e) => ApplyListFields();

    private void OnListToggled(object sender, RoutedEventArgs e) => ApplyListFields();

    private void OnCountChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => ApplyListFields();

    private void OnListLostFocus(object sender, RoutedEventArgs e) => ApplyListFields();
}
