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

    /// The Held menus rows' text, for the self-test.
    public IEnumerable<string> HeldText => Held.Children.OfType<Border>().Select(b => AutomationProperties.GetName(b));

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

        Held.Children.Clear();
        Held.Children.Add(Described(null, "Hold", "Keep the leader's modifiers down. The Guide shows the menu with its first item highlighted."));
        Held.Children.Add(Described(null, "Repeat key", "Press the key that opened the menu again to move the highlight, as Tab does in Alt+Tab. Down and Up move it too."));
        Held.Children.Add(Described("Shift", "Back a step", "Hold Shift with the repeat key to move the highlight back."));
        Held.Children.Add(Described(null, "Let go", "Let go of a modifier to choose the highlighted item. Press an item's key to highlight it at once."));
        Held.Children.Add(Described(cancel, "Cancel", $"The first row. Let go on it, or press {cancel}, to choose nothing."));

        ShowBuiltIns(config);

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

    /// Which kinds of built-in action are open, kept while the app runs.
    private static readonly HashSet<string> OpenBuiltIns = [];

    /// The built-in actions' rows, by kind, for the self-test: "label|name|overridden".
    public IReadOnlyList<(string Kind, IReadOnlyList<string> Rows)> BuiltInRows { get; private set; } = [];

    /// Built-in actions (spec v11, S-071): a list to open for each kind with values, with
    /// its count, and one card for Fill, Minimize, Show Help and Do Nothing. Names are
    /// monospaced and can be selected and copied. Focus Window and Swap Window end with
    /// their numbered windows, which have no end.
    private void ShowBuiltIns(Config config)
    {
        BuiltIns.Children.Clear();
        var listed = new List<(string, IReadOnlyList<string>)>();
        var singles = new StackPanel { Spacing = UI.Space.Sm };
        var singleRows = new List<string>();
        foreach (var group in BuiltInActions.Groups())
        {
            var rows = group.Entries.Select(e => (Label: group.Entries.Count == 1 ? group.Name : BuiltInActions.ValueLabel(e.Action) ?? group.Name, e.Name)).ToList();
            if (group.Entries[0].Action is FocusAction or SwapAction)
                rows.Add(("Window 1, 2, 3…", $"{BuiltInActions.Prefix(group.Entries[0].Action.Type)}-1"));
            if (group.Entries[0].Action is SpaceSendAction)
                rows.Add(("Desktop 1, 2, 3…", "space-send-1"));
            if (group.Entries.Count == 1)
            {
                foreach (var (label, name) in rows) { singles.Children.Add(BuiltInRow(label, name, config)); singleRows.Add(RowText(label, name, config)); }
                continue;
            }
            var panel = new StackPanel { Spacing = UI.Space.Sm };
            foreach (var (label, name) in rows) panel.Children.Add(BuiltInRow(label, name, config));
            var expander = new Expander
            {
                Header = $"{group.Name} · {group.Entries.Count} actions",
                Content = panel,
                IsExpanded = OpenBuiltIns.Contains(group.Name),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            expander.Expanding += (_, _) => OpenBuiltIns.Add(group.Name);
            expander.Collapsed += (_, _) => OpenBuiltIns.Remove(group.Name);
            BuiltIns.Children.Add(expander);
            listed.Add((group.Name, [.. rows.Select(r => RowText(r.Label, r.Name, config))]));
        }
        BuiltIns.Children.Add(new Border { Child = singles, Style = Resource<Style>("CardStyle") });
        listed.Add(("Other", singleRows));
        BuiltInRows = listed;
    }

    /// Opens one kind's list, as a click does, for the `help-builtin` snapshot.
    public void OpenBuiltIn(string kind)
    {
        OpenBuiltIns.Add(kind);
        foreach (var expander in BuiltIns.Children.OfType<Expander>())
            if (expander.Header is string header && header.StartsWith(kind + " ·")) expander.IsExpanded = true;
    }

    /// How far down the built-in actions start, for the `help-builtin` snapshot.
    public double BuiltInOffset => BuiltIns.TransformToVisual(this).TransformPoint(default).Y - UI.Space.Section * 2;

    private static bool Overridden(string name, Config config) => config.Action(name) is not null;

    private static string RowText(string label, string name, Config config) => $"{label}|{name}|{(Overridden(name, config) ? "overridden" : "")}";

    /// "Left half    window-place-left-half", with "Overridden by your action" under the name when it is.
    private static Grid BuiltInRow(string label, string name, Config config)
    {
        var grid = new Grid { ColumnSpacing = UI.Space.Md };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var names = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        names.Children.Add(new TextBlock
        {
            Text = name,
            FontFamily = Resource<Microsoft.UI.Xaml.Media.FontFamily>("CodeFontFamily"),
            IsTextSelectionEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Right,
        });
        if (Overridden(name, config))
            names.Children.Add(new TextBlock { Text = "Overridden by your action", Style = Resource<Style>("FieldDescriptionTextStyle"), HorizontalAlignment = HorizontalAlignment.Right });
        Add(grid, names, 1);
        AutomationProperties.SetName(grid, $"{label}, {name}{(Overridden(name, config) ? ", overridden by your action" : "")}");
        return grid;
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
