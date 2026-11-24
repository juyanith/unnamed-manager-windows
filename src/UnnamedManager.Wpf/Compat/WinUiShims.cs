// The few WinUI names the shared app files use, built on WPF, so those files compile in
// both apps unchanged: KeyService and ConfigStore post to the UI thread and start timers
// through DispatcherQueue, and GuideIconCache keeps ImageSource values. Only what those
// files call is here. New shared code should take no UI framework at all instead.

global using ImageSource = System.Windows.Media.ImageSource;

using System.Windows.Threading;

namespace Microsoft.UI.Xaml.Media
{
    // Empty: GuideIconCache imports it. ImageSource comes from the global alias above.
}

namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low, Normal, High }

    /// WinUI's DispatcherQueue on a WPF Dispatcher.
    public sealed class DispatcherQueue
    {
        private readonly Dispatcher _dispatcher;

        private DispatcherQueue(Dispatcher dispatcher) => _dispatcher = dispatcher;

        /// The queue of the calling thread, or null on a thread with no dispatcher.
        public static DispatcherQueue? GetForCurrentThread() =>
            Dispatcher.FromThread(Thread.CurrentThread) is { } d ? new DispatcherQueue(d) : null;

        public bool TryEnqueue(Action callback) => TryEnqueue(DispatcherQueuePriority.Normal, callback);

        public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
        {
            if (_dispatcher.HasShutdownStarted) return false;
            _dispatcher.BeginInvoke(callback, priority switch
            {
                DispatcherQueuePriority.Low => DispatcherPriority.Background,
                DispatcherQueuePriority.High => DispatcherPriority.Send,
                _ => DispatcherPriority.Normal,
            });
            return true;
        }

        public DispatcherQueueTimer CreateTimer() => new(_dispatcher);
    }

    /// WinUI's DispatcherQueueTimer on a WPF DispatcherTimer. Repeats unless told not to.
    public sealed class DispatcherQueueTimer
    {
        private readonly DispatcherTimer _timer;

        internal DispatcherQueueTimer(Dispatcher dispatcher)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
            _timer.Tick += (_, _) =>
            {
                if (!IsRepeating) _timer.Stop();
                Tick?.Invoke(this, EventArgs.Empty);
            };
        }

        public TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }

        public bool IsRepeating { get; set; } = true;

        public bool IsRunning => _timer.IsEnabled;

        public event Action<DispatcherQueueTimer, object>? Tick;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();
    }
}
