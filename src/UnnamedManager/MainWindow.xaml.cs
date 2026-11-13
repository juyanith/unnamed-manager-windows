using UnnamedManager.Pages;
using UnnamedManager.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace UnnamedManager;

/// The Bindings window: Bindings, then Settings at the bottom (DESIGN.md). Closing it
/// hides it; the app keeps running in the tray (S-038).
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
        ApplyTheme(App.ThemeMode);
        AppWindow.Closing += (window, args) =>
        {
            args.Cancel = true;
            window.Hide();
        };
        Show("bindings");
    }

    /// Opens a page by id: "bindings", "actions", "contexts", "help" or "settings".
    public void Show(string id)
    {
        object? item = id == "settings" ? Nav.SettingsItem : Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == id);
        if (item is not null && Nav.SelectedItem != item) Nav.SelectedItem = item;
    }

    /// Brings the window forward on a page, from the tray or a second start.
    public void Open(string id)
    {
        Show(id);
        AppWindow.Show();
        Activate();
        Win32.ForceForeground(this.Handle());
    }

    /// The Bindings page with one entry selected, as a Help row shows it (S-050).
    public void ShowBinding(Core.Config.KeyPath path)
    {
        Show("bindings");
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (CurrentPage is BindingsPage page) page.Select(page.Find(path));
        });
    }

    /// The page on screen, or null before the first navigation.
    public Page? CurrentPage => ContentFrame.Content as Page;

    private void OnPageShortcut(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Show(sender.Key switch
        {
            Windows.System.VirtualKey.Number2 => "actions",
            Windows.System.VirtualKey.Number3 => "contexts",
            Windows.System.VirtualKey.Number4 or Windows.System.VirtualKey.F1 => "help",
            _ => "bindings",
        });
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
        var (title, page) = args.IsSettingsSelected ? ("Settings", typeof(SettingsPage))
            : (string)((NavigationViewItem)args.SelectedItem).Tag switch
            {
                "actions" => ("Actions", typeof(ActionsPage)),
                "contexts" => ("Contexts", typeof(ContextsPage)),
                "help" => ("Help", typeof(HelpPage)),
                _ => ("Bindings", typeof(BindingsPage)),
            };
        sender.Header = null;
        Title = $"{title} - Unnamed Manager";
        ContentFrame.Navigate(page, null, new SuppressNavigationTransitionInfo());
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Nav.IsPaneOpen = !Nav.IsPaneOpen;
}
