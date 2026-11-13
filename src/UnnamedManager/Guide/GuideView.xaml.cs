using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;

namespace UnnamedManager.Guide;

/// One part of the path: a menu title, with a › before every part but the first.
public sealed record PathPart(string Title, bool First)
{
    public Visibility SeparatorVisibility => First ? Visibility.Collapsed : Visibility.Visible;
}

/// One row: the key in a keycap, the title, and a › on rows that open a menu. A
/// Context Aware binding with nothing to run for the app in front is dimmed and says
/// so in words: "not available in Notepad" (S-014).
public sealed partial class GuideRow(KeyName key, string title, bool isMenu, string? unavailableIn = null) : INotifyPropertyChanged
{
    public enum RowKind { Entry, Header, Separator }

    /// A group's header in Home ("Meh"), or the line under Home's current group (S-021).
    public static GuideRow Header(string title) => new(KeyName.A, title, false) { Kind = RowKind.Header };
    public static GuideRow Separator() => new(KeyName.A, "", false) { Kind = RowKind.Separator };

    public RowKind Kind { get; private init; } = RowKind.Entry;
    public Visibility EntryVisibility => Kind == RowKind.Entry ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HeaderVisibility => Kind == RowKind.Header ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SeparatorVisibility => Kind == RowKind.Separator ? Visibility.Visible : Visibility.Collapsed;

    public string Key { get; } = key.Label();

    /// The action's glyph, or "" (S-045). When any row in a column has one, every row
    /// in it keeps the space, so the titles line up.
    public string Glyph { get; init; } = "";
    public Visibility IconVisibility { get; set; } = Visibility.Collapsed;
    public string Title { get; } = title;
    public Visibility ChevronVisibility => isMenu ? Visibility.Visible : Visibility.Collapsed;
    public bool IsAvailable => unavailableIn is null;
    public Visibility AvailableVisibility => IsAvailable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UnavailableVisibility => IsAvailable ? Visibility.Collapsed : Visibility.Visible;
    public string UnavailableNote => unavailableIn is null ? "" : $"not available in {unavailableIn}";
    /// What Narrator reads: the key in words, the full title, "menu" for menus, and why a row is dimmed.
    public string Spoken { get; } = $"{key.SpokenName()}, {title}{(isMenu ? ", menu" : "")}{(unavailableIn is null ? "" : $", not available in {unavailableIn}")}";

    /// Every keycap in a column is as wide as its widest, so the titles line up.
    public double KeyWidth
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new(nameof(KeyWidth))); }
    } = 0;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record GuideColumn(IReadOnlyList<GuideRow> Rows);

public sealed partial class GuideView : UserControl
{
    /// Up to this many rows make one column (docs/DESIGN.md, The Guide).
    public const int RowsPerColumn = 12;
    public const int MaxColumns = 3;

    public GuideView()
    {
        InitializeComponent();
    }

    public FrameworkElement RootElement => Root;

    /// The rows split into columns: up to 12 rows make one column, up to 24
    /// two, more three, each as tall as the others give or take one.
    public static IReadOnlyList<GuideColumn> Columns(IReadOnlyList<GuideRow> rows)
    {
        var columns = Math.Clamp((rows.Count + RowsPerColumn - 1) / RowsPerColumn, 1, MaxColumns);
        var perColumn = (rows.Count + columns - 1) / columns;
        return WithIconSpace(Enumerable.Range(0, columns)
            .Select(c => new GuideColumn(rows.Skip(c * perColumn).Take(perColumn).ToList()))
            .Where(c => c.Rows.Count > 0)
            .ToList());
    }

    /// Every entry row in a column with a glyph keeps the icon space.
    public static IReadOnlyList<GuideColumn> WithIconSpace(IReadOnlyList<GuideColumn> columns)
    {
        foreach (var column in columns)
        {
            var space = column.Rows.Any(r => r.Glyph.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
            foreach (var row in column.Rows) row.IconVisibility = space;
        }
        return columns;
    }

    private static string Glyph(BindingNode node, LoadedConfig config) =>
        node.Action is { } id ? ActionGlyphs.For(config.Config.Action(id)) ?? "" : "";

    /// Shows a menu: its path and one row per child, in file order.
    public void Show(BindingNode menu, IReadOnlyList<string> path, LoadedConfig config, FrontApp? front = null)
    {
        ShowFailureMode(false);
        PathItems.ItemsSource = path.Select((title, i) => new PathPart(title, i == 0)).ToList();
        var rows = menu.Children.Select(c => new GuideRow(c.Path.LastStep!.Value.Key, c.Title, c.IsMenu,
            config.IsAvailable(c, front) ? null : front?.Name ?? "this app") { Glyph = Glyph(c, config) }).ToList();
        ColumnItems.ItemsSource = Columns(rows);
        CancelKey.Text = config.CancelKey.Label();
        BackHint.Visibility = config.BackKey is null ? Visibility.Collapsed : Visibility.Visible;
        if (config.BackKey is { } back) BackKey.Text = back.Label();
        AutomationProperties.SetName(Root, string.Join(" › ", path));
    }

    /// Shows Home (S-021): with 12 rows or fewer, one column with the current group, a
    /// line, then each other group under its header; with more, the groups side by side,
    /// each under its header, never split.
    public void ShowHome(Home home, IReadOnlyList<string> path, LoadedConfig config, FrontApp? front = null)
    {
        ShowFailureMode(false);
        PathItems.ItemsSource = path.Select((title, i) => new PathPart(title, i == 0)).ToList();
        GuideRow Row(BindingNode e) => new(e.Path.Leader.Key, e.Title, e.IsMenu, config.IsAvailable(e, front) ? null : front?.Name ?? "this app") { Glyph = Glyph(e, config) };
        var count = home.Groups.Sum(g => g.Entries.Count);
        ColumnItems.ItemsSource = WithIconSpace(HomeColumns(home, Row, count <= RowsPerColumn));
        CancelKey.Text = config.CancelKey.Label();
        BackHint.Visibility = config.BackKey is null ? Visibility.Collapsed : Visibility.Visible;
        if (config.BackKey is { } back) BackKey.Text = back.Label();
        AutomationProperties.SetName(Root, string.Join(" › ", path));
    }

    public static IReadOnlyList<GuideColumn> HomeColumns(Home home, Func<BindingNode, GuideRow> row, bool oneColumn)
    {
        if (!oneColumn)
            return home.Groups.Select(g => new GuideColumn([GuideRow.Header(g.Title), .. g.Entries.Select(row)])).ToList();
        var rows = new List<GuideRow>();
        foreach (var (group, i) in home.Groups.Select((g, i) => (g, i)))
        {
            if (i == 1) rows.Add(GuideRow.Separator());
            if (i > 0) rows.Add(GuideRow.Header(group.Title));
            rows.AddRange(group.Entries.Select(row));
        }
        return [new GuideColumn(rows)];
    }

    /// Shows why an action failed, on one warning line with nothing else (S-016).
    public void ShowFailure(string message)
    {
        FailureText.Text = message;
        ShowFailureMode(true);
        AutomationProperties.SetName(Root, message);
    }

    private void ShowFailureMode(bool failure)
    {
        FailureLine.Visibility = failure ? Visibility.Visible : Visibility.Collapsed;
        var rest = failure ? Visibility.Collapsed : Visibility.Visible;
        PathItems.Visibility = rest;
        ColumnItems.Visibility = rest;
        Footer.Visibility = rest;
    }

    public string? FailureMessage => FailureLine.Visibility == Visibility.Visible ? FailureText.Text : null;

    /// The accent outline for a key that isn't bound (S-033).
    public bool IsFlashing
    {
        get => FlashOutline.Visibility == Visibility.Visible;
        set => FlashOutline.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// Lines up the keycaps in each column, once the rows are laid out.
    public void AlignKeycaps()
    {
        if (ColumnItems.ItemsSource is not IReadOnlyList<GuideColumn> columns) return;
        foreach (var column in columns)
        {
            var widest = 0.0;
            foreach (var row in column.Rows)
            {
                var text = new TextBlock { Text = row.Key, Style = (Style)Application.Current.Resources["KeyCapTextStyle"] };
                text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                widest = Math.Max(widest, text.DesiredSize.Width);
            }
            var padding = (Thickness)Application.Current.Resources["KeyCapPadding"];
            var border = (Thickness)Application.Current.Resources["KeyCapBorderThickness"];
            var width = Math.Ceiling(widest + padding.Left + padding.Right + border.Left + border.Right);
            foreach (var row in column.Rows) row.KeyWidth = width;
        }
    }

    /// Solid instead of acrylic, for transparency effects off or a contrast theme.
    public void UseSolidBackground(bool solid) => SolidBackground.Visibility = solid ? Visibility.Visible : Visibility.Collapsed;

    /// Tells Narrator where the sequence is, without moving its focus: "Applications, 3 keys".
    public void Announce(string text)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(Root) ?? FrameworkElementAutomationPeer.CreatePeerForElement(Root);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "GuideMenu");
    }
}
