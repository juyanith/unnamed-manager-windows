using Microsoft.UI.Dispatching;
using UnnamedManager.Core.Config;
using UnnamedManager.Core.Keyboard;

namespace UnnamedManager.Keyboard;

/// Captures leader keys while the app runs: the tracker decides, the hook
/// listens, and effects reach the UI thread through `Effect` (S-003, S-004).
public sealed class KeyService : IDisposable
{
    private readonly DispatcherQueue _ui;
    private KeyboardTracker? _tracker;
    private KeyboardHook? _hook;

    private readonly ForegroundWatcher _foreground = new();

    public KeyService(DispatcherQueue ui)
    {
        _ui = ui;
        _foreground.Changed += front => _tracker?.SetFrontApp(front);
    }

    /// The app in front now (S-014).
    public FrontApp? FrontApp => _foreground.Current;

    /// The app in front while it runs as administrator (S-056).
    public string? ElevatedApp => _foreground.ElevatedApp;

    /// Raised on the UI thread for every effect, in order.
    public event Action<SequenceEffect>? Effect;

    /// A key the recorder took (S-040), on the UI thread.
    public event Action<Chord>? Recorded;

    private bool _recording;

    /// Turns the key recorder on or off: while on, no leader matches.
    public bool Recording
    {
        get => _recording;
        set
        {
            _recording = value;
            if (_tracker is not null) _tracker.Recording = value;
        }
    }

    public bool IsCapturing => _hook?.IsInstalled == true;

    public KeyboardTracker? Tracker => _tracker;

    public KeyboardHook? Hook => _hook;

    /// The config the keys run with; its effects' menus and timing come from it.
    public LoadedConfig? Config { get; private set; }

    /// Starts capturing with this config, or swaps it in when already capturing.
    public bool Start(LoadedConfig config)
    {
        Config = config;
        if (_tracker is not null)
        {
            _tracker.ReplaceConfig(config);
            return IsCapturing;
        }
        _tracker = new KeyboardTracker(config);
        _tracker.SetFrontApp(_foreground.Current);
        // Runs on the hook thread: only post, never wait.
        _tracker.Recording = _recording;
        _hook = new KeyboardHook(_tracker, effect => _ui.TryEnqueue(() => Effect?.Invoke(effect)),
            chord => _ui.TryEnqueue(() => Recorded?.Invoke(chord)));
        var ok = _hook.Start();
        AgentLog.Line(ok ? "HOOK capturing leader keys" : "HOOK not capturing");
        return ok;
    }

    /// Stops capturing: the hook is removed and no key is consumed.
    public void Stop()
    {
        _hook?.Stop();
        _hook = null;
        _tracker = null;
    }

    public void Dispose() => Stop();
}
