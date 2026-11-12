using UnnamedManager.Core.Config;

namespace UnnamedManager;

/// The config the app runs on: the file at AppPaths.ConfigFile, read at start (S-002).
public sealed class ConfigStore
{
    public ConfigStore(string path) => Path = path;

    public string Path { get; }

    /// The config in use, or null when the file has never loaded.
    public LoadedConfig? Current { get; private set; }

    /// The errors from the last read, empty when it loaded.
    public IReadOnlyList<ConfigIssue> Errors { get; private set; } = [];

    /// True when this run wrote the starter config, so the tray menu can say so.
    public bool CreatedStarter { get; private set; }

    /// Writes the starter config when there's no file, then reads the file.
    public void Load()
    {
        try
        {
            CreatedStarter = ConfigLoader.WriteStarterIfMissing(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AgentLog.Line($"CONFIG can't write the starter config at {Path}: {e.Message}");
        }
        var result = ConfigLoader.LoadFile(Path);
        Errors = result.Errors;
        if (result.Loaded is { } loaded) Current = loaded;
        AgentLog.Line(result.IsValid
            ? $"CONFIG loaded {Path}: {result.Loaded!.Leaders.Count} top-level entries, {result.Loaded.Warnings.Count} warnings{(CreatedStarter ? " (starter written)" : "")}"
            : $"CONFIG error in {Path}: {Errors[0].Message}");
    }
}
