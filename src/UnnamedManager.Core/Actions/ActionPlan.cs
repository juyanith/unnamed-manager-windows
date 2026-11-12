namespace UnnamedManager.Core.Config;

/// One thing to do in the world, worked out from an action with no side effects.
/// The app carries it out (ActionRunner), so every rule here is unit-tested and the
/// self-test can check plans without running them.
public abstract record ActionPlan
{
    /// Use Application: bring an app forward, start it, or both, by its mode.
    public sealed record UseApp(UseApplicationAction App) : ActionPlan;

    /// Open a file, folder or URL with its default app, as double-clicking it would.
    public sealed record Open(string Target) : ActionPlan
    {
        /// A URL or another protocol ("https://…", "mailto:…"), not a path.
        public bool IsUrl => Uri.TryCreate(Target, UriKind.Absolute, out var uri) && !uri.IsFile && uri.Scheme.Length > 1;
    }
}
