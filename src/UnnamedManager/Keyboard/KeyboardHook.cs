using System.Runtime.InteropServices;
using UnnamedManager.Core.Keyboard;

namespace UnnamedManager.Keyboard;

/// The low-level keyboard hook (S-003). It runs on its own thread with its own
/// message loop, so a busy UI thread never delays a keystroke anywhere. Windows
/// removes a hook whose callback is slow (LowLevelHooksTimeout) without telling
/// it, so the callback only asks the tracker, injects the mask when needed, and
/// hands effects to `onEffect`, which must return at once (post to the UI thread).
public sealed partial class KeyboardHook : IDisposable
{
    /// dwExtraInfo on every event the app injects, so the hook lets them pass.
    public const nuint Marker = 0x554E4D47; // "UNMG"

    /// An unassigned virtual key. Pressed while Win or Alt is held, it keeps
    /// their key-up from opening Start, the Office hub or a menu bar.
    public const ushort MaskKey = 0xE8;

    private static KeyboardHook? _active;

    private readonly KeyboardTracker _tracker;
    private readonly Action<SequenceEffect> _onEffect;
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private readonly ManualResetEventSlim _started = new();

    public KeyboardHook(KeyboardTracker tracker, Action<SequenceEffect> onEffect)
    {
        _tracker = tracker;
        _onEffect = onEffect;
    }

    public bool IsInstalled => _hook != 0;

    /// Installs the hook on a new thread. Returns false when Windows refuses it.
    public bool Start()
    {
        if (_thread is not null) return IsInstalled;
        _active = this;
        _started.Reset();
        _thread = new Thread(Run) { IsBackground = true, Name = "Keyboard hook", Priority = ThreadPriority.Highest };
        _thread.Start();
        _started.Wait();
        if (!IsInstalled) { _thread = null; _active = null; }
        return IsInstalled;
    }

    /// Removes the hook and ends its thread. Any sequence ends.
    public void Stop()
    {
        if (_thread is null) return;
        PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        _thread.Join(2000);
        _thread = null;
        _active = null;
        _tracker.Reset();
    }

    public void Dispose() => Stop();

    private unsafe void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, &Callback, GetModuleHandleW(null), 0);
        if (_hook == 0) AgentLog.Line($"HOOK Windows refused the keyboard hook (error {Marshal.GetLastPInvokeError()})");
        _started.Set();
        if (_hook == 0) return;
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(msg);
            DispatchMessageW(msg);
        }
        UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    [UnmanagedCallersOnly]
    private static unsafe nint Callback(int code, nuint wParam, nint lParam)
    {
        if (code >= 0 && _active is { } hook)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam;
            if (hook.Decide(*info)) return 1;
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// The whole of the callback's work. Public so the self-test can time it.
    public bool Decide(KBDLLHOOKSTRUCT info)
    {
        var raw = new RawKey((int)info.vkCode, (int)info.scanCode, (info.flags & LLKHF_EXTENDED) != 0,
            (info.flags & LLKHF_UP) != 0, info.dwExtraInfo == Marker);
        var decision = _tracker.Process(raw);
        if (decision.SendMask) SendMask();
        if (decision.Effect is { } effect) _onEffect(effect);
        return decision.Consume;
    }

    /// Presses and lets go of the masking key, marked as the app's own.
    private static void SendMask()
    {
        Span<INPUT> inputs =
        [
            new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = MaskKey, dwExtraInfo = Marker } },
            new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = MaskKey, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = Marker } },
        ];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// Presses or lets go of a key by its scan code, as a keyboard would (not
    /// marked as the app's own), so the self-test can drive the real hook.
    public static void InjectForTest(Core.Config.KeyName key, bool up)
    {
        var code = Core.Config.Keys.ScanCode(key);
        var flags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0) | (code > 0xFF ? KEYEVENTF_EXTENDEDKEY : 0);
        Span<INPUT> input = [new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wScan = (ushort)(code & 0xFF), dwFlags = flags } }];
        SendInput(1, input, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nuint dwExtraInfo;
    }

    // INPUT is a union; the mouse member is the largest, so pad to its size.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX, ptY;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const uint WM_QUIT = 0x0012, INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008;
    public const uint LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80;

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial nint SetWindowsHookExW(int idHook, delegate* unmanaged<int, nuint, nint, nint> proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(in MSG msg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, Span<INPUT> inputs, int size);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
