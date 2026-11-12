namespace UnnamedManager.Core.Config;

/// The app in front: its program's full path, its AppUserModelID when it has one,
/// and the name the Guide shows ("Notepad").
public sealed record FrontApp(string? ExePath, string? AppId, string Name)
{
    /// Whether a context's app name means this app: a name ending in `.exe` is an exe
    /// (file name or full path), any other an AppUserModelID (spec v4, Contexts).
    public bool Is(string app)
    {
        app = app.Trim();
        if (UseApplicationAction.IsExe(app)) return ExePath is { } path && new UseApplicationAction(app, null).MatchesExe(path);
        return AppId is { } id && string.Equals(id, app, StringComparison.OrdinalIgnoreCase);
    }
}

/// Picks what Context Aware runs for the app in front (spec, Actions, contextual).
public static class ContextResolver
{
    /// The action to run, or null when the action is unavailable for this app. Other
    /// kinds of action come back as they are.
    public static ConfigAction? Resolve(ConfigAction action, FrontApp? front, IReadOnlyList<ConfigContext> contexts)
    {
        if (action is not ContextualAction contextual) return action;
        if (front is not null)
        {
            // The first context, in `contexts` order, that has the app and a variant.
            foreach (var context in contexts)
            {
                if (!context.Apps.Any(front.Is)) continue;
                if (contextual.Variants.FirstOrDefault(v => v.Key == context.Id).Value is { } variant) return variant;
            }
        }
        return contextual.Fallback;
    }

    /// False for a Context Aware binding with nothing to run for this app.
    public static bool IsAvailable(this LoadedConfig config, BindingNode binding, FrontApp? front) =>
        binding.Action is not { } id || config.Config.Action(id) is not { } action
        || Resolve(action, front, config.Config.Contexts) is not null;
}
