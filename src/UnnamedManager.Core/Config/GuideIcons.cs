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

    /// A URL: the icon of the app that opens its scheme (the browser, for https).
    public sealed record Url(string Scheme) : IconSource
    {
        public override string Key => $"url|{Scheme.ToLowerInvariant()}";
    }
}

public static class GuideIcons
{
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
            if (node.Action is { } id && For(config.Config.Resolve(id), home) is { } source && seen.Add(source.Key)) sources.Add(source);
            foreach (var child in node.Children) Walk(child);
        }
        foreach (var leader in config.Leaders) Walk(leader);
        return sources;
    }

}
