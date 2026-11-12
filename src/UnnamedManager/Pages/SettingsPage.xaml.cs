using System.Diagnostics;
using System.Reflection;
using UnnamedManager.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnnamedManager.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();

        foreach (var (id, name) in Theme.Modes) ThemeBox.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        ThemeBox.SelectedIndex = Math.Max(0, Array.FindIndex(Theme.Modes, m => m.Id == Prefs.Current.Theme));

        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        AboutCard.Description = $"Version {version?.Split('+')[0]}. Your data stays on this PC.";
        _loading = false;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        Prefs.Current.Theme = id;
        if (!AppPaths.IsOverridden) Prefs.Current.Save();
        App.MainWindow.ApplyTheme(id);
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", System.IO.Path.GetDirectoryName(AppPaths.Store)!) { UseShellExecute = true });
}
