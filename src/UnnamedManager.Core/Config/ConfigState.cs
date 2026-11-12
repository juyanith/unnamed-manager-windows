namespace UnnamedManager.Core.Config;

/// The config the app runs on across reloads (S-008): a reload with errors keeps
/// the last valid config running and reports the errors.
public sealed class ConfigState
{
    /// The config in use, or null when the file has never loaded.
    public LoadedConfig? Current { get; private set; }

    /// The errors from the last read, empty when it loaded.
    public IReadOnlyList<ConfigIssue> Errors { get; private set; } = [];

    /// When the config in use was loaded.
    public DateTimeOffset? LoadedAt { get; private set; }

    public bool HasErrors => Errors.Count > 0;

    /// Takes in a new read. Returns true when the config in use changed.
    public bool Apply(LoadResult result, DateTimeOffset now)
    {
        Errors = result.Errors;
        if (result.Loaded is not { } loaded) return false;
        Current = loaded;
        LoadedAt = now;
        return true;
    }

    /// The first error with its entry path, and how many more there are:
    /// ("Binding “Hyper+Space Q” uses action “open-foo”, which isn't defined.", 2).
    public (string First, int More)? ErrorSummary =>
        Errors.Count == 0 ? null : (Errors[0].Message, Errors.Count - 1);
}
