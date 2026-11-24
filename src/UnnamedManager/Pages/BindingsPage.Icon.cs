using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using UnnamedManager.Core.Config;
using UnnamedManager.Guide;
using UnnamedManager.UI;
using IconSource = UnnamedManager.Core.Config.IconSource;

namespace UnnamedManager.Pages;

/// The Bindings page's Icon field (S-095): a code point or a path, with common glyphs,
/// Browse and Automatic beside it.
public sealed partial class BindingsPage
{
    /// The menu's glyphs: ones that tell rows apart at 20 epx.
    private static readonly (string Name, string CodePoint)[] CommonGlyphs =
    [
        ("Home", "U+E80F"), ("Star", "U+E734"), ("Mail", "U+E715"), ("Calendar", "U+E787"), ("Globe", "U+E774"),
        ("Folder", "U+E8B7"), ("Document", "U+E8A5"), ("Music", "U+E8D6"), ("Search", "U+E721"), ("Settings", "U+E713"),
        ("Code", "U+E943"), ("Terminal", "U+E756"), ("People", "U+E716"), ("Games", "U+E7FC"),
    ];

    private void FillIconMenu()
    {
        foreach (var (name, codePoint) in CommonGlyphs)
        {
            var item = new MenuFlyoutItem { Text = name, Tag = codePoint };
            if (GuideIcons.Own(codePoint, GuideIconCache.Home) is IconSource.Glyph glyph) item.Icon = new FontIcon { Glyph = glyph.Text };
            item.Click += (_, _) => SetIcon(codePoint);
            IconMenu.Items.Add(item);
        }
        IconMenu.Items.Add(new MenuFlyoutSeparator());
        var browse = new MenuFlyoutItem { Text = "Browse…" };
        browse.Click += async (_, _) =>
        {
            var path = Selected?.Node.Path;
            if (await AppPicker.BrowseIconAsync(App.MainWindow.Handle()) is { } file && Equals(Selected?.Node.Path, path)) SetIcon(file);
        };
        IconMenu.Items.Add(browse);
        var automatic = new MenuFlyoutItem { Text = "Automatic" };
        automatic.Click += (_, _) => SetIcon("");
        IconMenu.Items.Add(automatic);
    }

    /// Sets the selected entry's icon. "" is Automatic, which leaves `icon` out of the file.
    /// One the spec doesn't allow isn't saved and says why.
    public void SetIcon(string text)
    {
        if (!CanEdit || Selected is not { } item) return;
        text = text.Trim();
        var valid = text.Length == 0 || ConfigValidator.IsWindowsIcon(text);
        IconError.Text = valid ? "" : "Use a code point such as U+E756, or a path that starts with a drive, \\\\ or ~.";
        IconError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (!valid) return;
        var path = item.Node.Path;
        string? icon = text.Length == 0 ? null : text;
        if (icon == item.Node.Icon) { IconBox.Text = text; return; }
        Editor.Change("Change icon", c => c.SetIcon(path, icon));
        Refresh(path);
    }

    /// The row the Icon box shows, and its text as the box gave it back.
    private (KeyPath Path, string Text)? _iconShown;

    private void ShowIcon(BindingItem item)
    {
        IconBox.Text = item.Node.Icon ?? "";
        _iconShown = (item.Node.Path, IconBox.Text);
        IconBox.IsEnabled = IconButton.IsEnabled = CanEdit;
        IconError.Visibility = Visibility.Collapsed;
        _ = ShowPreviewAsync(item.Node);
    }

    /// What the Guide shows for the row, once it's looked up, and what's missing.
    private async Task ShowPreviewAsync(BindingNode node)
    {
        IconGlyph.Glyph = "";
        IconImage.Source = null;
        IconMark.Visibility = Visibility.Collapsed;
        IconMissing.Visibility = Visibility.Collapsed;
        if (App.Config.Current is not { } loaded) return;
        // The draft's actions, which may not be saved yet.
        var config = loaded with { Config = Editor.Draft };
        var source = GuideIcons.For(node, config.Config, GuideIconCache.Home);
        if (source is not null)
        {
            GuideIconCache.Load([source]);
            for (var i = 0; i < 60 && !GuideIconCache.Known(source); i++) await Task.Delay(50);
            // The card moved on meanwhile.
            if (Selected?.Node is not { } now || !now.Path.Equals(node.Path) || now.Icon != node.Icon || now.Action != node.Action) return;
        }
        var (glyph, image) = GuideIconCache.Row(node, config);
        var marked = GuideIcons.ShowsAppMark(node);
        IconGlyph.Glyph = image is null ? glyph : "";
        IconImage.Source = image;
        IconMark.Visibility = marked ? Visibility.Visible : Visibility.Collapsed;

        var own = node.Icon;
        var name = string.IsNullOrWhiteSpace(own) ? "Automatic icon"
            : own.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ? $"Icon {own.ToUpperInvariant()}"
            : $"Icon {Path.GetFileName(own.TrimEnd('\\', '/'))}";
        AutomationProperties.SetName(IconPreview, name);
        AutomationProperties.SetName(IconGlyph, name);
        AutomationProperties.SetName(IconImage, name);
        if (string.IsNullOrWhiteSpace(own) || (source is not null && GuideIconCache.Found(source))) return;
        IconMissing.Text = own.StartsWith("U+", StringComparison.OrdinalIgnoreCase)
            ? $"Segoe Fluent Icons has no {own.ToUpperInvariant()}, so the Guide shows no icon."
            : $"There's no icon at {own}, so the Guide shows none.";
        IconMissing.Visibility = Visibility.Visible;
    }

    /// Only what you typed is saved, to the row it was typed for.
    private void SaveIcon()
    {
        if (_iconShown is { } shown && IconBox.Text != shown.Text && Equals(Selected?.Node.Path, shown.Path)) SetIcon(IconBox.Text);
    }

    /// Saves a typed icon to its own row when the selection leaves it, queued as the title is.
    private void KeepTypedIcon()
    {
        if (!CanEdit || _iconShown is not { } shown || IconBox.Text == shown.Text) return;
        var text = IconBox.Text.Trim();
        if (text.Length > 0 && !ConfigValidator.IsWindowsIcon(text)) return;
        DispatcherQueue.TryEnqueue(() => Editor.Change("Change icon", c => c.SetIcon(shown.Path, text.Length == 0 ? null : text)));
    }

    public string? IconErrorText => IconError.Visibility == Visibility.Visible ? IconError.Text : null;

    public string? IconMissingText => IconMissing.Visibility == Visibility.Visible ? IconMissing.Text : null;

    /// The menu beside the field, for the self-test.
    public IEnumerable<string> IconMenuLabels => IconMenu.Items.OfType<MenuFlyoutItem>().Select(i => i.Text);

    /// The box as Tab leaves it, for the self-test.
    public void LeaveIconBox() => SaveIcon();

    private void OnIconLostFocus(object sender, RoutedEventArgs e) => SaveIcon();

    private void OnIconKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) SaveIcon();
    }
}
