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

    public KeyService(DispatcherQueue ui) => _ui = ui;

    /// Raised on the UI thread for every effect, in order.
    public event Action<SequenceEffect>? Effect;

    public bool IsCapturing => _hook?.IsInstalled == true;

    public KeyboardTracker? Tracker => _tracker;

    public KeyboardHook? Hook => _hook;

    /// Starts capturing with this config, or swaps it in when already capturing.
    public bool Start(LoadedConfig config)
    {
        if (_tracker is not null)
        {
            _tracker.ReplaceConfig(config);
            return IsCapturing;
        }
        _tracker = new KeyboardTracker(config);
        // Runs on the hook thread: only post, never wait.
        _hook = new KeyboardHook(_tracker, effect => _ui.TryEnqueue(() => Effect?.Invoke(effect)));
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
