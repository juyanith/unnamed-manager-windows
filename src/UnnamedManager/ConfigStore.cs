using Microsoft.UI.Dispatching;
using UnnamedManager.Core.Config;

namespace UnnamedManager;

/// The config file and the one owner of reading, writing and watching it: read at
/// start (S-002) and on Reload config (S-008), keeping the last valid one; written by
/// the Bindings window and Settings (S-048); reloaded when another program changes it.
public sealed class ConfigStore
{
    private readonly ConfigState _state = new();
    private FileSystemWatcher? _watcher;
    private DispatcherQueueTimer? _debounce;

    /// The text last read or written, so the app's own saves don't reload twice.
    private string? _lastText;

    public ConfigStore(string path) => Path = path;

    public string Path { get; }

    /// The config in use, or null when the file has never loaded.
    public LoadedConfig? Current => _state.Current;

    /// The errors from the last read, empty when it loaded.
    public IReadOnlyList<ConfigIssue> Errors => _state.Errors;

    public ConfigState State => _state;

    /// True when this run wrote the starter config, until the next reload.
    public bool CreatedStarter { get; private set; }

    /// How many times the file was read because it changed on disk.
    public int WatchedReloads { get; private set; }

    /// Raised after every read or save, valid or not.
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

    /// Writes a config if it's valid, atomically (a temp file, then `File.Replace`),
    /// and starts using it. One with errors is refused and the file isn't touched;
    /// the errors come back.
    public IReadOnlyList<ConfigIssue> Save(Config config)
    {
        var text = ConfigWriter.Write(config);
        var result = ConfigLoader.Load(text);
        if (!result.IsValid)
        {
            AgentLog.Line($"CONFIG save refused: {result.Errors[0].Message}");
            return result.Errors;
        }
        if (text == _lastText && !_state.HasErrors) return [];
        try
        {
            var temp = Path + ".saving";
            File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
            if (File.Exists(Path)) File.Replace(temp, Path, null);
            else File.Move(temp, Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AgentLog.Line($"CONFIG can't save {Path}: {e.Message}");
            return [new ConfigIssue(Severity.Error, $"Can't save {Path}: {e.Message}")];
        }
        _lastText = text;
        CreatedStarter = false;
        _state.Apply(result, DateTimeOffset.Now);
        AgentLog.Line($"CONFIG saved {Path}");
        Changed?.Invoke();
        return [];
    }

    /// Reloads within a second of a change on disk. The watcher is on the folder, so a
    /// save through a temp file and a rename, as editors do, is seen too.
    public void Watch(DispatcherQueue ui)
    {
        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        _debounce = ui.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(300);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => ReadIfChanged();
        _watcher = new FileSystemWatcher(folder)
        {
            Filter = System.IO.Path.GetFileName(Path),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        FileSystemEventHandler changed = (_, _) => ui.TryEnqueue(() => { _debounce.Stop(); _debounce.Start(); });
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Renamed += (_, e) => changed(this, e);
        _watcher.EnableRaisingEvents = true;
    }

    private void ReadIfChanged()
    {
        string text;
        try { text = File.ReadAllText(Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        if (text == _lastText) return;
        WatchedReloads++;
        AgentLog.Line("CONFIG changed on disk");
        Reload();
    }

    private void Read()
    {
        LoadResult result;
        try
        {
            _lastText = File.ReadAllText(Path);
            result = ConfigLoader.Load(_lastText);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result = new LoadResult(null, [new ConfigIssue(Severity.Error, $"Can't read {Path}: {e.Message}")]);
        }
        _state.Apply(result, DateTimeOffset.Now);
        AgentLog.Line(result.IsValid
            ? $"CONFIG loaded {Path}: {result.Loaded!.Leaders.Count} top-level entries, {result.Loaded.Warnings.Count} warnings{(CreatedStarter ? " (starter written)" : "")}"
            : $"CONFIG error in {Path}: {result.Errors[0].Message}{(Current is null ? "" : " (the last valid config keeps running)")}");
        Changed?.Invoke();
    }
}
