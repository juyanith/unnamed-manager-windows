using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Keyboard;

/// What Narrator hears as the Guide changes, for both apps (S-099, S-100).
public static class GuideSpeech
{
    /// The words for a list with nothing in it, on screen and spoken.
    public const string EmptyList = "No recent windows";

    /// A menu opened: "Apps, 4 keys", or held, "Apps, Terminal, 1 of 4".
    public static string Opened(BindingNode menu, int? highlight)
    {
        if (highlight is { } h) return $"{menu.Title}, {Moved(menu, h)}";
        var title = string.Join(", ", menu.PathTitles);
        if (menu.List is not null && menu.Items.Count == 0) return $"{title}, {EmptyList}";
        return $"{title}, {menu.Children.Count} {(menu.Children.Count == 1 ? "key" : "keys")}";
    }

    /// The highlight moved: "Browser, 2 of 4", counted across a list's pages. Cancel isn't
    /// counted: "Cancel", or "Cancel, <title>" in a list, whose Cancel is the focused window.
    public static string Moved(BindingNode menu, int highlight)
    {
        if (highlight <= 0 || highlight > menu.Children.Count)
            return menu.List is not null && menu.Front is { } front ? $"Cancel, {front.Title}" : "Cancel";
        var (at, count) = menu.List is null
            ? (highlight, menu.Children.Count)
            : (menu.Page * BindingNode.PageSize + highlight, menu.Items.Count);
        return $"{menu.Children[highlight - 1].Title}, {at} of {count}";
    }
}
