namespace UnnamedManager.Core.Config;

/// Where a Guide row's icon comes from (S-060). The app turns each into an image.
public abstract record IconSource
{
    /// The cache key: one icon per source, whatever binding uses it.
    public abstract string Key { get; }

    /// An exe as the config names it: a file name found on the PATH or in App Paths, or a path.
    public sealed record Exe(string Name) : IconSource
    {
        public override string Key => $"exe|{Name.ToLowerInvariant()}";
    }

    /// An app by its AppUserModelID: the icon of its Start entry.
    public sealed record AppId(string Id) : IconSource
    {
        public override string Key => $"app|{Id.ToLowerInvariant()}";
    }

    /// A file or folder: the icon the shell shows for it.
    public sealed record File(string Path) : IconSource
    {
        public override string Key => $"file|{Path.ToLowerInvariant()}";
    }

    /// A glyph of Windows' icon font, which the app looks up with the icons (S-093).
    public sealed record Glyph(string Text) : IconSource
    {
        public override string Key => $"glyph|{Text}";
    }

    /// A URL: the icon of the app that opens its scheme (the browser, for https).
    public sealed record Url(string Scheme) : IconSource
    {
        public override string Key => $"url|{Scheme.ToLowerInvariant()}";
    }
}

public static class GuideIcons
{
    /// An entry's own icon (S-093): `U+` and 4 to 6 hex digits is a glyph, anything else a
    /// file, with a leading ~ as the home folder. Null without one, or for a code point
    /// that's no character.
    public static IconSource? Own(string? icon, string home)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        if (icon.StartsWith("U+", StringComparison.OrdinalIgnoreCase) && icon.Length is >= 6 and <= 8
            && int.TryParse(icon[2..], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var code))
            return code is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? new IconSource.Glyph(char.ConvertFromUtf32(code)) : null;
        return new IconSource.File(new PlanContext(home).Expand(icon.Trim()));
    }

    /// A row's icon: the entry's own, which wins even when there's nothing to show for it,
    /// else its action's app, file or link.
    public static IconSource? For(BindingNode node, Config config, string home) =>
        !string.IsNullOrWhiteSpace(node.Icon) ? Own(node.Icon, home)
        : node.Action is { } id ? For(config.Resolve(id), home)
        : null;

    /// The icon source for a binding's action, or null: Use Application shows the app,
    /// Open the file, folder or URL's app. Window actions keep their glyph (S-045).
    public static IconSource? For(ConfigAction? action, string home) => action switch
    {
        UseApplicationAction { Exe: { } exe } when exe.Trim().Length > 0 => new IconSource.Exe(exe.Trim()),
        UseApplicationAction { AppId: { } id } when id.Trim().Length > 0 => new IconSource.AppId(id.Trim()),
        OpenAction { Target: var target } when new ActionPlan.Open(target).IsUrl => new IconSource.Url(new Uri(target).Scheme),
        OpenAction { Target: var target } when target.Trim().Length > 0 => new IconSource.File(new PlanContext(home).Expand(target.Trim())),
        _ => null,
    };

    /// Every icon source a config's bindings use, once each, for loading when it loads.
    public static IReadOnlyList<IconSource> All(LoadedConfig config, string home)
    {
        var seen = new HashSet<string>();
        var sources = new List<IconSource>();
        void Walk(BindingNode node)
        {
            if (For(node, config.Config, home) is { } source && seen.Add(source.Key)) sources.Add(source);
            foreach (var child in node.Children) Walk(child);
        }
        foreach (var leader in config.Leaders) Walk(leader);
        return sources;
    }

}
