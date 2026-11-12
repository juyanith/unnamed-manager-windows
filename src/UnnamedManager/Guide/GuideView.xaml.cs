using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using UnnamedManager.Core.Config;

namespace UnnamedManager.Guide;

/// One part of the path: a menu title, with a › before every part but the first.
public sealed record PathPart(string Title, bool First)
{
    public Visibility SeparatorVisibility => First ? Visibility.Collapsed : Visibility.Visible;
}

/// One row: the key in a keycap, the title, and a › on rows that open a menu.
public sealed partial class GuideRow(KeyName key, string title, bool isMenu) : INotifyPropertyChanged
{
    public string Key { get; } = key.Label();
    public string Title { get; } = title;
    public Visibility ChevronVisibility => isMenu ? Visibility.Visible : Visibility.Collapsed;
    /// What Narrator reads: the key in words, the full title, and "menu" for menus.
    public string Spoken { get; } = $"{key.SpokenName()}, {title}{(isMenu ? ", menu" : "")}";

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
        return Enumerable.Range(0, columns)
            .Select(c => new GuideColumn(rows.Skip(c * perColumn).Take(perColumn).ToList()))
            .Where(c => c.Rows.Count > 0)
            .ToList();
    }

    /// Shows a menu: its path and one row per child, in file order.
    public void Show(BindingNode menu, IReadOnlyList<string> path, LoadedConfig config)
    {
        PathItems.ItemsSource = path.Select((title, i) => new PathPart(title, i == 0)).ToList();
        var rows = menu.Children.Select(c => new GuideRow(c.Path.LastStep!.Value.Key, c.Title, c.IsMenu)).ToList();
        ColumnItems.ItemsSource = Columns(rows);
        CancelKey.Text = config.CancelKey.Label();
        BackHint.Visibility = config.BackKey is null ? Visibility.Collapsed : Visibility.Visible;
        if (config.BackKey is { } back) BackKey.Text = back.Label();
        AutomationProperties.SetName(Root, string.Join(" › ", path));
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
