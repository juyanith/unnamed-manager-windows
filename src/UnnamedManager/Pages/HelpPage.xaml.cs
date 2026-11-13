using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;
using UnnamedManager.Tray;

namespace UnnamedManager.Pages;

/// The Help page (S-050): your leaders and direct bindings grouped as Home groups
/// them, the keys that work in every sequence, the words, and where to read more.
/// Rows are buttons: clicking one shows it selected on the Bindings page.
public sealed partial class HelpPage : Page
{
    public static readonly Uri Documentation = new("https://github.com/juyanith/unnamed-manager-windows#readme");
    public static readonly Uri Problems = new("https://github.com/juyanith/unnamed-manager-windows/issues");

    public HelpPage()
    {
        InitializeComponent();
        Docs.NavigateUri = Documentation;
        Issues.NavigateUri = Problems;
        Version.Text = App.VersionLine;
        Loaded += (_, _) => { App.Editor.Changed += Refresh; App.Config.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { App.Editor.Changed -= Refresh; App.Config.Changed -= Refresh; };
    }

    /// The key rows, by group title, for the self-test.
    public IReadOnlyList<(string Group, Button Row, KeyPath Path)> Rows { get; private set; } = [];

    /// The In a sequence rows' text, for the self-test.
    public IEnumerable<string> SequenceText => Sequence.Children.OfType<Border>().Select(b => AutomationProperties.GetName(b));

    public void Refresh()
    {
        var config = App.Editor.Draft;
        var leaders = config.Tree();
        Empty.Visibility = leaders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        KeysFooter.Visibility = leaders.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Keys.Children.Clear();
        var rows = new List<(string, Button, KeyPath)>();
        if (leaders.Count > 0)
        {
            var home = new Home(leaders, leaders[0].Path.Leader.Modifiers);
            foreach (var group in home.Groups)
            {
                var title = $"Your keys: {group.Title}";
                Keys.Children.Add(new TextBlock { Text = title, Style = Resource<Style>("BodyStrongTextBlockStyle"), Margin = Keys.Children.Count == 0 ? new Thickness(0) : Resource<Thickness>("SettingsGroupMargin") });
                foreach (var entry in group.Entries)
                {
                    var row = KeyRow(entry);
                    rows.Add((group.Title, row, entry.Path));
                    Keys.Children.Add(row);
                }
            }
        }
        Rows = rows;

        var cancel = KeyName(config.Runtime.CancelKey);
        var back = config.Runtime.BackKey.Length > 0 ? KeyName(config.Runtime.BackKey) : null;
        Sequence.Children.Clear();
        Sequence.Children.Add(Described(cancel, "Cancel", "Ends the sequence without running anything."));
        if (back is not null)
            Sequence.Children.Add(Described(back, "Back", leaders.Count >= 2
                ? "Goes up one menu. In a leader's menu, it opens Home, which lists every leader and direct binding."
                : "Goes up one menu. In a leader's menu, it ends the sequence."));
        Sequence.Children.Add(Described(null, "Guide", "Pause during a sequence and the Guide shows where you are and which keys come next."));

        if (Words.Children.Count > 0) return;
        foreach (var (term, meaning) in new[]
        {
            ("Leader", "A chord such as Hyper+Space that starts a sequence and opens its menu."),
            ("Menu", "A group of keys inside a sequence. It opens more choices and runs nothing itself."),
            ("Binding", "A key that runs an action, such as E for Editor."),
            ("Direct binding", "A chord such as Hyper+T that runs its action at once, with no sequence."),
            ("Action", "What a binding does: use an app, open a file or link, run a command, send a shortcut, or move a window."),
            ("Context", "A set of apps, such as your browsers, that a Context Aware action checks to pick what it does."),
        })
            Words.Children.Add(Described(null, term, meaning));
    }

    private static string KeyName(string name) => Core.Config.Keys.ParseKey(name) is { } key ? key.Label() : name;

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];

    /// A leader or direct binding: its key, its title, and which one it is.
    private Button KeyRow(BindingNode entry)
    {
        var kind = entry.IsMenu ? "Leader" : "Direct binding";
        var grid = new Grid { ColumnSpacing = UI.Space.Md };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(KeyCap(entry.Path.Leader.ToString()));
        Add(grid, new TextBlock { Text = entry.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, 1);
        Add(grid, new TextBlock { Text = kind, Style = Resource<Style>("FieldDescriptionTextStyle"), VerticalAlignment = VerticalAlignment.Center }, 2);
        Add(grid, new FontIcon { Glyph = "", FontSize = Resource<double>("GuideChevronSize"), Style = Resource<Style>("SecondaryGlyphStyle") }, 3);
        var row = new Button { Content = grid, Style = Resource<Style>("HelpRowButtonStyle") };
        AutomationProperties.SetName(row, $"{BindingLabels.Spoken(entry, kind.ToLowerInvariant())}");
        ToolTipService.SetToolTip(row, "Show it on the Bindings page");
        row.Click += (_, _) => App.MainWindow.ShowBinding(entry.Path);
        return row;
    }

    /// A card with an optional keycap and a title, then a line in secondary text.
    private static Border Described(string? key, string title, string text)
    {
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UI.Space.Sm };
        if (key is not null) top.Children.Add(KeyCap(key));
        top.Children.Add(new TextBlock { Text = title, Style = Resource<Style>("BodyStrongTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center });
        var panel = new StackPanel { Spacing = UI.Space.Xs };
        panel.Children.Add(top);
        panel.Children.Add(new TextBlock { Text = text, Style = Resource<Style>("FieldDescriptionTextStyle") });
        var card = new Border { Child = panel, Style = Resource<Style>("CardStyle") };
        AutomationProperties.SetName(card, key is null ? $"{title}: {text}" : $"{title}, {key}: {text}");
        return card;
    }

    private static Border KeyCap(string label) =>
        new() { Style = Resource<Style>("KeyCapStyle"), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = label, Style = Resource<Style>("KeyCapTextStyle") } };

    private static void Add(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private void OnOpenFile(object sender, RoutedEventArgs e) => ConfigFiles.OpenFile(App.Config.Path);

    private void OnNewBinding(object sender, RoutedEventArgs e)
    {
        App.MainWindow.Show("bindings");
        App.MainWindow.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            (App.MainWindow.CurrentPage as BindingsPage)?.NewBinding());
    }
}
