namespace UnnamedManager.Core.Keyboard;

/// What the watchdog does after a check.
public enum WatchStep
{
    Nothing,
    /// Send the hook a key of the app's own and see whether it arrives.
    Probe,
}

/// Decides when the keyboard hook was lost (S-055). Windows removes a low-level hook
/// without telling the app, for example after its callback ran past the system's
/// timeout. `GetLastInputInfo` says input happened, but counts the mouse too, so input
/// with no hook call only raises a doubt: the app then sends a marked key (the mask key)
/// and the hook is lost when that doesn't arrive.
public sealed class HookWatchdog
{
    /// A callback slower than this is logged.
    public const double SlowMs = 50;

    private bool _reinstalled;

    /// Every check: was there input since the last one, and how many hook calls.
    public WatchStep Check(bool inputSince, long hookCalls)
    {
        if (hookCalls > 0) { _reinstalled = false; return WatchStep.Nothing; }
        return inputSince && !_reinstalled ? WatchStep.Probe : WatchStep.Nothing;
    }

    /// The probe's answer. True when the hook should be installed again: once per
    /// outage, since while an app that hides keys from the hook (one running as
    /// administrator) is in front, installing again every check would only fill the log.
    public bool ProbeAnswered(bool seen)
    {
        if (seen) { _reinstalled = false; return false; }
        if (_reinstalled) return false;
        _reinstalled = true;
        return true;
    }

    public static bool IsSlow(double ms) => ms > SlowMs;

    /// The log line for a slow callback: how long, which key, and what it decided.
    public static string SlowLine(double ms, RawKey key, HookDecision decision)
    {
        var doing = decision.Effect is { } effect ? effect.GetType().Name
            : decision.Recorded is not null ? "Recorded"
            : decision.Consume ? "Consumed" : "Passed";
        return $"Keyboard hook callback took {ms:0} ms on key {key.Vk:X2} {(key.IsUp ? "up" : "down")}: {doing}{(decision.SendMask ? ", sent the mask key" : "")}";
    }
}
