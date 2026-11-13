using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using UnnamedManager.Core.Config;
using UnnamedManager.Tray;

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
        CancelKeyButton.Content = Recording == "cancel" ? "Press a key…" : c.Runtime.CancelKey;
        BackToggle.IsOn = c.Runtime.BackKey.Length > 0;
        BackKeyButton.IsEnabled = BackToggle.IsOn;
        BackKeyButton.Content = Recording == "back" ? "Press a key…" : c.Runtime.BackKey.Length > 0 ? c.Runtime.BackKey : "Off";
        var errors = App.Config.Errors.Concat(Editor.DraftErrors).ToList();
        Problem.IsOpen = errors.Count > 0;
        Problem.Title = App.Config.State.HasErrors ? "The config file has errors" : "This change isn't saved yet";
        Problem.Message = string.Join(" ", errors.Select(e => e.Message));
        foreach (var control in new Control[] { ThemeBox, PositionBox, ShowHudToggle, DelayBox, TimeoutBox, FlashToggle, BellToggle, GapBox, CancelKeyButton, BackToggle })
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

    private void OnOpenConfigFile(object sender, RoutedEventArgs e) => ConfigFiles.OpenFile(App.Config.Path);

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => ConfigFiles.OpenLogFolder();
}
