using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core;
using UnnamedManager.UI;
using Windows.Graphics;

namespace UnnamedManager.Welcome;

/// Welcome (S-058): a small window on first start with the three steps that finish
/// setup. It shows at each start until you've run one sequence; Later only closes it.
/// Its height follows its content, and it can't be resized.
public sealed partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WelcomeTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        ConfigPath.Text = App.Config.Path;
        ApplyTheme(App.ThemeMode);
        Refresh();
        Root.Loaded += (_, _) => SizeToContent();
        Closed += (_, _) => App.Keys.Effect -= OnEffect;
        App.Keys.Effect += OnEffect;
    }

    public WelcomeStatus Status { get; private set; } = new(false, true, false);

    public FrameworkElement RootElement => Root;

    public string CloseText => (string)CloseButton.Content;

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

    /// Shows each step's mark from what's done now.
    public void Refresh() => Show(new WelcomeStatus(Prefs.Current.OpenedConfig, Platform.OfficeShortcuts.On, Prefs.Current.RanSequence));

    /// Shows a status as it is; snapshots pass one of their own.
    public void Show(WelcomeStatus status)
    {
        Status = status;
        ConfigMark.Content = Mark(1, status.OpenedConfig);
        OfficeMark.Content = Mark(2, status.OfficeDone);
        TryMark.Content = Mark(3, status.RanSequence);
        OfficeButton.Visibility = status.OfficeDone ? Visibility.Collapsed : Visibility.Visible;
        OfficeLine.Text = status.OfficeShortcutsOn switch
        {
            true => "Windows opens Office on Hyper and on Hyper+W, X, P, O, T and other letters. Keep them for your bindings.",
            false => "Hyper and Hyper with a letter are yours. Turn them back on from the tray menu.",
            null => "Office shortcuts open an app of your own, so Unnamed Manager leaves them as they are.",
        };
        var (title, detail) = WelcomeStatus.TryStep(App.Config.Current);
        TryTitle.Text = title;
        TryDetail.Text = status.RanSequence ? "Done. Leaders work in every app." : detail;
        CloseButton.Content = status.IsComplete ? "Done" : "Later";
        CloseButton.Style = status.IsComplete ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
    }

    /// A step's number, or a check once it's done; Narrator reads which.
    private static FrameworkElement Mark(int step, bool done)
    {
        FrameworkElement mark = done
            ? new FontIcon { Style = (Style)Application.Current.Resources["WelcomeDoneGlyphStyle"] }
            : new TextBlock { Text = step.ToString(), Style = (Style)Application.Current.Resources["WelcomeStepNumberStyle"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mark, done ? $"Step {step}, done" : $"Step {step}");
        return mark;
    }

    /// The window as tall as its content, at the Welcome width, centered on its display.
    private void SizeToContent()
    {
        var width = (double)Application.Current.Resources["WelcomeWidth"];
        Root.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        var scale = this.Scale();
        AppWindow.ResizeClient(new SizeInt32((int)Math.Round(width * scale), (int)Math.Ceiling(Root.DesiredSize.Height * scale)));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Move(new PointInt32(area.X + (area.Width - AppWindow.Size.Width) / 2, area.Y + (area.Height - AppWindow.Size.Height) / 2));
    }

    // A sequence that runs a binding finishes the third step, here as in App.
    private void OnEffect(Core.Keyboard.SequenceEffect effect)
    {
        if (effect is Core.Keyboard.SequenceEffect.Run) DispatcherQueue.TryEnqueue(Refresh);
    }

    private void OnOpenConfigFile(object sender, RoutedEventArgs e)
    {
        App.OpenConfigFile();
        Refresh();
    }

    private void OnTurnOffOffice(object sender, RoutedEventArgs e)
    {
        Platform.OfficeShortcuts.Set(false);
        Refresh();
    }

    private async void OnOpenTaskbarSettings(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:taskbar"));

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
