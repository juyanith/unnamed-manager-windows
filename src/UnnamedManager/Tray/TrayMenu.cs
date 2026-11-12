using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using UnnamedManager.UI;
using Windows.Graphics;

namespace UnnamedManager.Tray;

/// What the tray menu shows: the state of the app when it opens.
public sealed record TrayStatus(
    bool Enabled,
    bool CreatedStarter,
    DateTimeOffset? LoadedAt,
    (string First, int More)? Error,
    string VersionLine,
    string Theme);

/// The commands the tray menu offers.
public sealed record TrayCommands(
    Action<bool> SetEnabled,
    Action Reload,
    Action OpenConfigFile,
    Action OpenFileLocation,
    Action Exit);

/// The tray menu (S-008, S-009): a WinUI MenuFlyout, so it looks like a Windows 11
/// menu, follows the app theme, takes tooltips and a focusable error row, and can be
/// snapshotted. It opens from a tiny borderless host window at the tray icon, which
/// takes the foreground first so the menu closes on a click elsewhere.
public sealed class TrayMenu
{
    private readonly Window _host = new();
    private readonly Grid _anchor = new();
    private MenuFlyout? _menu;

    public TrayMenu()
    {
        _host.Content = _anchor;
        _host.Title = "Unnamed Manager menu";
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        _host.AppWindow.SetPresenter(presenter);
        _host.AppWindow.IsShownInSwitchers = false;
    }

    public bool IsOpen => _menu?.IsOpen == true;

    public MenuFlyout? Menu => _menu;

    public nint HostHandle => _host.Handle();

    /// Opens the menu at a point in screen pixels. With `keyboard`, the first item has focus.
    public void Open(int x, int y, TrayStatus status, TrayCommands commands, bool keyboard)
    {
        _menu?.Hide();
        _anchor.RequestedTheme = Theme.Element(status.Theme);
        _host.AppWindow.MoveAndResize(new RectInt32(x, y, 1, 1));
        _host.AppWindow.Show();
        Win32.ForceForeground(_host.Handle());

        _menu = Build(status, commands);
        _menu.ShouldConstrainToRootBounds = false;
        _menu.Closed += (_, _) => _host.AppWindow.Hide();
        if (keyboard)
            // The items can take focus only once laid out, after Opened.
            _menu.Opened += (_, _) => _anchor.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                (_menu.Items.FirstOrDefault(i => i is MenuFlyoutItemBase { IsEnabled: true }) as Control)?.Focus(FocusState.Keyboard));
        _menu.ShowAt(_anchor, new FlyoutShowOptions { Position = new Windows.Foundation.Point(0, 0), ShowMode = FlyoutShowMode.Standard });
    }

    public void Close() => _menu?.Hide();

    /// The items in S-009's order: status and warnings (only when there are any),
    /// Enabled, the config commands, the version and config lines, Exit.
    public static MenuFlyout Build(TrayStatus status, TrayCommands commands)
    {
        var menu = new MenuFlyout();
        var items = menu.Items;
        var status_ = false;
        if (!status.Enabled)
        {
            items.Add(new MenuFlyoutItem { Text = "Not capturing leaders" });
            status_ = true;
        }
        if (status.Error is { } error)
        {
            items.Add(new MenuFlyoutItem { Text = "Config error", IsEnabled = false, Icon = new FontIcon { Style = (Style)Application.Current.Resources["WarningGlyphStyle"] } });
            var row = new MenuFlyoutItem { Text = error.First };
            ToolTipService.SetToolTip(row, error.First);
            row.Click += (_, _) => commands.OpenConfigFile();
            items.Add(row);
            if (error.More > 0) items.Add(new MenuFlyoutItem { Text = $"+{error.More} more", IsEnabled = false });
            status_ = true;
        }
        if (status_) items.Add(new MenuFlyoutSeparator());

        var enabled = new ToggleMenuFlyoutItem { Text = "Enabled", IsChecked = status.Enabled, AccessKey = "E" };
        enabled.Click += (_, _) => commands.SetEnabled(enabled.IsChecked);
        items.Add(enabled);
        items.Add(new MenuFlyoutSeparator());

        items.Add(Command("Reload config", "R", commands.Reload));
        items.Add(Command("Open config file", "O", commands.OpenConfigFile));
        items.Add(Command("Open file location", "L", commands.OpenFileLocation));
        items.Add(new MenuFlyoutSeparator());

        items.Add(new MenuFlyoutItem { Text = status.VersionLine, IsEnabled = false });
        var configLine = status.CreatedStarter && status.Error is null ? "Created config file"
            : status.LoadedAt is { } at ? $"Config loaded at {at.LocalDateTime:HH:mm}"
            : "No config loaded";
        items.Add(new MenuFlyoutItem { Text = configLine, IsEnabled = false });
        items.Add(Command("Exit", "X", commands.Exit));
        return menu;
    }

    private static MenuFlyoutItem Command(string text, string accessKey, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, AccessKey = accessKey };
        item.Click += (_, _) => action();
        return item;
    }
}
