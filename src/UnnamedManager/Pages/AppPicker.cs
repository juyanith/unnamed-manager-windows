using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Actions;

namespace UnnamedManager.Pages;

/// Choose app and Browse (S-044, S-046): an app from Start in a dialog with a
/// filter box, or an exe file from a file picker.
public static class AppPicker
{
    private static IReadOnlyList<StartApp>? _apps;

    /// The apps in Start, read once per run.
    public static IReadOnlyList<StartApp> Apps => _apps ??= StartApps.List();

    /// The dialog on screen, for the self-test.
    public static ContentDialog? Dialog { get; private set; }

    public static async Task<StartApp?> ChooseAsync(XamlRoot root, ElementTheme theme)
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = (double)Application.Current.Resources["AppPickerListHeight"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Apps");
        var filter = new TextBox { PlaceholderText = "Search apps" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(filter, "Search apps");
        void Fill()
        {
            list.Items.Clear();
            foreach (var app in Apps.Where(a => a.Name.Contains(filter.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)))
                list.Items.Add(new ListViewItem { Content = Row(app), Tag = app });
        }
        filter.TextChanged += (_, _) => Fill();
        Fill();
        var panel = new StackPanel { Spacing = UI.Space.Sm, Width = (double)Application.Current.Resources["AppPickerWidth"] };
        panel.Children.Add(filter);
        panel.Children.Add(list);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = "Choose app",
            Content = panel,
            PrimaryButtonText = "Choose",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        _picked = false;
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is not null) { _picked = true; dialog.Hide(); } };
        Dialog = dialog;
        var result = await dialog.ShowAsync();
        Dialog = null;
        return result == ContentDialogResult.Primary || _picked ? (list.SelectedItem as ListViewItem)?.Tag as StartApp : null;
    }

    private static bool _picked;

    /// Picks an app in the open dialog and closes it, for the self-test.
    public static void PickForTest(StartApp app)
    {
        if (Dialog?.Content is not StackPanel { Children: [_, ListView list] }) return;
        list.SelectedItem = list.Items.OfType<ListViewItem>().FirstOrDefault(i => i.Tag as StartApp == app);
        _picked = true;
        Dialog.Hide();
    }

    private static StackPanel Row(StartApp app)
    {
        var row = new StackPanel { Padding = new Thickness(0, UI.Space.Xs, 0, UI.Space.Xs) };
        row.Children.Add(new TextBlock { Text = app.Name });
        row.Children.Add(new TextBlock { Text = app.Detail, Style = (Style)Application.Current.Resources["SecondaryCaptionTextStyle"] });
        return row;
    }

    /// An exe from a file picker, or null.
    public static async Task<string?> BrowseAsync(nint window)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// Any file, for its icon (S-095).
    public static async Task<string?> BrowseIconAsync(nint window)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
