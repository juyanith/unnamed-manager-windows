using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnnamedManager.Pages;

public sealed partial class SettingsPage : Page
{

    public SettingsPage()
    {
        InitializeComponent();


        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        AboutCard.Description = $"Version {version?.Split('+')[0]}. Your bindings are in {App.Config.Path}.";
    }

    /// Opens the config file's folder with the file selected.
    private void OnOpenConfigFolder(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{App.Config.Path}\"") { UseShellExecute = true });
}
