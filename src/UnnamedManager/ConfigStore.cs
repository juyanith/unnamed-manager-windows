using UnnamedManager.Core.Config;

namespace UnnamedManager;

/// The config the app runs on: the file at AppPaths.ConfigFile, read at start
/// (S-002) and on Reload config (S-008), keeping the last valid one.
public sealed class ConfigStore
{
    private readonly ConfigState _state = new();

    public ConfigStore(string path) => Path = path;

    public string Path { get; }

    /// The config in use, or null when the file has never loaded.
    public LoadedConfig? Current => _state.Current;

    /// The errors from the last read, empty when it loaded.
    public IReadOnlyList<ConfigIssue> Errors => _state.Errors;

    public ConfigState State => _state;

    /// True when this run wrote the starter config, until the next reload.
    public bool CreatedStarter { get; private set; }

    /// Raised after every read, valid or not.
    public event Action? Changed;

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
        Read();
    }

    /// Reads the file again. With errors, the last valid config keeps running.
    public void Reload()
    {
        CreatedStarter = false;
        Read();
    }

    private void Read()
    {
        var result = ConfigLoader.LoadFile(Path);
        _state.Apply(result, DateTimeOffset.Now);
        AgentLog.Line(result.IsValid
            ? $"CONFIG loaded {Path}: {result.Loaded!.Leaders.Count} top-level entries, {result.Loaded.Warnings.Count} warnings{(CreatedStarter ? " (starter written)" : "")}"
            : $"CONFIG error in {Path}: {result.Errors[0].Message}{(Current is null ? "" : " (the last valid config keeps running)")}");
        Changed?.Invoke();
    }
}
