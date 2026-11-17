using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using UnnamedManager.Core.Config;
using UnnamedManager.Tray;
using UnnamedManager.UI;

namespace UnnamedManager.Pages;

/// Settings (S-047): the config's runtime, ui and windows settings as cards. Every
/// change is one undoable step, written to the file at once.
public sealed partial class SettingsPage : Page
{
    private bool _showing;

    public SettingsPage()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        AboutCard.Description = $"Version {version?.Split('+')[0]}. Your bindings are in {App.Config.Path}.";
        Loaded += (_, _) => { App.Editor.Changed += Refresh; App.Config.Changed += Refresh; App.Keys.Recorded += OnRecorded; Refresh(); };
        Unloaded += (_, _) =>
        {
            App.Editor.Changed -= Refresh;
            App.Config.Changed -= Refresh;
            App.Keys.Recorded -= OnRecorded;
            StopRecording();
        };
    }

    private static ConfigEditor Editor => App.Editor;

    /// How far down the Windows group starts, for the `settings-windows` snapshot.
    public double WindowsOffset => WindowsHeader.TransformToVisual(this).TransformPoint(default).Y - Space.Xxl;

    /// "cancel" or "back" while a key button records.
    public string? Recording { get; private set; }

    public string? KeyErrorText => KeyError.Visibility == Visibility.Visible ? KeyError.Text : null;

    public void Refresh()
    {
        var c = Editor.Draft;
        _showing = true;
        Select(ThemeBox, c.UI.Theme.ToLowerInvariant());
        Select(PositionBox, c.UI.Position.ToLowerInvariant());
        ShowHudToggle.IsOn = c.Runtime.ShowHud;
        DelayBox.Value = c.Runtime.HudDelayMs;
        TimeoutBox.Value = c.Runtime.TimeoutMs;
        FlashToggle.IsOn = c.UI.ErrorFlash;
        BellToggle.IsOn = c.UI.ErrorBell;
        GapBox.Value = c.Windows.Gap;
        ColumnsBox.Value = c.Windows.MaxColumns;
        RowsBox.Value = c.Windows.MaxRows;
        MinVisibleBox.Value = c.Windows.MinVisible;
        RowToleranceBox.Value = c.Windows.RowTolerance;
        TieToleranceBox.Value = c.Windows.TieTolerance;
        ShowApps(c.Windows.Apps);
        CancelKeyButton.Content = Recording == "cancel" ? "Press a key…" : c.Runtime.CancelKey;
        BackToggle.IsOn = c.Runtime.BackKey.Length > 0;
        BackKeyButton.IsEnabled = BackToggle.IsOn;
        BackKeyButton.Content = Recording == "back" ? "Press a key…" : c.Runtime.BackKey.Length > 0 ? c.Runtime.BackKey : "Off";
        var errors = App.Config.Errors.Concat(Editor.DraftErrors).ToList();
        Problem.IsOpen = errors.Count > 0;
        Problem.Title = App.Config.State.HasErrors ? "The config file has errors" : "This change isn't saved yet";
        Problem.Message = string.Join(" ", errors.Select(e => e.Message));
        ShowStartup();
        foreach (var control in new Control[] { ThemeBox, PositionBox, ShowHudToggle, DelayBox, TimeoutBox, FlashToggle, BellToggle, GapBox, ColumnsBox, RowsBox,
            MinVisibleBox, RowToleranceBox, TieToleranceBox, ChooseAppButton, BrowseAppButton, CancelKeyButton, BackToggle })
            control.IsEnabled = App.CanEdit;
        _showing = false;
    }

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    /// One setting change, as one undoable step.
    public void Change(string name, Func<Config, Config> edit)
    {
        if (_showing || !App.CanEdit) return;
        Editor.Change(name, edit);
    }

    private void OnTheme(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedTag(ThemeBox) is { } theme) Change("Change theme", c => c.WithUI(u => u with { Theme = theme }));
    }

    private void OnPosition(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedTag(PositionBox) is { } position) Change("Change position", c => c.WithUI(u => u with { Position = position }));
    }

    private void OnShowHud(object sender, RoutedEventArgs e) => Change("Show the Guide", c => c.WithRuntime(r => r with { ShowHud = ShowHudToggle.IsOn }));

    private void OnDelay(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Change("Change wait", c => c.WithRuntime(r => r with { HudDelayMs = SettingsValues.Milliseconds(args.NewValue) }));

    private void OnTimeout(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Change("Change close after", c => c.WithRuntime(r => r with { TimeoutMs = SettingsValues.Milliseconds(args.NewValue) }));

    private void OnFlash(object sender, RoutedEventArgs e) => Change("Flash", c => c.WithUI(u => u with { ErrorFlash = FlashToggle.IsOn }));

    private void OnBell(object sender, RoutedEventArgs e) => Change("Play a sound", c => c.WithUI(u => u with { ErrorBell = BellToggle.IsOn }));

    private void OnGap(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // Rounded to a step of 2; the refresh after the change shows the value kept.
        Change("Change gap", c => c.WithGap(args.NewValue));
    }

    private void OnColumns(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Change("Change maximum columns", c => c.WithWindows(w => w with { MaxColumns = SettingsValues.Limit(args.NewValue) }));

    private void OnRows(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Change("Change maximum rows", c => c.WithWindows(w => w with { MaxRows = SettingsValues.Limit(args.NewValue) }));

    private void OnMinVisible(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Change("Change smallest visible part", c => c.WithWindows(w => w with { MinVisible = SettingsValues.Distance(args.NewValue) }));

    private void OnRowTolerance(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Change("Change same row within", c => c.WithWindows(w => w with { RowTolerance = SettingsValues.Distance(args.NewValue) }));

    private void OnTieTolerance(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Change("Change equally near within", c => c.WithWindows(w => w with { TieTolerance = SettingsValues.Distance(args.NewValue) }));

    // Apps (spec v15): a card per app in windows.apps, with a box for each window action.

    private string? _shownApps;

    /// The app rows, rebuilt only when the list changes, so a box keeps focus as it's checked.
    private void ShowApps(IReadOnlyList<KeyValuePair<string, AppRules>> apps)
    {
        var shown = string.Join("\n", apps.Select(a => $"{a.Key} {a.Value} {App.CanEdit}"));
        if (shown == _shownApps) return;
        _shownApps = shown;
        AppsPanel.Children.Clear();
        foreach (var (app, rules) in apps) AppsPanel.Children.Add(AppCard(app, rules));
    }

    private CommunityToolkit.WinUI.Controls.SettingsCard AppCard(string app, AppRules rules)
    {
        var name = Actions.AppIcons.Name(app);
        var size = (int)(double)Application.Current.Resources["ContextIconSize"];
        var card = new CommunityToolkit.WinUI.Controls.SettingsCard
        {
            Header = name,
            Description = app,
            HeaderIcon = Actions.AppIcons.For(app, size) is { } icon ? new ImageIcon { Source = icon } : new FontIcon { Glyph = "" },
        };
        CheckBox Box(string label, bool on, Func<AppRules, bool, AppRules> set)
        {
            var box = new CheckBox { Content = label, IsChecked = on, MinWidth = 0, IsEnabled = App.CanEdit };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, $"{label}: {name}");
            box.Click += (_, _) =>
            {
                var current = Editor.Draft.Windows.Apps.FirstOrDefault(a => a.Key == app).Value ?? new AppRules();
                Change($"Change {label.ToLowerInvariant()}", c => c.WithAppRules(app, set(current, box.IsChecked == true)));
            };
            return box;
        }
        var remove = new Button { Content = new FontIcon { Glyph = "" }, IsEnabled = App.CanEdit };
        ToolTipService.SetToolTip(remove, "Remove");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, $"Remove {name}");
        remove.Click += (_, _) => RemoveApp(app);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UI.Space.Md };
        row.Children.Add(Box("Focus", rules.Focus, (r, v) => r with { Focus = v }));
        row.Children.Add(Box("Swap", rules.Swap, (r, v) => r with { Swap = v }));
        row.Children.Add(Box("Arrange", rules.Arrange, (r, v) => r with { Arrange = v }));
        row.Children.Add(remove);
        card.Content = row;
        return card;
    }

    /// Adds an app as Contexts does: an installed exe by its file name, a packaged app by
    /// its AppUserModelID. It starts left out of all three, since that's why you add it.
    public void AddApp(Actions.StartApp app)
    {
        var name = app.AppId ?? (app.Exe is { } exe && Actions.AppSwitcher.Resolve(Path.GetFileName(exe)) is not null ? Path.GetFileName(exe) : app.Exe!);
        if (Editor.Draft.Windows.Apps.Any(a => string.Equals(a.Key, name, StringComparison.OrdinalIgnoreCase))) return;
        Change("Add app", c => c.WithAppRules(name, new AppRules(false, false, false)));
    }

    public void RemoveApp(string app) => Change("Remove app", c => c.WithoutAppRules(app));

    /// Checks or clears an app's box, as a click does, for the self-test.
    public void SetAppRule(string app, string label, bool on)
    {
        var card = AppsPanel.Children.OfType<CommunityToolkit.WinUI.Controls.SettingsCard>().FirstOrDefault(c => (string)c.Description == app);
        if (card?.Content is StackPanel row && row.Children.OfType<CheckBox>().FirstOrDefault(b => (string)b.Content == label) is { } box)
        {
            box.IsChecked = on;
            var current = Editor.Draft.Windows.Apps.FirstOrDefault(a => a.Key == app).Value ?? new AppRules();
            Change($"Change {label.ToLowerInvariant()}", c => c.WithAppRules(app, label switch
            {
                "Focus" => current with { Focus = on },
                "Swap" => current with { Swap = on },
                _ => current with { Arrange = on },
            }));
        }
    }

    private async void OnChooseApp(object sender, RoutedEventArgs e)
    {
        if (await AppPicker.ChooseAsync(XamlRoot, ActualTheme) is { } app) AddApp(app);
    }

    private async void OnBrowseApp(object sender, RoutedEventArgs e)
    {
        if (await AppPicker.BrowseAsync(App.MainWindow.Handle()) is { } path) AddApp(new Actions.StartApp(Path.GetFileNameWithoutExtension(path), path, null));
    }

    private void OnBackToggled(object sender, RoutedEventArgs e) =>
        Change(BackToggle.IsOn ? "Turn on Back" : "Turn off Back", c => c.WithRuntime(r => r with { BackKey = BackToggle.IsOn ? "Backspace" : "" }));

    // The cancel and back keys record like a binding's key (S-040).

    public void StartRecording(string which)
    {
        if (!App.CanEdit) return;
        Recording = which;
        App.Keys.Recording = true;
        ShowKeyError(null);
        Refresh();
    }

    public void StopRecording()
    {
        if (Recording is null) return;
        Recording = null;
        App.Keys.Recording = false;
        Refresh();
    }

    public void OnRecorded(Chord chord)
    {
        if (Recording is not { } which) return;
        if (chord is { Modifiers: Modifiers.None, Key: KeyName.Escape } && which == "back") { StopRecording(); return; }
        var runtime = Editor.Draft.Runtime;
        if (SettingsValues.RuntimeKeyProblem(chord, which == "cancel" ? runtime.BackKey : runtime.CancelKey) is { } problem) { ShowKeyError(problem); return; }
        var key = chord.Key.Name();
        Recording = null;
        App.Keys.Recording = false;
        Editor.Change(which == "cancel" ? "Change cancel key" : "Change back key",
            c => c.WithRuntime(r => which == "cancel" ? r with { CancelKey = key } : r with { BackKey = key }));
        Refresh();
    }

    private void ShowKeyError(string? message)
    {
        KeyError.Text = message ?? "";
        KeyError.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRecordCancel(object sender, RoutedEventArgs e) => StartRecording("cancel");

    private void OnRecordBack(object sender, RoutedEventArgs e) => StartRecording("back");

    private void OnKeyLostFocus(object sender, RoutedEventArgs e) => StopRecording();

    private void OnUndo(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (App.CanEdit) Editor.Undo();
    }

    private void OnRedo(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (App.CanEdit) Editor.Redo();
    }

    /// Run at startup (S-054) is Windows' setting, not the config's: no undo, and it
    /// works while the file has errors.
    private void ShowStartup()
    {
        var state = Platform.Startup.State;
        StartupToggle.IsOn = state == Core.StartupState.On;
        StartupAppsLink.Visibility = state == Core.StartupState.TurnedOffInTaskManager ? Visibility.Visible : Visibility.Collapsed;
        StartupCard.Description = state == Core.StartupState.TurnedOffInTaskManager
            ? "Turned off in Startup apps in Windows Settings or Task Manager. Turn it on here, or there."
            : "Start Unnamed Manager when you sign in to Windows.";
    }

    public string StartupLine => StartupCard.Description as string ?? "";

    private void OnStartup(object sender, RoutedEventArgs e)
    {
        if (_showing) return;
        Platform.Startup.Set(StartupToggle.IsOn);
        _showing = true;
        ShowStartup();
        _showing = false;
    }

    private async void OnOpenStartupApps(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));

    private void OnOpenConfigFile(object sender, RoutedEventArgs e) => App.OpenConfigFile();

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => ConfigFiles.OpenLogFolder();
}
