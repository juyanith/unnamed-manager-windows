using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using UnnamedManager.UI;

namespace UnnamedManager.Tray;

/// What the tray menu shows: the state of the app when it opens.
public sealed record TrayStatus(
    bool Enabled,
    bool CreatedStarter,
    DateTimeOffset? LoadedAt,
    (string First, int More)? Error,
    string VersionLine,
    bool RunsAtStartup,
    string? ElevatedApp = null,
    bool? OfficeShortcutsOn = null);

/// The commands the tray menu offers.
public sealed record TrayCommands(
    Action<bool> SetEnabled,
    Action Reload,
    Action OpenConfigFile,
    Action OpenFileLocation,
    Action OpenLogFolder,
    Action OpenHelp,
    Action<bool> SetRunAtStartup,
    Action Exit,
    Action<bool>? SetOfficeShortcuts = null);

/// The tray menu (S-008, S-009), as a WPF ContextMenu in the Fluent theme. It opens from a
/// tiny borderless host window at the tray icon, which takes the foreground first, so the
/// menu closes on a click elsewhere. The WPF app has no Bindings window yet: Edit bindings
/// and Settings are left out, and Run at startup is a switch here instead.
public sealed partial class TrayMenu
{
    private readonly Window _host;
    private ContextMenu? _menu;

    public TrayMenu()
    {
        _host = new Window
        {
            Title = "Unnamed Manager menu",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Width = 1,
            Height = 1,
            Opacity = 0,
            AllowsTransparency = true,
        };
    }

    public bool IsOpen => _menu?.IsOpen == true;

    /// Opens the menu at a point in screen pixels. With `keyboard`, the first item has focus.
    public void Open(int x, int y, TrayStatus status, TrayCommands commands, bool keyboard)
    {
        if (_menu is not null) _menu.IsOpen = false;
        var hwnd = new WindowInteropHelper(_host).EnsureHandle();
        SetWindowPos(hwnd, 0, x, y, 1, 1, SWP_NOZORDER);
        _host.Show();
        Win32.ForceForeground(hwnd);

        _menu = Build(status, commands);
        _menu.PlacementTarget = _host;
        _menu.Placement = PlacementMode.Relative;
        _menu.Closed += (_, _) => _host.Hide();
        if (keyboard)
            _menu.Opened += (_, _) => _menu.Dispatcher.BeginInvoke(() =>
                _menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEnabled)?.Focus(), DispatcherPriority.Input);
        _menu.IsOpen = true;
    }

    public void Close()
    {
        if (_menu is not null) _menu.IsOpen = false;
    }

    /// The items in S-009's order: status and warnings (only when there are any),
    /// Enabled, the config commands, the version and config lines, Exit.
    public static ContextMenu Build(TrayStatus status, TrayCommands commands)
    {
        var menu = new ContextMenu();
        var items = menu.Items;
        var hasStatus = false;
        if (!status.Enabled)
        {
            items.Add(Line("Not capturing leaders"));
            hasStatus = true;
        }
        if (status.ElevatedApp is { } elevated)
        {
            // S-056: why leaders and window actions may not work in the app you came from.
            items.Add(Line(Core.Windows.Elevation.TrayLine, warning: true));
            items.Add(Line(Core.Windows.Elevation.TrayWhy(elevated)));
            hasStatus = true;
        }
        if (status.Error is { } error)
        {
            items.Add(Line("Config error", warning: true));
            var row = Command("", commands.OpenConfigFile);
            row.Header = new TextBlock { Text = error.First };
            row.ToolTip = error.First;
            items.Add(row);
            if (error.More > 0) items.Add(Line($"+{error.More} more"));
            hasStatus = true;
        }
        if (hasStatus) items.Add(new Separator());

        items.Add(Toggle("_Enabled", status.Enabled, commands.SetEnabled));
        items.Add(Toggle("Run at _startup", status.RunsAtStartup, commands.SetRunAtStartup));
        // S-057: only while the handler is Windows' own or this app's; never the user's own.
        if (status.OfficeShortcutsOn is { } officeOn)
        {
            var office = Command(officeOn ? "Turn o_ff Office shortcuts" : "Turn on O_ffice shortcuts", () => commands.SetOfficeShortcuts?.Invoke(!officeOn));
            office.ToolTip = officeOn
                ? "Windows opens Office on Hyper alone and on Hyper+W, X, P, O, T and other letters. Turn this off to keep those keys for your bindings."
                : "Let Windows open Office on Hyper and on Hyper+W, X, P, O, T and other letters again.";
            items.Add(office);
        }
        items.Add(new Separator());

        items.Add(Command("_Reload config", commands.Reload));
        items.Add(Command("_Open config file", commands.OpenConfigFile));
        items.Add(Command("Open file _location", commands.OpenFileLocation));
        items.Add(new Separator());

        items.Add(Command("_Help", commands.OpenHelp));
        items.Add(Command("Open lo_g folder", commands.OpenLogFolder));
        items.Add(Line(status.VersionLine));
        var configLine = status.CreatedStarter && status.Error is null ? "Created config file"
            : status.LoadedAt is { } at ? $"Config loaded at {at.LocalDateTime:HH:mm}"
            : "No config loaded";
        items.Add(Line(configLine));
        items.Add(Command("E_xit", commands.Exit));
        return menu;
    }

    private static MenuItem Command(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Toggle(string header, bool on, Action<bool> set)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = on };
        item.Click += (_, _) => set(item.IsChecked);
        return item;
    }

    /// A line that says something and does nothing. A warning has the warning glyph, never color alone.
    private static MenuItem Line(string text, bool warning = false)
    {
        // Text, not a header with access keys: an underscore in a path stays an underscore.
        var item = new MenuItem { Header = new TextBlock { Text = text }, IsEnabled = false };
        if (warning)
        {
            var glyph = new TextBlock { Text = "", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
            item.Icon = glyph;
        }
        return item;
    }

    private const uint SWP_NOZORDER = 0x4;

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
