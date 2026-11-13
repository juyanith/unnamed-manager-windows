using System.Diagnostics;
using System.Runtime.InteropServices;
using UnnamedManager.Core.Keyboard;

namespace UnnamedManager.Keyboard;

/// Keeps the hook working (S-055). A message-only window on a thread of its own gets:
/// - resume from sleep and session unlock, after which Windows may have dropped the hook;
/// - a timer that runs the watchdog every two seconds: when `GetLastInputInfo` saw input
///   but the hook saw none, it sends the mask key, marked as the app's own, and installs
///   the hook again if that doesn't arrive within 250 ms.
/// It asks the hook's thread to install the hook again with a thread message. Each time
/// is logged, and so is each callback slower than 50 ms. (Raw input would see keys
/// without a probe, but registering it, on any thread, stopped the hook seeing them.)
public sealed partial class KeyboardHook
{
    private const uint WM_TIMER = 0x0113, WM_POWERBROADCAST = 0x0218, WM_WTSSESSION_CHANGE = 0x02B1;
    private const uint WM_APP_CHECK = 0x8001, WM_APP_DROP = 0x8002, WM_APP_REINSTALL = 0x8003;
    private static readonly string[] Reasons = ["the PC woke from sleep", "the session was unlocked", "the hook missed its test key"];
    private const nuint PBT_APMRESUMEAUTOMATIC = 0x12, WTS_SESSION_UNLOCK = 0x8;
    private const uint WatchdogMs = 2000, ProbeMs = 250;
    private const nuint WatchTimer = 1, ProbeTimer = 2;
    private static readonly nint HWND_MESSAGE = -3;

    private readonly HookWatchdog _watchdog = new();
    private nint _window, _powerNotify;
    private Thread? _guard;
    private uint _guardThreadId;
    private readonly ManualResetEventSlim _guardReady = new();
    // Hook calls since the last check; the last input time the guard saw; calls before a probe.
    private long _hookCalls, _probeCalls;
    private uint _seenInput;
    private RawKey _lastKey;
    private HookDecision _lastDecision;
    private volatile int _reinstalls;
    private volatile string? _lastReinstall, _lastSlow;
    private long _callsTotal;

    /// How many times the hook was installed again, and why the last time.
    public int Reinstalls => _reinstalls;
    public string? LastReinstall => _lastReinstall;

    /// The last slow-callback log line.
    public string? LastSlow => _lastSlow;

    /// Every hook call since it started.
    public long Calls => Interlocked.Read(ref _callsTotal);

    /// Self-test only: extra work inside the real callback, to make it slow.
    public static Action? SlowForTest { get; set; }

    // The self-test sends the window what Windows would.
    public void SimulateWake() => Post(WM_POWERBROADCAST, PBT_APMRESUMEAUTOMATIC);
    public void SimulateUnlock() => Post(WM_WTSSESSION_CHANGE, WTS_SESSION_UNLOCK);
    /// Removes the hook the way Windows does: without the app knowing.
    public void DropForTest() => PostThreadMessageW(_threadId, WM_APP_DROP, 0, 0);
    public void CheckNow() => Post(WM_APP_CHECK, 0);

    private void Post(uint message, nuint wParam) => PostMessageW(_window, message, wParam, 0);

    private void StartGuard()
    {
        _guardReady.Reset();
        _guard = new Thread(RunGuard) { IsBackground = true, Name = "Keyboard hook guard" };
        _guard.Start();
        _guardReady.Wait();
    }

    private void StopGuard()
    {
        if (_guard is null) return;
        PostThreadMessageW(_guardThreadId, WM_QUIT, 0, 0);
        _guard.Join(2000);
        _guard = null;
    }

    private void RunGuard()
    {
        _guardThreadId = GetCurrentThreadId();
        OpenGuardWindow();
        _guardReady.Set();
        if (_window == 0) return;
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(msg);
            DispatchMessageW(msg);
        }
        CloseGuardWindow();
    }

    private unsafe void OpenGuardWindow()
    {
        var module = GetModuleHandleW(null);
        fixed (char* name = "UnnamedManagerHookGuard")
        {
            var cls = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = module, lpszClassName = name };
            RegisterClassExW(cls); // fails harmlessly once the class exists
            _window = CreateWindowExW(0, name, null, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, module, 0);
        }
        if (_window == 0) { Log($"Keyboard hook guard has no window (error {Marshal.GetLastPInvokeError()}); the hook isn't watched"); return; }
        _powerNotify = RegisterSuspendResumeNotification(_window, 0);
        WTSRegisterSessionNotification(_window, 0);
        _seenInput = LastInput();
        SetTimer(_window, WatchTimer, WatchdogMs, 0);
    }

    private void CloseGuardWindow()
    {
        KillTimer(_window, WatchTimer);
        KillTimer(_window, ProbeTimer);
        WTSUnRegisterSessionNotification(_window);
        if (_powerNotify != 0) UnregisterSuspendResumeNotification(_powerNotify);
        DestroyWindow(_window);
        _window = 0;
    }

    /// The callback's bookkeeping: counts for the watchdog, and the time it took.
    private bool Timed(in KBDLLHOOKSTRUCT info)
    {
        Interlocked.Increment(ref _hookCalls);
        Interlocked.Increment(ref _callsTotal);
        var start = Stopwatch.GetTimestamp();
        var consume = Decide(info);
        SlowForTest?.Invoke();
        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (HookWatchdog.IsSlow(ms)) Log(_lastSlow = HookWatchdog.SlowLine(ms, _lastKey, _lastDecision));
        return consume;
    }

    private void Watch()
    {
        var input = LastInput();
        var inputSince = input != _seenInput;
        _seenInput = input;
        if (_watchdog.Check(inputSince, Interlocked.Exchange(ref _hookCalls, 0)) != WatchStep.Probe) return;
        _probeCalls = Calls;
        SendMaskKey();
        // The probe is input too; the next check mustn't count it.
        _seenInput = LastInput();
        SetTimer(_window, ProbeTimer, ProbeMs, 0);
    }

    private void ProbeDone()
    {
        KillTimer(_window, ProbeTimer);
        if (_watchdog.ProbeAnswered(seen: Calls > _probeCalls)) AskReinstall(2);
    }

    private static uint LastInput()
    {
        var info = new LASTINPUTINFO { cbSize = 8 };
        return GetLastInputInfo(ref info) ? info.dwTime : 0;
    }

    private void AskReinstall(int reason) => PostThreadMessageW(_threadId, WM_APP_REINSTALL, (nuint)reason, 0);

    /// On the hook's thread: a request from the guard. True when it was one.
    private bool OnGuardMessage(uint message, nuint wParam)
    {
        switch (message)
        {
            case WM_APP_REINSTALL: Reinstall(Reasons[(int)wParam]); return true;
            case WM_APP_DROP: UnhookWindowsHookEx(_hook); return true;
            default: return false;
        }
    }

    /// Installs the hook again, on the hook's thread; a sequence in progress carries on.
    private unsafe void Reinstall(string reason)
    {
        if (_hook != 0) UnhookWindowsHookEx(_hook);
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, &Callback, GetModuleHandleW(null), 0);
        _lastReinstall = reason;
        _reinstalls++;
        Log(_hook != 0 ? $"Keyboard hook installed again: {reason}" : $"Keyboard hook couldn't be installed again after {reason} (error {Marshal.GetLastPInvokeError()})");
    }

    /// Logs off the hook thread, which must never wait on a file.
    private static void Log(string line) => ThreadPool.QueueUserWorkItem(_ =>
    {
        AppLog.Write(line);
        AgentLog.Line("HOOK " + line);
    });

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (_active is { } hook && hwnd == hook._window)
        {
            switch (message)
            {
                case WM_TIMER when wParam == ProbeTimer: hook.ProbeDone(); return 0;
                case WM_TIMER or WM_APP_CHECK: hook.Watch(); return 0;
                case WM_POWERBROADCAST when wParam == PBT_APMRESUMEAUTOMATIC: hook.AskReinstall(0); return 1;
                case WM_WTSSESSION_CHANGE when wParam == WTS_SESSION_UNLOCK: hook.AskReinstall(1); return 0;
            }
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public delegate* unmanaged<nint, uint, nuint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize, dwTime;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LASTINPUTINFO info);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(in WNDCLASSEXW cls);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hwnd, uint message, nuint wParam, nint lParam);


    [LibraryImport("user32.dll")]
    private static partial nint RegisterSuspendResumeNotification(nint recipient, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterSuspendResumeNotification(nint handle);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSRegisterSessionNotification(nint hwnd, uint flags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSUnRegisterSessionNotification(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillTimer(nint hwnd, nuint id);
}
