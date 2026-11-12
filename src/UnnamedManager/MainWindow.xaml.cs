using UnnamedManager.Pages;
using UnnamedManager.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace UnnamedManager;

/// The app's window. Until M5 turns it into the Bindings window, it holds Settings only.
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        this.SizeAndCenter(1040, 720);
        this.MinimumSize(760, 520);
        ApplyTheme(Prefs.Current.Theme);
        Show("settings");
    }

    /// Opens a page by id. Only "settings" exists until M5 adds the Bindings pages.
    public void Show(string id)
    {
        if (id == "settings" && Nav.SelectedItem != Nav.SettingsItem) Nav.SelectedItem = Nav.SettingsItem;
    }

    /// "system", "light", or "dark". Also sets the caption buttons to match.
    public void ApplyTheme(string mode)
    {
        Root.RequestedTheme = Theme.Element(mode);
        AppWindow.TitleBar.PreferredTheme = mode switch
        {
            "light" => TitleBarTheme.Light,
            "dark" => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    public FrameworkElement RootElement => Root;

    public void DrawGridOverlay()
    {
        if (GridOverlay.Enabled) GridOverlay.Draw(GridCanvas, Root);
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!args.IsSettingsSelected) return;
        sender.Header = null;
        Title = "Settings - Unnamed Manager";
        ContentFrame.Navigate(typeof(SettingsPage), null, new SuppressNavigationTransitionInfo());
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Nav.IsPaneOpen = !Nav.IsPaneOpen;
}
