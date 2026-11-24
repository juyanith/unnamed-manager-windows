using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace UnnamedManager.UI;

/// Fades a whole window, backdrop too, through its layered alpha (S-102). It never
/// activates it and never makes the caller wait. It ends by the clock, so a slow frame
/// can't keep a window up. No UI framework: both apps' Guides use it.
internal sealed partial class WindowFade
{
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _timer;

    private byte _from = 255, _to = 255;
    private long _began;
    private double _length;
    private Action? _done;

    public WindowFade(nint hwnd)
    {
        _hwnd = hwnd;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, GetWindowLongPtrW(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED);
        Set(255);
        _timer = DispatcherQueue.GetForCurrentThread()!.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(15);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Step();
    }

    public byte Alpha { get; private set; } = 255;

    /// On the way out: still drawn, and gone as far as the app is concerned.
    public bool IsLeaving { get; private set; }

    /// Before a hidden window shows: starts it clear, when there's a fade.
    public void Prepare(TimeSpan length)
    {
        IsLeaving = false;
        _timer.Stop();
        _done = null;
        _to = Alpha;
        Set(length > TimeSpan.Zero ? (byte)0 : (byte)255);
    }

    /// Fades in from wherever it is. A window that's fully shown, or on its way in, is left alone.
    public void In(TimeSpan length)
    {
        IsLeaving = false;
        if (_to == 255 && (_timer.IsRunning || Alpha == 255))
        {
            _done = null;
            return;
        }
        Start(255, length, null);
    }

    /// Fades out, then `done` hides it.
    public void Out(TimeSpan length, Action done)
    {
        if (IsLeaving) return;
        IsLeaving = true;
        Start(0, length, () =>
        {
            IsLeaving = false;
            done();
        });
    }

    private void Start(byte to, TimeSpan length, Action? done)
    {
        _timer.Stop();
        _from = Alpha;
        _to = to;
        _began = Environment.TickCount64;
        _length = length.TotalMilliseconds;
        _done = done;
        if (_length <= 0) Finish();
        else _timer.Start();
    }

    private void Step()
    {
        var part = (Environment.TickCount64 - _began) / _length;
        if (part >= 1) Finish();
        else Set((byte)Math.Round(_from + (_to - _from) * part));
    }

    private void Finish()
    {
        _timer.Stop();
        Set(_to);
        var done = _done;
        _done = null;
        done?.Invoke();
    }

    private void Set(byte alpha)
    {
        Alpha = alpha;
        SetLayeredWindowAttributes(_hwnd, 0, alpha, LWA_ALPHA);
    }

    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_LAYERED = 0x80000;
    private const uint LWA_ALPHA = 2;

    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport("user32.dll")] private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
}
