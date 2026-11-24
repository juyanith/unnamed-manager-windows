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
    public enum RowKind { Entry, Header, Separator, Empty }

    /// What a list with nothing in it says, in secondary text (S-100).
    public static GuideRow Empty() => new(KeyName.A, GuideSpeech.EmptyList, false) { Kind = RowKind.Empty };

    /// A group's header in Home ("Meh"), or the line under Home's current group (S-021).
    public static GuideRow Header(string title) => new(KeyName.A, title, false) { Kind = RowKind.Header };
    public static GuideRow Separator() => new(KeyName.A, "", false) { Kind = RowKind.Separator };

    public RowKind Kind { get; private init; } = RowKind.Entry;
    public Visibility EntryVisibility => Kind == RowKind.Entry ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HeaderVisibility => Kind == RowKind.Header ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SeparatorVisibility => Kind == RowKind.Separator ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => Kind == RowKind.Empty ? Visibility.Visible : Visibility.Collapsed;

    public string Key { get; init; } = key.Label();
    /// Cancel in a held menu has no key, so no keycap (spec v18).
    /// Its keycap keeps its space, so the icon and title line up with the rows below.
    public double KeyOpacity => Key.Length > 0 ? 1 : 0;

    /// The item letting go chooses in a held menu (spec v18): filled with the accent.
    public bool IsHighlighted { get; init; }
    public Visibility HighlightVisibility => IsHighlighted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlainVisibility => IsHighlighted ? Visibility.Collapsed : Visibility.Visible;

    /// What a list's item says about its window: "minimized", "on another desktop" (spec v18).
    public string Note { get; init; } = "";
    public Visibility NoteVisibility => Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// The action's glyph, or "" (S-045). When any row in a column has one, every row
    /// in it keeps the space, so the titles line up.
    public string Glyph { get; init; } = "";

    /// The app's, file's or URL's icon, or null (S-060). Decorative: Narrator reads the row as before.
    public Microsoft.UI.Xaml.Media.ImageSource? Icon { get; init; }

    /// Whether the column keeps the icon space; the glyph fills it unless there's an icon.
    public Visibility IconVisibility { get; set; } = Visibility.Collapsed;
    /// The app's glyph, on a menu with no icon of its own (S-094).
    public bool IsMarked { get; init; }
    private bool ShowsMark => IconVisibility == Visibility.Visible && Icon is null && IsMarked;
    public Visibility MarkVisibility => ShowsMark && !IsHighlighted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HighlightedMarkVisibility => ShowsMark && IsHighlighted ? Visibility.Visible : Visibility.Collapsed;

    private bool ShowsGlyph => IconVisibility == Visibility.Visible && Icon is null && !IsMarked;
    public Visibility GlyphVisibility => ShowsGlyph && !IsHighlighted ? Visibility.Visible : Visibility.Collapsed;
    /// On the highlight, the glyph takes the highlight's text color (S-093).
    public Visibility HighlightedGlyphVisibility => ShowsGlyph && IsHighlighted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ImageVisibility => IconVisibility == Visibility.Visible && Icon is not null ? Visibility.Visible : Visibility.Collapsed;
    public string Title { get; } = title;
    public Visibility ChevronVisibility => isMenu ? Visibility.Visible : Visibility.Collapsed;
    public bool IsAvailable => unavailableIn is null;
    public Visibility AvailableVisibility => IsAvailable && !IsHighlighted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HighlightedTitleVisibility => IsAvailable && IsHighlighted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UnavailableVisibility => IsAvailable ? Visibility.Collapsed : Visibility.Visible;
    public string UnavailableNote => unavailableIn is null ? "" : $"not available in {unavailableIn}";
    /// What Narrator reads: the key in words, the full title, "menu" for menus, and why a row is dimmed.
    public string Spoken { get; init; } = $"{key.SpokenName()}, {title}{(isMenu ? ", menu" : "")}{(unavailableIn is null ? "" : $", not available in {unavailableIn}")}";

    /// Every keycap in a column is as wide as its widest, so the titles line up.
    public double KeyWidth
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new(nameof(KeyWidth))); }
    } = 0;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record GuideColumn(IReadOnlyList<GuideRow> Rows)
{
    /// A line follows Home's first column (S-098).
    public bool Divided { get; init; }
    public Visibility DividerVisibility => Divided ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class GuideView : UserControl
{
    /// Up to this many rows make one column (docs/DESIGN.md, The Guide).
    public const int RowsPerColumn = HomeLayout.RowsPerColumn;
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

    /// Every entry row in a column with a glyph or an icon keeps the icon space.
    public static IReadOnlyList<GuideColumn> WithIconSpace(IReadOnlyList<GuideColumn> columns)
    {
        foreach (var column in columns)
        {
            var space = column.Rows.Any(r => r.Glyph.Length > 0 || r.Icon is not null || r.IsMarked) ? Visibility.Visible : Visibility.Collapsed;
            foreach (var row in column.Rows) row.IconVisibility = space;
        }
        return columns;
    }

    private static string Glyph(BindingNode node, LoadedConfig config) => GuideIconCache.Row(node, config).Glyph;

    private static Microsoft.UI.Xaml.Media.ImageSource? Icon(BindingNode node, LoadedConfig config) => GuideIconCache.Row(node, config).Image;

    /// The menu on screen, and its highlight while held (spec v18).
    public KeyPath? ShownMenu { get; private set; }
    public int? Highlight { get; private set; }
    /// What Cancel's row says: the focused window's title in a held list, else "Cancel".
    public string CancelTitle { get; private set; } = "Cancel";

    /// The rows on screen, for the self-test.
    public IReadOnlyList<GuideRow> Rows => ColumnItems.ItemsSource is IReadOnlyList<GuideColumn> columns ? [.. columns.SelectMany(c => c.Rows)] : [];

    /// The columns on screen, for the self-test.
    public IReadOnlyList<GuideColumn> ShownColumns => ColumnItems.ItemsSource as IReadOnlyList<GuideColumn> ?? [];

    /// Whether the columns end inside the Guide, for the self-test.
    public bool ColumnsFit =>
        ColumnItems.TransformToVisual(this).TransformBounds(new Windows.Foundation.Rect(0, 0, ColumnItems.ActualWidth, ColumnItems.ActualHeight)).Right <= ActualWidth + 1;

    /// "minimized, on another desktop".
    public static string Note(ItemMarks marks) => string.Join(", ", new[]
    {
        marks.HasFlag(ItemMarks.Minimized) ? "minimized" : null,
        marks.HasFlag(ItemMarks.OtherDesktop) ? "on another desktop" : null,
        marks.HasFlag(ItemMarks.OtherDisplay) ? "on another display" : null,
    }.OfType<string>());

    private static Microsoft.UI.Xaml.Media.ImageSource? AppIcon(ListItem? item) =>
        item?.App is { } app ? GuideIconCache.Cached(new Core.Config.IconSource.Exe(app)) : null;

    /// Shows a menu: its path and one row per child, in file order. A held menu
    /// (`highlight` set, spec v18) starts with Cancel and fills the highlighted row; a
    /// list's items show their windows' titles, app icons and where they are.
    public void Show(BindingNode menu, IReadOnlyList<string> path, LoadedConfig config, FrontApp? front = null, int? highlight = null)
    {
        ShowFailureMode(false);
        ShownMenu = menu.Path;
        Highlight = highlight;
        PathItems.ItemsSource = path.Select((title, i) => new PathPart(title, i == 0)).ToList();
        var rows = menu.Children.Select((c, i) => new GuideRow(c.Path.LastStep!.Value.Key, c.Title, c.IsMenu,
            config.IsAvailable(c, front) ? null : front?.Name ?? "this app")
        {
            Glyph = c.Item is null ? Glyph(c, config) : "",
            IsMarked = GuideIcons.ShowsAppMark(c),
            Icon = c.Item is { } item ? AppIcon(item) : Icon(c, config),
            Note = c.Item is { } marked ? Note(marked.Marks) : "",
            IsHighlighted = highlight == i + 1,
        }).ToList();
        // A chevron, as menus have: 0 turns the page and picks no window (S-101).
        if (menu.HasMore) rows.Add(new GuideRow(KeyName.D0, "More", true));
        if (highlight is not null)
        {
            // Cancel comes first and has no key; in a held list it's the focused window.
            CancelTitle = menu.List is not null && menu.Front is { } focused ? focused.Title : "Cancel";
            rows.Insert(0, new GuideRow(KeyName.A, CancelTitle, false)
            {
                Key = "", Spoken = CancelTitle == "Cancel" ? "Cancel" : $"Cancel, {CancelTitle}",
                Icon = menu.List is not null ? AppIcon(menu.Front) : null, Glyph = menu.List is null || menu.Front is null ? "" : "",
                IsHighlighted = highlight == 0,
            });
        }
        else if (menu.List is not null && menu.Items.Count == 0)
            rows.Add(GuideRow.Empty());
        ColumnItems.ItemsSource = Columns(rows);
        HoldHint.Visibility = highlight is null ? Visibility.Collapsed : Visibility.Visible;
        CancelKey.Text = config.CancelKey.Label();
        BackHint.Visibility = config.BackKey is null ? Visibility.Collapsed : Visibility.Visible;
        if (config.BackKey is { } back) BackKey.Text = back.Label();
        AutomationProperties.SetName(Root, string.Join(" › ", path));
    }

    /// Shows Home (S-021): with 12 rows or fewer, one column with the current group, a
    /// line, then each other group under its header; with more, the current group's column,
    /// a divider, then the others packed into columns, never split (S-098).
    public void ShowHome(Home home, IReadOnlyList<string> path, LoadedConfig config, FrontApp? front = null)
    {
        ShowFailureMode(false);
        ShownMenu = null;
        Highlight = null;
        HoldHint.Visibility = Visibility.Collapsed;
        PathItems.ItemsSource = path.Select((title, i) => new PathPart(title, i == 0)).ToList();
        GuideRow Row(BindingNode e) => new(e.Path.Leader.Key, e.Title, e.IsMenu, config.IsAvailable(e, front) ? null : front?.Name ?? "this app") { Glyph = Glyph(e, config), Icon = Icon(e, config), IsMarked = GuideIcons.ShowsAppMark(e) };
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
        {
            var packed = HomeLayout.Pack([.. home.Groups.Select(g => g.Entries.Count)]);
            return packed.Select((groups, i) => new GuideColumn(
                [.. groups.SelectMany(g => home.Groups[g].Entries.Select(row).Prepend(GuideRow.Header(home.Groups[g].Title)))])
            {
                Divided = i == 0 && packed.Count > 1,
            }).ToList();
        }
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
        ColumnBox.Visibility = rest;
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
        Speaker()?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "GuideMenu");
    }

    /// Whether there's a peer for Announce to speak through.
    internal bool CanAnnounce => Speaker() is not null;

    private AutomationPeer? Speaker() =>
        FrameworkElementAutomationPeer.FromElement(Root) ?? FrameworkElementAutomationPeer.CreatePeerForElement(Root);
}
